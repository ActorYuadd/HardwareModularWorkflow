using System.Threading.Channels;
using HardwareModularWorkflow.Workflow.Abstractions;
using HardwareModularWorkflow.Workflow.Models;
using HardwareModularWorkflow.Workflow.Engine;
using HardwareModularWorkflow.Workflow.Results;
using ExecutionContext = HardwareModularWorkflow.Workflow.Abstractions.ExecutionContext;

namespace HardwareModularWorkflow.Workflow.Engine;

/// <summary>
/// 工作流调度引擎：全局任务调度器
/// 
/// 核心设计：
/// - Channel&lt;WorkItem&gt; 作为待执行队列
/// - SemaphoreSlim(50) 限制全局并发数
/// - CancellationToken 控制整个调度器生命周期
/// - 嵌套流通过队列调度（非递归），避免栈溢出
/// - 不做嵌套层级限制，运行时通过 ExecutionPath 检测循环
/// </summary>
public sealed class WorkflowScheduler : IAsyncDisposable
{
    private readonly int _maxConcurrency;
    private readonly IStepExecutor _stepExecutor;
    private readonly IFlowResolver? _flowResolver;
    private readonly ModuleExecutor _moduleExecutor;
    private readonly FlowExecutor _flowExecutor;
    private readonly Channel<WorkItem> _workQueue;
    private readonly SemaphoreSlim _concurrencyLimit;
    private readonly CancellationTokenSource _globalCts = new();
    private readonly List<Task> _runningTasks = new();
    private readonly SemaphoreSlim _taskListLock = new(1, 1);
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
        get
        {
            if (_workQueue.Reader.TryPeek(out _))
            {
                // Channel 没有公开的 Count 属性，只能通过 BoundedChannel 获取
                // 对于无界队列返回 0（未知）
            }
            return 0;
        }
    }

    #endregion

    /// <summary>
    /// 创建调度器
    /// </summary>
    /// <param name="stepExecutor">步骤执行器（由 Core 层注入实现）</param>
    /// <param name="flowResolver">流解析器（可选，用于子流解析）</param>
    /// <param name="maxConcurrency">最大并发工作项数（默认 50）</param>
    /// <param name="channelCapacity">队列容量（默认 1000，0 表示无界）</param>
    public WorkflowScheduler(IStepExecutor stepExecutor, IFlowResolver? flowResolver = null, int maxConcurrency = 50, int channelCapacity = 1000)
    {
        _maxConcurrency = maxConcurrency;
        _stepExecutor = stepExecutor ?? throw new ArgumentNullException(nameof(stepExecutor));
        _flowResolver = flowResolver;
        _moduleExecutor = new ModuleExecutor(stepExecutor);
        _flowExecutor = new FlowExecutor(_moduleExecutor);
        _concurrencyLimit = new SemaphoreSlim(maxConcurrency, maxConcurrency);

        _workQueue = channelCapacity > 0
            ? Channel.CreateBounded<WorkItem>(new BoundedChannelOptions(channelCapacity)
            {
                FullMode = BoundedChannelFullMode.Wait
            })
            : Channel.CreateUnbounded<WorkItem>();
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

        // 启动后台调度循环（每个 WorkItem 作为一个独立 Task，通过 Semaphore 控制并发）
        _ = Task.Run(async () =>
        {
            await foreach (var item in _workQueue.Reader.ReadAllAsync(_globalCts.Token))
            {
                // 等待获取并发槽位
                await _concurrencyLimit.WaitAsync(_globalCts.Token);

                // 启动执行 Task，完成后释放槽位
                var task = ExecuteWorkItemAsync(item, _globalCts.Token)
                    .ContinueWith(async _ =>
                    {
                        _concurrencyLimit.Release();
                    }, TaskScheduler.Default)
                    .Unwrap();

                await _taskListLock.WaitAsync(_globalCts.Token);
                try
                {
                    _runningTasks.Add(task);
                }
                finally
                {
                    _taskListLock.Release();
                }

                // 清理已完成的任务（避免内存泄漏）
                _ = CleanupCompletedTasksAsync();
            }
        }, _globalCts.Token);
    }

    /// <summary>
    /// 请求停止调度器：不再接受新任务，已排队任务继续执行
    /// </summary>
    public void Stop()
    {
        _workQueue.Writer.Complete();
    }

    /// <summary>
    /// 取消整个调度器：立即停止所有正在执行和待执行的任务
    /// </summary>
    public void Cancel()
    {
        _globalCts.Cancel();
    }

    /// <summary>
    /// 优雅关闭：停止接受新任务，等待所有正在执行的任务完成
    /// </summary>
    public async Task ShutdownAsync(TimeSpan? timeout = null)
    {
        Stop();
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
    public async Task<WorkItemResult> SubmitFlowAsync(Flow flow, CancellationToken ct = default)
    {
        if (_isDisposed) throw new ObjectDisposedException(nameof(WorkflowScheduler));
        if (!_isStarted) Start();

        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(_globalCts.Token, ct);
        var tcs = new TaskCompletionSource<WorkItemResult>();

        var context = new ExecutionContext
        {
            CurrentFlowId = flow.FlowId
        };
        context.RecordFlowEntry(flow.FlowId);

        var workItem = WorkItem.CreateFlow(flow, context, tcs);
        await _workQueue.Writer.WriteAsync(workItem, linkedCts.Token);

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
        var tcs = new TaskCompletionSource<WorkItemResult>();

        var workItem = WorkItem.CreateModule(module, parentContext, tcs);
        await _workQueue.Writer.WriteAsync(workItem, linkedCts.Token);

        return await tcs.Task;
    }

    #endregion

    #region 执行 WorkItem

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
        catch (OperationCanceledException)
        {
            result = WorkItemResult.Cancelled(DateTime.UtcNow - startTime, item.Context);
        }
        catch (Exception ex)
        {
            result = WorkItemResult.Failed(DateTime.UtcNow - startTime, ex.Message, item.Context);
        }

        // 通知完成
        item.CompletionSource?.TrySetResult(result);
    }

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
            : WorkItemResult.Failed(flowResult.Duration, flowResult.ErrorMessage ?? "Flow execution failed", item.Context);
    }

    private async Task<WorkItemResult> ExecuteModuleItemAsync(WorkItem item, CancellationToken ct)
    {
        if (item.Module is null)
            return WorkItemResult.Failed(TimeSpan.Zero, "Module is null", item.Context);

        var moduleResult = await _moduleExecutor.ExecuteAsync(item.Module, item.Context, ct);

        return moduleResult.IsSuccess
            ? WorkItemResult.Success(moduleResult.Duration, item.Context)
            : WorkItemResult.Failed(moduleResult.Duration, moduleResult.ErrorMessage ?? "Module execution failed", item.Context);
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
        var childContext = parentCtx.CreateChildContext(subFlow.FlowId);

        // 创建子流 WorkItem 并提交到队列
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(_globalCts.Token, ct);
        var tcs = new TaskCompletionSource<WorkItemResult>();

        var workItem = WorkItem.CreateSubFlow(flowRef, childContext, subFlow, tcs);
        await _workQueue.Writer.WriteAsync(workItem, linkedCts.Token);

        return await tcs.Task;
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
        await WaitForAllAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);

        _globalCts.Dispose();
        _concurrencyLimit.Dispose();
        _taskListLock.Dispose();
    }

    #endregion
}
