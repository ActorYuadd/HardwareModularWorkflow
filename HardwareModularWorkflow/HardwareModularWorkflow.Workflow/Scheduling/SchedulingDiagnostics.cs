namespace HardwareModularWorkflow.Workflow.Scheduling;

/// <summary>调度器与资源管理器共同写入的运行时审计事件类型。</summary>
public enum SchedulingEventKind
{
    WorkItemQueued,
    WorkItemStarted,
    WorkItemCompleted,
    WorkItemFailed,
    WorkItemCancelled,
    ResourceWaiting,
    ResourceGranted,
    ResourceReleased,
    ResourceTimedOut,
    ResourceWaitCancelled,
    PriorityChanged,
    PriorityInherited,
    QueueRejected
}

/// <summary>单条可追溯调度事件。事件仅保存标识和摘要，不持有工作流运行对象。</summary>
public sealed record SchedulingEvent(
    DateTime TimestampUtc,
    SchedulingEventKind Kind,
    string Source,
    string Message,
    Guid? WorkflowRunId = null,
    Guid? NodeRunId = null,
    Guid? WorkItemId = null,
    Guid? ReservationId = null,
    int? BasePriority = null,
    int? EffectivePriority = null);

/// <summary>调度事件写入与查询接口。</summary>
public interface ISchedulingEventLog
{
    void Record(SchedulingEvent schedulingEvent);

    IReadOnlyList<SchedulingEvent> GetRecentEvents(int maximumCount = 200);
}

/// <summary>有界的进程内调度审计日志，防止工作流长期运行造成日志对象无限增长。</summary>
public sealed class InMemorySchedulingEventLog : ISchedulingEventLog
{
    private readonly object _sync = new();
    private readonly Queue<SchedulingEvent> _events = new();
    private readonly int _capacity;

    public InMemorySchedulingEventLog(int capacity = 5000)
    {
        if (capacity <= 0) throw new ArgumentOutOfRangeException(nameof(capacity));
        _capacity = capacity;
    }

    public void Record(SchedulingEvent schedulingEvent)
    {
        ArgumentNullException.ThrowIfNull(schedulingEvent);
        lock (_sync)
        {
            _events.Enqueue(schedulingEvent);
            while (_events.Count > _capacity)
                _events.Dequeue();
        }
    }

    public IReadOnlyList<SchedulingEvent> GetRecentEvents(int maximumCount = 200)
    {
        if (maximumCount <= 0) return Array.Empty<SchedulingEvent>();
        lock (_sync)
        {
            return _events
                .TakeLast(Math.Min(maximumCount, _events.Count))
                .Reverse()
                .ToList();
        }
    }
}

internal sealed class NullSchedulingEventLog : ISchedulingEventLog
{
    public static NullSchedulingEventLog Instance { get; } = new();

    private NullSchedulingEventLog() { }

    public void Record(SchedulingEvent schedulingEvent) { }

    public IReadOnlyList<SchedulingEvent> GetRecentEvents(int maximumCount = 200) =>
        Array.Empty<SchedulingEvent>();
}

/// <summary>全局工作项队列的实时指标。</summary>
public sealed record WorkflowSchedulerSnapshot(
    int MaxConcurrency,
    int RunningWorkItems,
    int QueuedWorkItems,
    int AvailableSlots,
    long AcceptedWorkItems,
    long StartedWorkItems,
    long CompletedWorkItems,
    long FailedWorkItems,
    long CancelledWorkItems,
    TimeSpan AverageQueueWait,
    TimeSpan OldestQueueWait);
