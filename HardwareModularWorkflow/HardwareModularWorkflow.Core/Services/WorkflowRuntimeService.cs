using HardwareModularWorkflow.Workflow.Abstractions;
using HardwareModularWorkflow.Workflow.Engine;
using HardwareModularWorkflow.Workflow.Events;
using HardwareModularWorkflow.Workflow.Models;
using HardwareModularWorkflow.Core.Events;
using HardwareModularWorkflow.Db.Services;
using HardwareModularWorkflow.Db.Entities;

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

    public WorkflowRuntimeService(WorkflowScheduler scheduler, IEventBus eventBus, ExecutionLogService logService)
    {
        _scheduler = scheduler ?? throw new ArgumentNullException(nameof(scheduler));
        _eventBus = eventBus ?? throw new ArgumentNullException(nameof(eventBus));
        _logService = logService ?? throw new ArgumentNullException(nameof(logService));
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
    {
        // 创建执行会话
        var session = new ExecutionSession
        {
            Name = flow.Name,
            Description = $"Flow execution: {flow.Name}",
            StartedAt = DateTime.UtcNow
        };
        session = await _logService.CreateSessionAsync(session, ct);

        // 订阅事件记录日志
        using var subscription = _eventBus.Subscribe<WorkflowEvent>(async evt =>
        {
            await LogEventAsync(session.Id, evt, ct);
        });

        // 执行流
        var result = await _scheduler.SubmitFlowAsync(flow, ct);

        // 完成会话
        await _logService.CompleteSessionAsync(
            session.Id,
            result.IsSuccess,
            (long?)result.Duration.TotalMilliseconds,
            ct);

        return result;
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
}
