using System.Text.Json;
using HardwareModularWorkflow.Workflow.Abstractions;
using HardwareModularWorkflow.Workflow.Models;
using HardwareModularWorkflow.Workflow.Engine;
using HardwareModularWorkflow.Workflow.Results;
using HardwareModularWorkflow.Workflow.Resources;
using HardwareModularWorkflow.Workflow.Scheduling;
using ExecutionContext = HardwareModularWorkflow.Workflow.Abstractions.ExecutionContext;

namespace HardwareModularWorkflow.Workflow.Engine;

/// <summary>
/// 工作流调度引擎：全局任务调度器
/// 
/// 核心设计：
/// - 有界优先级队列保存待执行工作项，基础优先级相同时按 FIFO，等待老化防止饥饿
/// - SemaphoreSlim(50) 限制全局并发数
/// - CancellationToken 控制整个调度器生命周期
/// - 嵌套流复用父运行实例的调度槽，避免父项占槽等待子项造成饥饿
/// - 不做嵌套层级限制，运行时通过 ExecutionPath 检测循环
/// </summary>
public sealed class WorkflowScheduler : IAsyncDisposable
{
    private readonly int _maxConcurrency;
    private readonly IStepExecutor _stepExecutor;
    private readonly IFlowResolver? _flowResolver;
    private readonly ModuleExecutor _moduleExecutor;
    private readonly FlowExecutor _flowExecutor;
    private readonly object _queueSync = new();
    private readonly List<QueuedWorkItem> _workQueue = new();
    private readonly SemaphoreSlim _workAvailable = new(0);
    private readonly SemaphoreSlim? _queueSlots;
    private readonly SemaphoreSlim _concurrencyLimit;
    private readonly CancellationTokenSource _globalCts = new();
    private readonly List<Task> _runningTasks = new();
    private readonly SemaphoreSlim _taskListLock = new(1, 1);
    private readonly object _subFlowInvocationSync = new();
    private readonly Dictionary<long, List<ActiveSubFlowInvocation>> _activeSubFlows = new();
    private readonly Dictionary<long, SemaphoreSlim> _exclusiveSubFlowGates = new();
    private readonly ISchedulingEventLog _eventLog;
    private readonly int _schedulerAgingStepSeconds;
    private readonly int _schedulerMaxAgingBoost;
    private long _enqueueSequence;
    private long _acceptedWorkItems;
    private long _startedWorkItems;
    private long _completedWorkItems;
    private long _failedWorkItems;
    private long _cancelledWorkItems;
    private long _totalQueueWaitTicks;
    private Task? _dispatcherTask;
    private bool _acceptingWork = true;
    private bool _isDisposed;
    private bool _isStarted;

    #region 状态属性

    /// <summary>调度器是否已启动</summary>
    public bool IsRunning => _isStarted && !_isDisposed;

    /// <summary>最大并发槽位数</summary>
    public int MaxConcurrency => _maxConcurrency;

    /// <summary>当前可用并发槽位数</summary>
    public int CurrentAvailableSlots => _concurrencyLimit.CurrentCount;

    /// <summary>当前正在执行的任务数</summary>
    public int CurrentRunningTasks
    {
        get
        {
            // Semaphore 的剩余数 + 已用数 = 总数，但需确保不小于 0
            var running = _maxConcurrency - _concurrencyLimit.CurrentCount;
            return running < 0 ? 0 : running;
        }
    }

    /// <summary>队列中待执行的工作项数（有界队列时可用）</summary>
    public int QueuedWorkItems
    {
        get { lock (_queueSync) return _workQueue.Count; }
    }

    /// <summary>返回当前并发槽、队列长度、吞吐及等待时长指标。</summary>
    public WorkflowSchedulerSnapshot GetSnapshot()
    {
        TimeSpan oldestQueueWait;
        lock (_queueSync)
        {
            oldestQueueWait = _workQueue.Count == 0
                ? TimeSpan.Zero
                : DateTime.UtcNow - _workQueue.Min(item => item.EnqueuedAtUtc);
        }

        var started = Interlocked.Read(ref _startedWorkItems);
        var totalWaitTicks = Interlocked.Read(ref _totalQueueWaitTicks);
        return new WorkflowSchedulerSnapshot(
            _maxConcurrency,
            CurrentRunningTasks,
            QueuedWorkItems,
            CurrentAvailableSlots,
            Interlocked.Read(ref _acceptedWorkItems),
            started,
            Interlocked.Read(ref _completedWorkItems),
            Interlocked.Read(ref _failedWorkItems),
            Interlocked.Read(ref _cancelledWorkItems),
            started == 0 ? TimeSpan.Zero : TimeSpan.FromTicks(totalWaitTicks / started),
            oldestQueueWait);
    }

    #endregion

    /// <summary>
    /// 创建调度器
    /// </summary>
    /// <param name="stepExecutor">步骤执行器（由 Core 层注入实现）</param>
    /// <param name="flowResolver">流解析器（可选，用于子流解析）</param>
    /// <param name="maxConcurrency">最大并发工作项数（默认 50）</param>
    /// <param name="channelCapacity">队列容量（默认 1000，0 表示无界）</param>
    public WorkflowScheduler(
        IStepExecutor stepExecutor,
        IFlowResolver? flowResolver = null,
        int maxConcurrency = 50,
        int channelCapacity = 1000,
        IResourceReservationManager? resourceReservationManager = null,
        ISchedulingEventLog? eventLog = null,
        int schedulerAgingStepSeconds = 5,
        int schedulerMaxAgingBoost = 20)
    {
        if (maxConcurrency <= 0) throw new ArgumentOutOfRangeException(nameof(maxConcurrency));
        if (schedulerAgingStepSeconds <= 0) throw new ArgumentOutOfRangeException(nameof(schedulerAgingStepSeconds));
        if (schedulerMaxAgingBoost < 0) throw new ArgumentOutOfRangeException(nameof(schedulerMaxAgingBoost));

        _maxConcurrency = maxConcurrency;
        _stepExecutor = stepExecutor ?? throw new ArgumentNullException(nameof(stepExecutor));
        _flowResolver = flowResolver;
        _moduleExecutor = new ModuleExecutor(stepExecutor, resourceReservationManager);
        _flowExecutor = new FlowExecutor(_moduleExecutor, resourceReservationManager);
        _concurrencyLimit = new SemaphoreSlim(maxConcurrency, maxConcurrency);
        _queueSlots = channelCapacity > 0 ? new SemaphoreSlim(channelCapacity, channelCapacity) : null;
        _eventLog = eventLog ?? NullSchedulingEventLog.Instance;
        _schedulerAgingStepSeconds = schedulerAgingStepSeconds;
        _schedulerMaxAgingBoost = schedulerMaxAgingBoost;
    }

    #region 启动与停止

    /// <summary>
    /// 启动调度器：开始从队列取任务并执行
    /// 可多次调用（幂等），但仅启动一次后台循环
    /// </summary>
    public void Start()
    {
        if (_isStarted) return;
        _isStarted = true;
        _dispatcherTask = Task.Run(DispatchLoopAsync);
    }

    /// <summary>
    /// 请求停止调度器：不再接受新任务，已排队任务继续执行
    /// </summary>
    public void Stop()
    {
        lock (_queueSync)
            _acceptingWork = false;
        // 哨兵令牌使调度循环在队列清空后能够退出。
        _workAvailable.Release();
    }

    /// <summary>
    /// 取消整个调度器：立即停止所有正在执行和待执行的任务
    /// </summary>
    public void Cancel()
    {
        _globalCts.Cancel();
        CancelQueuedWorkItems();
    }

    /// <summary>
    /// 优雅关闭：停止接受新任务，等待所有正在执行的任务完成
    /// </summary>
    public async Task ShutdownAsync(TimeSpan? timeout = null)
    {
        Stop();
        if (_dispatcherTask is not null)
        {
            if (timeout.HasValue)
            {
                using var dispatcherTimeout = new CancellationTokenSource(timeout.Value);
                try { await _dispatcherTask.WaitAsync(dispatcherTimeout.Token).ConfigureAwait(false); }
                catch (OperationCanceledException) { }
            }
            else
            {
                await _dispatcherTask.ConfigureAwait(false);
            }
        }
        await WaitForAllAsync(timeout);
    }

    /// <summary>
    /// 等待所有正在执行的任务完成
    /// </summary>
    public async Task WaitForAllAsync(TimeSpan? timeout = null)
    {
        Task[] tasks;
        await _taskListLock.WaitAsync().ConfigureAwait(false);
        try
        {
            tasks = _runningTasks.ToArray();
        }
        finally
        {
            _taskListLock.Release();
        }

        if (tasks.Length == 0) return;

        if (timeout.HasValue)
        {
            using var timeoutCts = new CancellationTokenSource(timeout.Value);
            try
            {
                await Task.WhenAll(tasks).WaitAsync(timeoutCts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // 超时，但任务仍在后台运行
            }
        }
        else
        {
            await Task.WhenAll(tasks).ConfigureAwait(false);
        }
    }

    #endregion

    #region 提交任务

    /// <summary>
    /// 提交一个流执行请求
    /// </summary>
    /// <returns>执行结果 Task</returns>
    public Task<WorkItemResult> SubmitFlowAsync(Flow flow, CancellationToken ct = default) =>
        SubmitFlowAsync(flow, inputs: null, ct);

    /// <summary>
    /// 提交带输入参数的流执行请求。输入在写入队列前按工作流契约验证，
    /// 并复制为本次运行独立的不可变快照。
    /// </summary>
    public async Task<WorkItemResult> SubmitFlowAsync(
        Flow flow,
        IReadOnlyDictionary<string, object?>? inputs,
        CancellationToken ct = default) =>
        await SubmitFlowAsync(flow, inputs, Guid.NewGuid(), ct);

    /// <summary>使用调用方分配的运行标识提交工作流，便于持久化快照、日志和恢复记录使用同一 ID。</summary>
    public async Task<WorkItemResult> SubmitFlowAsync(
        Flow flow,
        IReadOnlyDictionary<string, object?>? inputs,
        Guid executionId,
        CancellationToken ct = default)
    {
        if (_isDisposed) throw new ObjectDisposedException(nameof(WorkflowScheduler));
        if (!_isStarted) Start();

        if (!FlowInputValidator.TryCreateInputSnapshot(flow, inputs, out var inputSnapshot, out var validationError))
            return WorkItemResult.Failed(TimeSpan.Zero, validationError!);

        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(_globalCts.Token, ct);
        var tcs = new TaskCompletionSource<WorkItemResult>(TaskCreationOptions.RunContinuationsAsynchronously);

        var context = new ExecutionContext
        {
            ExecutionId = executionId,
            CurrentFlowId = flow.FlowId,
            Inputs = inputSnapshot
        };
        context.RecordFlowEntry(flow.FlowId);

        var workItem = WorkItem.CreateFlow(flow, context, tcs);
        await EnqueueAsync(workItem, linkedCts.Token);

        return await tcs.Task;
    }

    /// <summary>
    /// 提交多个流执行请求（并行调度）
    /// </summary>
    public async Task<WorkItemResult[]> SubmitFlowsAsync(IEnumerable<Flow> flows, CancellationToken ct = default)
    {
        var tasks = new List<Task<WorkItemResult>>();
        foreach (var flow in flows)
        {
            tasks.Add(SubmitFlowAsync(flow, ct));
        }
        return await Task.WhenAll(tasks);
    }

    /// <summary>
    /// 提交单个模块执行（用于子流拆分的模块级调度）
    /// </summary>
    public async Task<WorkItemResult> SubmitModuleAsync(Module module, ExecutionContext parentContext, CancellationToken ct = default)
    {
        if (_isDisposed) throw new ObjectDisposedException(nameof(WorkflowScheduler));
        if (!_isStarted) Start();

        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(_globalCts.Token, ct);
        var tcs = new TaskCompletionSource<WorkItemResult>(TaskCreationOptions.RunContinuationsAsynchronously);

        var workItem = WorkItem.CreateModule(module, parentContext, tcs);
        await EnqueueAsync(workItem, linkedCts.Token);

        return await tcs.Task;
    }

    #endregion

    #region 执行 WorkItem

    private async Task EnqueueAsync(WorkItem item, CancellationToken ct)
    {
        if (_queueSlots is not null)
            await _queueSlots.WaitAsync(ct).ConfigureAwait(false);

        var enqueued = false;
        try
        {
            lock (_queueSync)
            {
                if (!_acceptingWork)
                    throw new InvalidOperationException("Workflow scheduler is no longer accepting work items.");

                _workQueue.Add(new QueuedWorkItem(
                    item,
                    DateTime.UtcNow,
                    Interlocked.Increment(ref _enqueueSequence)));
                enqueued = true;
            }

            Interlocked.Increment(ref _acceptedWorkItems);
            RecordWorkItemEvent(item, SchedulingEventKind.WorkItemQueued,
                $"Queued {item.Type} work item with base priority {item.BasePriority}.", item.BasePriority);
            _workAvailable.Release();
        }
        finally
        {
            if (!enqueued)
                _queueSlots?.Release();
        }
    }

    private async Task DispatchLoopAsync()
    {
        try
        {
            while (true)
            {
                await _workAvailable.WaitAsync(_globalCts.Token).ConfigureAwait(false);

                QueuedWorkItem? queuedItem;
                int effectivePriority;
                lock (_queueSync)
                {
                    if (_workQueue.Count == 0)
                    {
                        if (!_acceptingWork)
                            return;
                        continue;
                    }
                }

                // 保留工作项在队列中直到真正获得并发槽；这样槽位释放时，后来到达的高优先级项仍可先执行。
                await _concurrencyLimit.WaitAsync(_globalCts.Token).ConfigureAwait(false);
                lock (_queueSync)
                {
                    if (_workQueue.Count == 0)
                    {
                        _concurrencyLimit.Release();
                        if (!_acceptingWork)
                            return;
                        continue;
                    }

                    var now = DateTime.UtcNow;
                    var selectedIndex = 0;
                    effectivePriority = GetEffectiveQueuePriority(_workQueue[0], now);
                    for (var index = 1; index < _workQueue.Count; index++)
                    {
                        var candidatePriority = GetEffectiveQueuePriority(_workQueue[index], now);
                        if (candidatePriority > effectivePriority
                            || (candidatePriority == effectivePriority
                                && _workQueue[index].Sequence < _workQueue[selectedIndex].Sequence))
                        {
                            selectedIndex = index;
                            effectivePriority = candidatePriority;
                        }
                    }

                    queuedItem = _workQueue[selectedIndex];
                    _workQueue.RemoveAt(selectedIndex);
                }

                _queueSlots?.Release();

                var queueWait = DateTime.UtcNow - queuedItem.EnqueuedAtUtc;
                Interlocked.Increment(ref _startedWorkItems);
                Interlocked.Add(ref _totalQueueWaitTicks, queueWait.Ticks);
                RecordWorkItemEvent(queuedItem.Item, SchedulingEventKind.WorkItemStarted,
                    $"Started {queuedItem.Item.Type} work item after waiting {queueWait.TotalMilliseconds:F0} ms.",
                    effectivePriority);

                var task = ExecuteScheduledWorkItemAsync(queuedItem.Item, _globalCts.Token);
                await _taskListLock.WaitAsync(_globalCts.Token).ConfigureAwait(false);
                try
                {
                    _runningTasks.Add(task);
                }
                finally
                {
                    _taskListLock.Release();
                }

                _ = CleanupCompletedTasksAsync();
            }
        }
        catch (OperationCanceledException) when (_globalCts.IsCancellationRequested)
        {
            // Cancel() 已负责完成仍在队列中的 CompletionSource。
        }
    }

    private async Task ExecuteScheduledWorkItemAsync(WorkItem item, CancellationToken ct)
    {
        try
        {
            await ExecuteWorkItemAsync(item, ct).ConfigureAwait(false);
        }
        finally
        {
            _concurrencyLimit.Release();
        }
    }

    private int GetEffectiveQueuePriority(QueuedWorkItem queuedItem, DateTime now)
    {
        var agingBoost = Math.Min(
            _schedulerMaxAgingBoost,
            Math.Max(0, (int)((now - queuedItem.EnqueuedAtUtc).TotalSeconds / _schedulerAgingStepSeconds)));
        return queuedItem.Item.BasePriority + agingBoost;
    }

    private void CancelQueuedWorkItems()
    {
        List<QueuedWorkItem> queued;
        lock (_queueSync)
        {
            _acceptingWork = false;
            queued = _workQueue.ToList();
            _workQueue.Clear();
        }

        foreach (var item in queued)
        {
            _queueSlots?.Release();
            item.Item.CompletionSource?.TrySetResult(WorkItemResult.Cancelled(TimeSpan.Zero, item.Item.Context));
            Interlocked.Increment(ref _cancelledWorkItems);
            RecordWorkItemEvent(item.Item, SchedulingEventKind.WorkItemCancelled,
                "Cancelled before a concurrency slot was assigned.", item.Item.BasePriority);
        }

        _workAvailable.Release();
    }

    private async Task ExecuteWorkItemAsync(WorkItem item, CancellationToken ct)
    {
        var startTime = DateTime.UtcNow;
        WorkItemResult result;

        try
        {
            result = item.Type switch
            {
                WorkItemType.Flow => await ExecuteFlowItemAsync(item, ct),
                WorkItemType.Module => await ExecuteModuleItemAsync(item, ct),
                WorkItemType.SubFlow => await ExecuteSubFlowItemAsync(item, ct),
                _ => WorkItemResult.Failed(TimeSpan.Zero, $"Unknown work item type: {item.Type}")
            };
        }
        catch (ResourceReservationTimeoutException ex)
        {
            result = WorkItemResult.Failed(DateTime.UtcNow - startTime, ex.Message, item.Context, "Timeout");
        }
        catch (ResourceReservationBusyException ex)
        {
            result = WorkItemResult.Failed(DateTime.UtcNow - startTime, ex.Message, item.Context, "Busy");
        }
        catch (OperationCanceledException)
        {
            result = WorkItemResult.Cancelled(DateTime.UtcNow - startTime, item.Context);
        }
        catch (Exception ex)
        {
            result = WorkItemResult.Failed(DateTime.UtcNow - startTime, ex.Message, item.Context);
        }

        if (result.IsCancelled)
        {
            Interlocked.Increment(ref _cancelledWorkItems);
            RecordWorkItemEvent(item, SchedulingEventKind.WorkItemCancelled,
                result.ErrorMessage ?? "Work item was cancelled.", item.BasePriority);
        }
        else if (result.IsSuccess)
        {
            Interlocked.Increment(ref _completedWorkItems);
            RecordWorkItemEvent(item, SchedulingEventKind.WorkItemCompleted,
                $"Work item completed in {result.Duration.TotalMilliseconds:F0} ms.", item.BasePriority);
        }
        else
        {
            Interlocked.Increment(ref _failedWorkItems);
            RecordWorkItemEvent(item, SchedulingEventKind.WorkItemFailed,
                result.ErrorMessage ?? "Work item failed.", item.BasePriority);
        }

        // 通知完成
        item.CompletionSource?.TrySetResult(result);
    }

    private void RecordWorkItemEvent(
        WorkItem item,
        SchedulingEventKind kind,
        string message,
        int effectivePriority) =>
        _eventLog.Record(new SchedulingEvent(
            DateTime.UtcNow,
            kind,
            nameof(WorkflowScheduler),
            message,
            item.Context.RootContext.ExecutionId,
            item.Context.ExecutionId,
            item.WorkItemId,
            BasePriority: item.BasePriority,
            EffectivePriority: effectivePriority));

    private async Task<WorkItemResult> ExecuteFlowItemAsync(WorkItem item, CancellationToken ct)
    {
        if (item.Flow is null)
            return WorkItemResult.Failed(TimeSpan.Zero, "Flow is null", item.Context);

        var flowResult = await _flowExecutor.ExecuteAsync(
            item.Flow,
            item.Context,
            subFlowCallback: async (flowRef, parentCtx) =>
            {
                return await ResolveAndQueueSubFlowAsync(flowRef, parentCtx, ct);
            },
            ct);

        return flowResult.IsSuccess
            ? WorkItemResult.Success(flowResult.Duration, item.Context)
            : WorkItemResult.Failed(flowResult.Duration, flowResult.ErrorMessage ?? "Flow execution failed", item.Context, flowResult.RouteKey);
    }

    private async Task<WorkItemResult> ExecuteModuleItemAsync(WorkItem item, CancellationToken ct)
    {
        if (item.Module is null)
            return WorkItemResult.Failed(TimeSpan.Zero, "Module is null", item.Context);

        var moduleResult = await _moduleExecutor.ExecuteAsync(item.Module, item.Context, ct);

        return moduleResult.IsSuccess
            ? WorkItemResult.Success(moduleResult.Duration, item.Context)
            : WorkItemResult.Failed(moduleResult.Duration, moduleResult.ErrorMessage ?? "Module execution failed", item.Context, moduleResult.RouteKey);
    }

    private async Task<WorkItemResult> ExecuteSubFlowItemAsync(WorkItem item, CancellationToken ct)
    {
        // 子流 WorkItem 的 Flow 属性应包含已解析的流对象
        if (item.Flow is null)
            return WorkItemResult.Failed(TimeSpan.Zero, "Resolved sub-flow is null", item.Context);

        return await ExecuteFlowItemAsync(item, ct);
    }

    /// <summary>
    /// 解析并排队子流：通过 IFlowResolver 从数据库加载子流，加入调度队列
    /// </summary>
    private async Task<WorkItemResult> ResolveAndQueueSubFlowAsync(FlowReference flowRef, ExecutionContext parentCtx, CancellationToken ct)
    {
        // 循环检测：如果父上下文已访问过该流，返回失败（防止死循环）
        if (parentCtx.HasVisitedFlow(flowRef.ReferencedFlowId))
        {
            return WorkItemResult.Failed(TimeSpan.Zero, $"Cycle detected: flow {flowRef.ReferencedFlowId} has already been visited in the current execution path");
        }

        // 检查是否有流解析器
        if (_flowResolver is null)
        {
            return WorkItemResult.Failed(TimeSpan.Zero,
                "IFlowResolver is not registered. " +
                "Please register IFlowResolver in the DI container to enable sub-flow execution.");
        }

        // 解析子流
        var subFlow = await _flowResolver.ResolveAsync(flowRef.ReferencedFlowId, ct);
        if (subFlow is null)
        {
            return WorkItemResult.Failed(TimeSpan.Zero,
                $"Sub-flow not found: Flow ID {flowRef.ReferencedFlowId} does not exist in database.");
        }

        // 创建子流执行上下文
        if (!TryCreateSubFlowInputs(flowRef, parentCtx, out var childInputs, out var bindingError))
            return WorkItemResult.Failed(TimeSpan.Zero, bindingError!, parentCtx);

        if (!FlowInputValidator.TryCreateInputSnapshot(subFlow, childInputs, out var inputSnapshot, out var validationError))
            return WorkItemResult.Failed(TimeSpan.Zero, validationError!, parentCtx);

        var signature = CreateInputSignature(inputSnapshot);
        var result = await ExecuteSubFlowByPolicyAsync(flowRef, subFlow, parentCtx, inputSnapshot, signature, ct);
        if (result.IsSuccess && result.Context is not null)
        {
            foreach (var (childOutput, parentVariable) in flowRef.OutputBindings)
            {
                if (result.Context.Outputs.TryGetValue(childOutput, out var value))
                    parentCtx.SetVariable(parentVariable, value);
            }
        }

        return result;
    }

    private async Task<WorkItemResult> ExecuteSubFlowByPolicyAsync(
        FlowReference flowRef,
        Flow subFlow,
        ExecutionContext parentContext,
        IReadOnlyDictionary<string, object?> inputs,
        string inputSignature,
        CancellationToken ct)
    {
        switch (flowRef.InvocationPolicy)
        {
            case FlowInvocationPolicy.Reentrant:
                return await StartTrackedSubFlowAsync(flowRef, subFlow, parentContext, inputs, inputSignature, ct);

            case FlowInvocationPolicy.Exclusive:
            {
                SemaphoreSlim gate;
                lock (_subFlowInvocationSync)
                {
                    if (!_exclusiveSubFlowGates.TryGetValue(subFlow.FlowId, out gate!))
                        _exclusiveSubFlowGates[subFlow.FlowId] = gate = new SemaphoreSlim(1, 1);
                }
                await gate.WaitAsync(ct);
                try
                {
                    return await StartTrackedSubFlowAsync(flowRef, subFlow, parentContext, inputs, inputSignature, ct);
                }
                finally
                {
                    gate.Release();
                }
            }

            case FlowInvocationPolicy.JoinRunning:
            {
                Task<WorkItemResult> invocationTask;
                lock (_subFlowInvocationSync)
                {
                    var existing = _activeSubFlows.GetValueOrDefault(subFlow.FlowId)?
                        .FirstOrDefault(item => string.Equals(item.InputSignature, inputSignature, StringComparison.Ordinal));
                    invocationTask = existing?.Task
                        ?? StartTrackedSubFlowAsync(flowRef, subFlow, parentContext, inputs, inputSignature, ct);
                }
                return await invocationTask.WaitAsync(ct);
            }

            case FlowInvocationPolicy.RejectIfRunning:
            {
                Task<WorkItemResult> invocationTask;
                lock (_subFlowInvocationSync)
                {
                    if (_activeSubFlows.GetValueOrDefault(subFlow.FlowId)?.Count > 0)
                    {
                        return WorkItemResult.Failed(
                            TimeSpan.Zero,
                            $"Sub-flow '{subFlow.Name}' is already running.",
                            parentContext,
                            "Busy");
                    }
                    invocationTask = StartTrackedSubFlowAsync(
                        flowRef, subFlow, parentContext, inputs, inputSignature, ct);
                }
                return await invocationTask;
            }

            default:
                throw new InvalidOperationException($"Unknown sub-flow invocation policy '{flowRef.InvocationPolicy}'.");
        }
    }

    private Task<WorkItemResult> StartTrackedSubFlowAsync(
        FlowReference flowRef,
        Flow subFlow,
        ExecutionContext parentContext,
        IReadOnlyDictionary<string, object?> inputs,
        string inputSignature,
        CancellationToken ct)
    {
        var childContext = parentContext.CreateChildContext(subFlow.FlowId, inputs);
        var completion = new TaskCompletionSource<WorkItemResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var workItem = WorkItem.CreateSubFlow(flowRef, childContext, subFlow, completion);
        var invocation = new ActiveSubFlowInvocation(inputSignature, completion.Task);
        lock (_subFlowInvocationSync)
        {
            if (!_activeSubFlows.TryGetValue(subFlow.FlowId, out var active))
                _activeSubFlows[subFlow.FlowId] = active = new List<ActiveSubFlowInvocation>();
            active.Add(invocation);
        }

        return QueueAndAwaitAsync();

        async Task<WorkItemResult> QueueAndAwaitAsync()
        {
            try
            {
                using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(_globalCts.Token, ct);
                // 子工作流属于当前根运行实例并复用其调度槽。
                // 如果父项占着槽位等待、子项再进入同一全局队列，会在槽位耗尽时形成调度饥饿。
                await ExecuteWorkItemAsync(workItem, linkedCts.Token);
                return await completion.Task;
            }
            finally
            {
                lock (_subFlowInvocationSync)
                {
                    if (_activeSubFlows.TryGetValue(subFlow.FlowId, out var active))
                    {
                        active.Remove(invocation);
                        if (active.Count == 0) _activeSubFlows.Remove(subFlow.FlowId);
                    }
                }
            }
        }
    }

    private static string CreateInputSignature(IReadOnlyDictionary<string, object?> inputs) =>
        JsonSerializer.Serialize(inputs.OrderBy(item => item.Key, StringComparer.Ordinal));

    private static bool TryCreateSubFlowInputs(
        FlowReference flowRef,
        ExecutionContext parentContext,
        out IReadOnlyDictionary<string, object?> inputs,
        out string? errorMessage)
    {
        var mapped = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var (childInput, parentValue) in flowRef.InputBindings)
        {
            if (string.IsNullOrWhiteSpace(childInput) || string.IsNullOrWhiteSpace(parentValue))
            {
                inputs = mapped;
                errorMessage = $"Sub-flow '{flowRef.Name}' contains an empty parameter binding.";
                return false;
            }

            if (parentContext.Variables.TryGetValue(parentValue, out var value)
                || parentContext.Outputs.TryGetValue(parentValue, out value)
                || parentContext.Inputs.TryGetValue(parentValue, out value))
            {
                mapped[childInput] = value;
                continue;
            }

            inputs = mapped;
            errorMessage = $"Sub-flow '{flowRef.Name}' input '{childInput}' refers to unavailable parent value '{parentValue}'.";
            return false;
        }

        inputs = mapped;
        errorMessage = null;
        return true;
    }

    #endregion

    #region 清理

    private async Task CleanupCompletedTasksAsync()
    {
        await _taskListLock.WaitAsync();
        try
        {
            _runningTasks.RemoveAll(t => t.IsCompleted);
        }
        finally
        {
            _taskListLock.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_isDisposed) return;
        _isDisposed = true;

        Cancel();
        Stop();
        if (_dispatcherTask is not null)
        {
            try { await _dispatcherTask.ConfigureAwait(false); }
            catch (OperationCanceledException) { }
        }
        await WaitForAllAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);

        _globalCts.Dispose();
        _workAvailable.Dispose();
        _queueSlots?.Dispose();
        _concurrencyLimit.Dispose();
        _taskListLock.Dispose();
        lock (_subFlowInvocationSync)
        {
            foreach (var gate in _exclusiveSubFlowGates.Values) gate.Dispose();
            _exclusiveSubFlowGates.Clear();
            _activeSubFlows.Clear();
        }
    }

    #endregion
}

internal sealed record ActiveSubFlowInvocation(string InputSignature, Task<WorkItemResult> Task);

internal sealed record QueuedWorkItem(WorkItem Item, DateTime EnqueuedAtUtc, long Sequence);
