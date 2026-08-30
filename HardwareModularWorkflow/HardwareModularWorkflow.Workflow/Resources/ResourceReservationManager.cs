using HardwareModularWorkflow.Workflow.Scheduling;

namespace HardwareModularWorkflow.Workflow.Resources;

/// <summary>
/// 进程内资源预留管理器。
/// 同一请求的全部资源以原子方式授予，避免工作流只占有部分轴或硬件后互相等待。
/// </summary>
public sealed class ResourceReservationManager : IResourceReservationManager
{
    private readonly object _sync = new();
    private readonly List<Reservation> _waiting = new();
    private readonly Dictionary<Guid, Reservation> _granted = new();
    private readonly int _agingStepSeconds;
    private readonly int _maxAgingBoost;
    private readonly int _queueCapacity;
    private readonly ISchedulingEventLog _eventLog;

    public ResourceReservationManager(
        int agingStepSeconds = 5,
        int maxAgingBoost = 20,
        int queueCapacity = 1000,
        ISchedulingEventLog? eventLog = null)
    {
        if (agingStepSeconds <= 0) throw new ArgumentOutOfRangeException(nameof(agingStepSeconds));
        if (maxAgingBoost < 0) throw new ArgumentOutOfRangeException(nameof(maxAgingBoost));
        if (queueCapacity <= 0) throw new ArgumentOutOfRangeException(nameof(queueCapacity));

        _agingStepSeconds = agingStepSeconds;
        _maxAgingBoost = maxAgingBoost;
        _queueCapacity = queueCapacity;
        _eventLog = eventLog ?? NullSchedulingEventLog.Instance;
    }

    public Task<ResourceLease> AcquireAsync(ResourceReservationRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.Requirements is null || request.Requirements.Count == 0)
            throw new ArgumentException("A resource reservation requires at least one resource.", nameof(request));
        if (request.Requirements.Any(requirement => string.IsNullOrWhiteSpace(requirement.ResourceId)))
            throw new ArgumentException("Resource identifiers cannot be empty.", nameof(request));
        if (request.WaitTimeout.HasValue && request.WaitTimeout.Value <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(request), "Resource wait timeout must be greater than zero.");

        ct.ThrowIfCancellationRequested();

        var reservation = new Reservation(request, NormalizeRequirements(request.Requirements));

        lock (_sync)
        {
            if (_waiting.Count >= _queueCapacity)
            {
                RecordEvent(reservation, SchedulingEventKind.QueueRejected,
                    $"Resource wait queue capacity ({_queueCapacity}) has been reached.");
                throw new ResourceReservationBusyException($"Resource wait queue capacity ({_queueCapacity}) has been reached.");
            }

            _waiting.Add(reservation);
            RecordEvent(reservation, SchedulingEventKind.ResourceWaiting,
                $"Waiting for resources: {FormatResources(reservation)}.");
            // 先入队再注册：即使注册时令牌已取消，回调也能移除这条请求。
            reservation.CancellationRegistration = ct.Register(() => Cancel(reservation.ReservationId, ct));
            if (request.WaitTimeout.HasValue)
            {
                reservation.TimeoutTimer = new Timer(
                    _ => Timeout(reservation.ReservationId),
                    null,
                    request.WaitTimeout.Value,
                    System.Threading.Timeout.InfiniteTimeSpan);
            }
            ScheduleWaitingReservations();
        }

        return reservation.Completion.Task;
    }

    public IReadOnlyCollection<ResourceReservationSnapshot> GetSnapshot()
    {
        lock (_sync)
        {
            var now = DateTime.UtcNow;
            RecalculateEffectivePriorities(now, emitInheritanceEvents: true);
            var reservations = _granted.Values.Concat(_waiting).ToList();
            return reservations
                .Select(reservation => new ResourceReservationSnapshot
                {
                    ReservationId = reservation.ReservationId,
                    WorkflowRunId = reservation.WorkflowRunId,
                    NodeRunId = reservation.NodeRunId,
                    Requirements = reservation.Requirements,
                    BlockingReservationIds = reservation.IsGranted
                        ? Array.Empty<Guid>()
                        : _granted.Values
                            .Where(active => HasConflict(reservation.Requirements, active.Requirements))
                            .Select(active => active.ReservationId)
                            .ToList(),
                    EffectivePriority = reservation.EffectivePriority,
                    BasePriority = reservation.BasePriority,
                    AgingPriorityBoost = GetAgingBoost(reservation, now),
                    InheritedPriorityBoost = Math.Max(
                        0,
                        reservation.EffectivePriority - reservation.BasePriority - GetAgingBoost(reservation, now)),
                    RequestedAtUtc = reservation.RequestedAtUtc,
                    DeadlineUtc = reservation.DeadlineUtc,
                    IsGranted = reservation.IsGranted,
                    WaitingDuration = reservation.IsGranted ? TimeSpan.Zero : now - reservation.RequestedAtUtc,
                    Description = reservation.Description
                })
                .ToList();
        }
    }

    public ResourceReservationMetrics GetMetrics()
    {
        lock (_sync)
        {
            var now = DateTime.UtcNow;
            RecalculateEffectivePriorities(now, emitInheritanceEvents: true);
            return new ResourceReservationMetrics(
                _queueCapacity,
                _waiting.Count,
                _granted.Count,
                _waiting.Concat(_granted.Values).Count(item =>
                    item.EffectivePriority > item.BasePriority + GetAgingBoost(item, now)),
                _waiting.Count == 0
                    ? TimeSpan.Zero
                    : now - _waiting.Min(item => item.RequestedAtUtc));
        }
    }

    public bool TryUpdateWaitingPriority(Guid workflowRunId, Guid nodeRunId, int basePriority)
    {
        lock (_sync)
        {
            var reservation = _waiting.FirstOrDefault(item =>
                item.WorkflowRunId == workflowRunId && item.NodeRunId == nodeRunId);
            if (reservation is null)
                return false;

            reservation.BasePriority = basePriority;
            RecordEvent(reservation, SchedulingEventKind.PriorityChanged,
                $"Base priority changed to {basePriority}.");
            ScheduleWaitingReservations();
            return true;
        }
    }

    public bool TryCancelWaitingReservation(Guid reservationId)
    {
        lock (_sync)
        {
            var reservation = _waiting.FirstOrDefault(item => item.ReservationId == reservationId);
            if (reservation is null)
                return false;

            _waiting.Remove(reservation);
            reservation.DisposeWaitHandles();
            reservation.Completion.TrySetCanceled();
            RecordEvent(reservation, SchedulingEventKind.ResourceWaitCancelled,
                "Resource wait was cancelled by an operator.");
            ScheduleWaitingReservations();
            return true;
        }
    }

    private void Release(Guid reservationId)
    {
        lock (_sync)
        {
            if (!_granted.Remove(reservationId, out var reservation))
                return;

            reservation.DisposeWaitHandles();
            RecordEvent(reservation, SchedulingEventKind.ResourceReleased,
                $"Released resources: {FormatResources(reservation)}.");
            ScheduleWaitingReservations();
        }
    }

    private void Cancel(Guid reservationId, CancellationToken ct)
    {
        lock (_sync)
        {
            var reservation = _waiting.FirstOrDefault(item => item.ReservationId == reservationId);
            if (reservation is null)
                return;

            _waiting.Remove(reservation);
            reservation.DisposeWaitHandles();
            reservation.Completion.TrySetCanceled(ct);
            RecordEvent(reservation, SchedulingEventKind.ResourceWaitCancelled,
                "Resource wait was cancelled.");
            ScheduleWaitingReservations();
        }
    }

    private void Timeout(Guid reservationId)
    {
        lock (_sync)
        {
            var reservation = _waiting.FirstOrDefault(item => item.ReservationId == reservationId);
            if (reservation is null)
                return;

            _waiting.Remove(reservation);
            reservation.DisposeWaitHandles();
            reservation.Completion.TrySetException(new ResourceReservationTimeoutException(
                $"Timed out waiting for resources: {string.Join(", ", reservation.Requirements.Select(item => item.ResourceId))}."));
            RecordEvent(reservation, SchedulingEventKind.ResourceTimedOut,
                $"Timed out waiting for resources: {FormatResources(reservation)}.");
            ScheduleWaitingReservations();
        }
    }

    private void ScheduleWaitingReservations()
    {
        var now = DateTime.UtcNow;
        while (true)
        {
            RecalculateEffectivePriorities(now, emitInheritanceEvents: true);
            var candidate = _waiting
                .OrderByDescending(reservation => reservation.EffectivePriority)
                .ThenBy(reservation => reservation.RequestedAtUtc)
                .FirstOrDefault(CanGrant);

            if (candidate is null)
                return;

            _waiting.Remove(candidate);
            candidate.IsGranted = true;
            _granted.Add(candidate.ReservationId, candidate);
            candidate.DisposeWaitHandles();
            RecordEvent(candidate, SchedulingEventKind.ResourceGranted,
                $"Granted resources: {FormatResources(candidate)}.");
            candidate.Completion.TrySetResult(new ResourceLease(candidate.ReservationId, () => Release(candidate.ReservationId)));
        }
    }

    private bool CanGrant(Reservation candidate) =>
        _granted.Values.All(active => !HasConflict(candidate.Requirements, active.Requirements));

    private static bool HasConflict(
        IReadOnlyCollection<ResourceRequirement> first,
        IReadOnlyCollection<ResourceRequirement> second)
    {
        foreach (var left in first)
        foreach (var right in second)
        {
            if (!string.Equals(left.ResourceId, right.ResourceId, StringComparison.Ordinal))
                continue;

            if (left.AccessMode == ResourceAccessMode.Exclusive || right.AccessMode == ResourceAccessMode.Exclusive)
                return true;
        }

        return false;
    }

    private int GetAgingBoost(Reservation reservation, DateTime now)
    {
        if (reservation.IsGranted)
            return 0;

        var agingBoost = Math.Min(
            _maxAgingBoost,
            (int)((now - reservation.RequestedAtUtc).TotalSeconds / _agingStepSeconds));
        return Math.Max(0, agingBoost);
    }

    /// <summary>
    /// 以工作流运行实例为传播单位计算优先级继承。若高优先级运行等待 A，而持有 A 的运行又在等待 B，
    /// 则提升会沿阻塞链传播到后者对 B 的等待请求，直到固定点。
    /// </summary>
    private void RecalculateEffectivePriorities(DateTime now, bool emitInheritanceEvents)
    {
        var reservations = _granted.Values.Concat(_waiting).ToList();
        var ownPriorities = reservations.ToDictionary(
            item => item.ReservationId,
            item => item.BasePriority + GetAgingBoost(item, now));
        var workflowPriorities = reservations
            .GroupBy(item => item.WorkflowRunId)
            .ToDictionary(
                group => group.Key,
                group => group.Max(item => ownPriorities[item.ReservationId]));

        var changed = true;
        while (changed)
        {
            changed = false;
            foreach (var waiting in _waiting)
            {
                var waitingPriority = Math.Max(
                    ownPriorities[waiting.ReservationId],
                    workflowPriorities[waiting.WorkflowRunId]);
                foreach (var blocker in _granted.Values.Where(active =>
                             HasConflict(waiting.Requirements, active.Requirements)))
                {
                    if (workflowPriorities[blocker.WorkflowRunId] >= waitingPriority)
                        continue;

                    workflowPriorities[blocker.WorkflowRunId] = waitingPriority;
                    changed = true;
                }
            }
        }

        foreach (var reservation in reservations)
        {
            var previous = reservation.EffectivePriority;
            reservation.EffectivePriority = Math.Max(
                ownPriorities[reservation.ReservationId],
                workflowPriorities[reservation.WorkflowRunId]);
            var inherited = reservation.EffectivePriority > ownPriorities[reservation.ReservationId];
            if (emitInheritanceEvents && inherited && previous != reservation.EffectivePriority)
            {
                RecordEvent(reservation, SchedulingEventKind.PriorityInherited,
                    $"Effective priority inherited from {previous} to {reservation.EffectivePriority}.");
            }
        }
    }

    private void RecordEvent(Reservation reservation, SchedulingEventKind kind, string message) =>
        _eventLog.Record(new SchedulingEvent(
            DateTime.UtcNow,
            kind,
            nameof(ResourceReservationManager),
            message,
            reservation.WorkflowRunId,
            reservation.NodeRunId,
            ReservationId: reservation.ReservationId,
            BasePriority: reservation.BasePriority,
            EffectivePriority: reservation.EffectivePriority));

    private static string FormatResources(Reservation reservation) =>
        string.Join(", ", reservation.Requirements.Select(item => item.ResourceId));

    private static IReadOnlyCollection<ResourceRequirement> NormalizeRequirements(
        IReadOnlyCollection<ResourceRequirement> requirements) =>
        requirements
            .GroupBy(requirement => requirement.ResourceId, StringComparer.Ordinal)
            .Select(group => group.Any(requirement => requirement.AccessMode == ResourceAccessMode.Exclusive)
                ? new ResourceRequirement(group.Key, ResourceAccessMode.Exclusive)
                : new ResourceRequirement(group.Key, ResourceAccessMode.SharedRead))
            .OrderBy(requirement => requirement.ResourceId, StringComparer.Ordinal)
            .ToList();

    private sealed class Reservation
    {
        public Reservation(ResourceReservationRequest request, IReadOnlyCollection<ResourceRequirement> requirements)
        {
            ReservationId = Guid.NewGuid();
            WorkflowRunId = request.WorkflowRunId;
            NodeRunId = request.NodeRunId;
            Requirements = requirements;
            BasePriority = request.BasePriority;
            EffectivePriority = request.BasePriority;
            Description = request.Description;
            DeadlineUtc = request.WaitTimeout.HasValue ? RequestedAtUtc + request.WaitTimeout.Value : null;
        }

        public Guid ReservationId { get; }
        public Guid WorkflowRunId { get; }
        public Guid NodeRunId { get; }
        public IReadOnlyCollection<ResourceRequirement> Requirements { get; }
        public int BasePriority { get; set; }
        public int EffectivePriority { get; set; }
        public string? Description { get; }
        public DateTime RequestedAtUtc { get; } = DateTime.UtcNow;
        public DateTime? DeadlineUtc { get; }
        public bool IsGranted { get; set; }
        public TaskCompletionSource<ResourceLease> Completion { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public CancellationTokenRegistration CancellationRegistration { get; set; }
        public Timer? TimeoutTimer { get; set; }

        public void DisposeWaitHandles()
        {
            CancellationRegistration.Dispose();
            TimeoutTimer?.Dispose();
            TimeoutTimer = null;
        }
    }
}
