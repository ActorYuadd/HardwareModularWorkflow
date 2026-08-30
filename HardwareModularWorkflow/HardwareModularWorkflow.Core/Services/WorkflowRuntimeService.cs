using HardwareModularWorkflow.Workflow.Abstractions;
using HardwareModularWorkflow.Workflow.Engine;
using HardwareModularWorkflow.Workflow.Events;
using HardwareModularWorkflow.Workflow.Models;
using HardwareModularWorkflow.Core.Events;
using HardwareModularWorkflow.Db.Services;
using HardwareModularWorkflow.Db.Entities;
using HardwareModularWorkflow.Core.Orchestration;
using System.Text.Json;

namespace HardwareModularWorkflow.Core.Services;

/// <summary>
/// 工作流运行时服务：Core 层对外提供的高层 API
/// 封装 WorkflowScheduler 和数据库操作，提供简洁的执行接口
/// </summary>
public sealed class WorkflowRuntimeService
{
    private readonly WorkflowScheduler _scheduler;
    private readonly IEventBus _eventBus;
    private readonly ExecutionLogService _logService;
    private readonly WorkflowRunSnapshotService _snapshotService;
    private readonly IFlowResolver _flowResolver;
    private readonly HardwareDriverFactory _driverFactory;

    public WorkflowRuntimeService(
        WorkflowScheduler scheduler,
        IEventBus eventBus,
        ExecutionLogService logService,
        WorkflowRunSnapshotService snapshotService,
        IFlowResolver flowResolver,
        HardwareDriverFactory driverFactory)
    {
        _scheduler = scheduler ?? throw new ArgumentNullException(nameof(scheduler));
        _eventBus = eventBus ?? throw new ArgumentNullException(nameof(eventBus));
        _logService = logService ?? throw new ArgumentNullException(nameof(logService));
        _snapshotService = snapshotService ?? throw new ArgumentNullException(nameof(snapshotService));
        _flowResolver = flowResolver ?? throw new ArgumentNullException(nameof(flowResolver));
        _driverFactory = driverFactory ?? throw new ArgumentNullException(nameof(driverFactory));
    }

    #region 状态属性

    /// <summary>运行时是否已启动</summary>
    public bool IsRuntimeRunning => _scheduler.IsRunning;

    /// <summary>最大并发槽位数</summary>
    public int MaxConcurrencySlots => _scheduler.MaxConcurrency;

    /// <summary>当前可用并发槽位数</summary>
    public int CurrentAvailableSlots => _scheduler.CurrentAvailableSlots;

    /// <summary>当前正在执行的任务数</summary>
    public int CurrentRunningTasks => _scheduler.CurrentRunningTasks;

    #endregion

    /// <summary>
    /// 启动运行时服务
    /// </summary>
    public void Start()
    {
        _scheduler.Start();
        SubscribeToEvents();
    }

    /// <summary>
    /// 在调度器启动前扫描上次进程遗留的活动快照。必须由应用启动流程异步等待，
    /// 避免阻塞 WPF UI 线程，也避免与新执行并发写入同一个运行快照上下文。
    /// </summary>
    public Task<int> InitializeRecoveryAsync(CancellationToken ct = default) =>
        _snapshotService.MarkInterruptedRunsAsync(ct);

    /// <summary>
    /// 停止运行时服务
    /// </summary>
    public async Task StopAsync(TimeSpan? timeout = null)
    {
        await _scheduler.ShutdownAsync(timeout);
    }

    /// <summary>
    /// 执行单个流
    /// </summary>
    public async Task<WorkItemResult> ExecuteFlowAsync(Flow flow, CancellationToken ct = default)
        => await ExecuteFlowAsync(flow, inputs: null, recoveredFromExecutionId: null, ct);

    public async Task<WorkItemResult> ExecuteFlowAsync(
        Flow flow,
        IReadOnlyDictionary<string, object?>? inputs,
        CancellationToken ct = default)
        => await ExecuteFlowAsync(flow, inputs, recoveredFromExecutionId: null, ct);

    private async Task<WorkItemResult> ExecuteFlowAsync(
        Flow flow,
        IReadOnlyDictionary<string, object?>? inputs,
        Guid? recoveredFromExecutionId,
        CancellationToken ct)
    {
        var executionId = Guid.NewGuid();
        // 创建执行会话
        var session = new ExecutionSession
        {
            Id = executionId,
            Name = flow.Name,
            Description = $"Flow execution: {flow.Name}",
            StartedAt = DateTime.UtcNow
        };
        session = await _logService.CreateSessionAsync(session, ct);

        var snapshot = new WorkflowRunSnapshot
        {
            ExecutionId = executionId,
            RecoveredFromExecutionId = recoveredFromExecutionId,
            FlowId = flow.FlowId,
            DefinitionVersion = flow.DefinitionVersion,
            FlowName = flow.Name,
            Status = WorkflowRunStatuses.Pending,
            RecoveryPolicy = flow.RecoveryPolicy.ToString(),
            InputsJson = JsonSerializer.Serialize(inputs ?? new Dictionary<string, object?>()),
            StartedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        };
        await _snapshotService.UpsertAsync(snapshot, ct);

        // 订阅事件记录日志
        using var subscription = _eventBus.Subscribe<WorkflowEvent>(async evt =>
        {
            await LogEventAsync(session.Id, evt, CancellationToken.None);
        });

        snapshot.Status = WorkflowRunStatuses.Running;
        snapshot.UpdatedAtUtc = DateTime.UtcNow;
        await _snapshotService.UpsertAsync(snapshot, ct);

        WorkItemResult result;
        try
        {
            result = await _scheduler.SubmitFlowAsync(flow, inputs, executionId, ct);
        }
        catch (Exception ex)
        {
            snapshot.Status = ex is OperationCanceledException
                ? WorkflowRunStatuses.Cancelled
                : WorkflowRunStatuses.Failed;
            snapshot.ErrorMessage = ex.Message;
            snapshot.CompletedAtUtc = DateTime.UtcNow;
            snapshot.UpdatedAtUtc = DateTime.UtcNow;
            await _snapshotService.UpsertAsync(snapshot, CancellationToken.None);
            throw;
        }

        // 完成会话
        await _logService.CompleteSessionAsync(
            session.Id,
            result.IsSuccess,
            (long?)result.Duration.TotalMilliseconds,
            CancellationToken.None);

        var currentSnapshot = await _snapshotService.GetAsync(executionId, CancellationToken.None);
        if (currentSnapshot?.Status != WorkflowRunStatuses.SafetyStopped)
        {
            snapshot.Status = result.IsSuccess
                ? WorkflowRunStatuses.Succeeded
                : result.IsCancelled
                    ? WorkflowRunStatuses.Cancelled
                    : WorkflowRunStatuses.Failed;
            snapshot.OutputsJson = JsonSerializer.Serialize(result.Context?.Outputs
                ?? new Dictionary<string, object?>());
            snapshot.ErrorMessage = result.ErrorMessage;
            snapshot.CompletedAtUtc = DateTime.UtcNow;
            snapshot.UpdatedAtUtc = DateTime.UtcNow;
            await _snapshotService.UpsertAsync(snapshot, CancellationToken.None);
        }

        return result;
    }

    public Task<List<WorkflowRunSnapshot>> GetRecoveryRequiredRunsAsync(CancellationToken ct = default) =>
        _snapshotService.GetRecoveryRequiredAsync(ct);

    /// <summary>
    /// 显式恢复一个中断运行。只支持定义声明的从头重启，并要求定义版本与中断时完全一致。
    /// </summary>
    public async Task<WorkflowRecoveryResult> ResumeFlowAsync(Guid interruptedExecutionId, CancellationToken ct = default)
    {
        var snapshot = await _snapshotService.GetAsync(interruptedExecutionId, ct);
        if (snapshot is null)
            return WorkflowRecoveryResult.Blocked("Workflow run snapshot was not found.");
        var flow = await _flowResolver.ResolveAsync(snapshot.FlowId, ct);
        if (flow is null)
            return WorkflowRecoveryResult.Blocked($"Flow {snapshot.FlowId} no longer exists.");
        if (!WorkflowRecoveryValidator.TryValidateRestart(snapshot, flow, out var recoveryError))
            return WorkflowRecoveryResult.Blocked(recoveryError!);

        IReadOnlyDictionary<string, object?> inputs;
        try
        {
            inputs = DeserializeInputs(flow, snapshot.InputsJson);
        }
        catch (Exception ex)
        {
            return WorkflowRecoveryResult.Blocked($"Stored inputs cannot be restored: {ex.Message}");
        }

        var result = await ExecuteFlowAsync(flow, inputs, interruptedExecutionId, ct);
        if (result.IsSuccess)
        {
            snapshot.Status = WorkflowRunStatuses.Recovered;
            snapshot.ErrorMessage = null;
            snapshot.CompletedAtUtc = DateTime.UtcNow;
            snapshot.UpdatedAtUtc = DateTime.UtcNow;
            await _snapshotService.UpsertAsync(snapshot, CancellationToken.None);
        }

        return new WorkflowRecoveryResult(true, result.IsSuccess, result.ErrorMessage, result);
    }

    /// <summary>取消工作流并对所有已创建硬件驱动发送停止命令，返回逐硬件确认结果。</summary>
    public async Task<HardwareSafetyStopReport> SafeStopAsync(
        string reason,
        TimeSpan? timeout = null,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(reason)) reason = "Safety stop requested.";
        _scheduler.Cancel();
        var report = await _driverFactory.TryStopAllAsync(timeout ?? TimeSpan.FromSeconds(5), ct);
        await _snapshotService.MarkActiveRunsSafetyStoppedAsync(reason, CancellationToken.None);
        return report;
    }

    /// <summary>
    /// 取消所有正在执行的任务
    /// </summary>
    public void CancelAll()
    {
        _scheduler.Cancel();
    }

    /// <summary>
    /// 订阅事件并记录到数据库
    /// </summary>
    private void SubscribeToEvents()
    {
        // 全局事件订阅（独立于单次执行的日志记录）
        _eventBus.Subscribe<WorkflowEvent>(async evt =>
        {
            // 可在此添加全局处理：如 UI 通知、报警等
            await Task.CompletedTask;
        });
    }

    /// <summary>
    /// 将工作流事件转换为执行日志
    /// </summary>
    private async Task LogEventAsync(Guid sessionId, WorkflowEvent evt, CancellationToken ct)
    {
        var log = new ExecutionLog
        {
            ExecutionId = sessionId,
            LogType = evt.EventType.ToString(),
            FlowName = evt.FlowId?.ToString(),
            ModuleName = evt.ModuleId?.ToString(),
            StepName = evt.StepId?.ToString(),
            HardwareName = evt.HardwareId?.ToString(),
            ErrorMessage = evt.Message,
            DetailsJson = evt.Data is not null ? System.Text.Json.JsonSerializer.Serialize(evt.Data) : null,
            IsSuccess = !evt.IsError,
            IsCancelled = evt.EventType == WorkflowEventType.Cancelled,
            ExecutedAt = evt.Timestamp
        };

        await _logService.CreateAsync(log, ct);
    }

    private static IReadOnlyDictionary<string, object?> DeserializeInputs(Flow flow, string json)
    {
        using var document = JsonDocument.Parse(json);
        var result = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var definition in flow.InputParameters)
        {
            if (!document.RootElement.TryGetProperty(definition.Name, out var element))
                continue;
            result[definition.Name] = element.ValueKind == JsonValueKind.Null
                ? null
                : definition.Type switch
                {
                    FlowParameterType.String => element.GetString(),
                    FlowParameterType.Boolean => element.GetBoolean(),
                    FlowParameterType.Integer => element.GetInt64(),
                    FlowParameterType.Decimal => element.GetDecimal(),
                    FlowParameterType.DateTime => element.GetDateTime(),
                    FlowParameterType.Guid => element.GetGuid(),
                    _ => element.Clone()
                };
        }
        return result;
    }
}

public sealed record WorkflowRecoveryResult(
    bool RecoveryStarted,
    bool IsSuccess,
    string? ErrorMessage,
    WorkItemResult? ExecutionResult)
{
    public static WorkflowRecoveryResult Blocked(string errorMessage) =>
        new(false, false, errorMessage, null);
}
