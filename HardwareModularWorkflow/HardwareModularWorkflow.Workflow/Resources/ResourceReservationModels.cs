namespace HardwareModularWorkflow.Workflow.Resources;

/// <summary>资源访问方式。</summary>
public enum ResourceAccessMode
{
    /// <summary>与任何其他访问互斥，例如电机运动命令。</summary>
    Exclusive,

    /// <summary>可与其他只读访问共享，但不能与独占访问同时进行。</summary>
    SharedRead
}

/// <summary>一次工作流操作需要占用的资源。</summary>
public sealed record ResourceRequirement(string ResourceId, ResourceAccessMode AccessMode = ResourceAccessMode.Exclusive);

/// <summary>资源预留请求。一个请求中的全部资源必须同时可用才会被授予。</summary>
public sealed class ResourceReservationRequest
{
    public required Guid WorkflowRunId { get; init; }
    public required Guid NodeRunId { get; init; }
    public required IReadOnlyCollection<ResourceRequirement> Requirements { get; init; }
    public int BasePriority { get; init; }
    /// <summary>仅限制资源等待时间；获得租约后不再计入此超时。</summary>
    public TimeSpan? WaitTimeout { get; init; }
    public string? Description { get; init; }
}

/// <summary>等待或持有资源的只读诊断信息。</summary>
public sealed class ResourceReservationSnapshot
{
    public required Guid ReservationId { get; init; }
    public required Guid WorkflowRunId { get; init; }
    public required Guid NodeRunId { get; init; }
    public required IReadOnlyCollection<ResourceRequirement> Requirements { get; init; }
    /// <summary>当前与本请求冲突、因而阻塞其授予的已持有预约。</summary>
    public required IReadOnlyCollection<Guid> BlockingReservationIds { get; init; }
    public required int EffectivePriority { get; init; }
    public required int BasePriority { get; init; }
    /// <summary>等待老化产生的提升；资源授予后不再继续老化。</summary>
    public required int AgingPriorityBoost { get; init; }
    /// <summary>由阻塞链中更高优先级工作流传递而来的提升。</summary>
    public required int InheritedPriorityBoost { get; init; }
    public required DateTime RequestedAtUtc { get; init; }
    public DateTime? DeadlineUtc { get; init; }
    public bool IsGranted { get; init; }
    public TimeSpan WaitingDuration { get; init; }
    public string? Description { get; init; }
}

/// <summary>资源等待队列与持有集合的实时指标。</summary>
public sealed record ResourceReservationMetrics(
    int QueueCapacity,
    int WaitingReservations,
    int GrantedReservations,
    int InheritedPriorityReservations,
    TimeSpan OldestWaitingDuration);

/// <summary>资源等待队列已满；调用方可按 Busy 路由处理。</summary>
public sealed class ResourceReservationBusyException : InvalidOperationException
{
    public ResourceReservationBusyException(string message) : base(message) { }
}

/// <summary>在指定等待时间内未能原子获得完整资源集合。</summary>
public sealed class ResourceReservationTimeoutException : TimeoutException
{
    public ResourceReservationTimeoutException(string message) : base(message) { }
}

/// <summary>资源租约；释放后等待队列会重新调度。</summary>
public sealed class ResourceLease : IAsyncDisposable, IDisposable
{
    private Action? _release;

    internal ResourceLease(Guid reservationId, Action release)
    {
        ReservationId = reservationId;
        _release = release;
    }

    public Guid ReservationId { get; }

    public void Dispose() => Interlocked.Exchange(ref _release, null)?.Invoke();

    public ValueTask DisposeAsync()
    {
        Dispose();
        return ValueTask.CompletedTask;
    }
}

/// <summary>工作流资源预留服务。</summary>
public interface IResourceReservationManager
{
    Task<ResourceLease> AcquireAsync(ResourceReservationRequest request, CancellationToken ct = default);

    IReadOnlyCollection<ResourceReservationSnapshot> GetSnapshot();

    ResourceReservationMetrics GetMetrics();

    /// <summary>
    /// 只调整尚未获得资源的请求。运行中的运动不可被抢占，避免在硬件层产生不安全的抢占行为。
    /// </summary>
    bool TryUpdateWaitingPriority(Guid workflowRunId, Guid nodeRunId, int basePriority);

    /// <summary>取消仍在等待队列中的预约；已获得资源的预约不会被此操作中断。</summary>
    bool TryCancelWaitingReservation(Guid reservationId);
}
