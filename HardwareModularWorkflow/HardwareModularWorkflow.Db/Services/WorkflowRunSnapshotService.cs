using HardwareModularWorkflow.Db.DbContext;
using HardwareModularWorkflow.Db.Entities;
using Microsoft.EntityFrameworkCore;

namespace HardwareModularWorkflow.Db.Services;

public sealed class WorkflowRunSnapshotService
{
    private readonly HardwareModularWorkflowDbContext _context;

    public WorkflowRunSnapshotService(HardwareModularWorkflowDbContext context)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
    }

    public async Task UpsertAsync(WorkflowRunSnapshot snapshot, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var existing = await _context.WorkflowRunSnapshots
            .FirstOrDefaultAsync(item => item.ExecutionId == snapshot.ExecutionId, ct);
        if (existing is null)
        {
            _context.WorkflowRunSnapshots.Add(snapshot);
        }
        else
        {
            existing.RecoveredFromExecutionId = snapshot.RecoveredFromExecutionId;
            existing.FlowId = snapshot.FlowId;
            existing.DefinitionVersion = snapshot.DefinitionVersion;
            existing.FlowName = snapshot.FlowName;
            existing.Status = snapshot.Status;
            existing.RecoveryPolicy = snapshot.RecoveryPolicy;
            existing.InputsJson = snapshot.InputsJson;
            existing.OutputsJson = snapshot.OutputsJson;
            existing.ErrorMessage = snapshot.ErrorMessage;
            existing.StartedAtUtc = snapshot.StartedAtUtc;
            existing.UpdatedAtUtc = snapshot.UpdatedAtUtc;
            existing.CompletedAtUtc = snapshot.CompletedAtUtc;
        }
        await _context.SaveChangesAsync(ct);
    }

    public Task<WorkflowRunSnapshot?> GetAsync(Guid executionId, CancellationToken ct = default) =>
        _context.WorkflowRunSnapshots
            .AsNoTracking()
            .FirstOrDefaultAsync(item => item.ExecutionId == executionId, ct);

    public Task<List<WorkflowRunSnapshot>> GetRecoveryRequiredAsync(CancellationToken ct = default) =>
        _context.WorkflowRunSnapshots
            .AsNoTracking()
            .Where(item => item.Status == WorkflowRunStatuses.RecoveryRequired)
            .OrderBy(item => item.UpdatedAtUtc)
            .ToListAsync(ct);

    public async Task<int> MarkInterruptedRunsAsync(CancellationToken ct = default)
    {
        var interrupted = await _context.WorkflowRunSnapshots
            .Where(item => item.Status == WorkflowRunStatuses.Pending
                || item.Status == WorkflowRunStatuses.Running)
            .ToListAsync(ct);
        foreach (var snapshot in interrupted)
        {
            snapshot.Status = WorkflowRunStatuses.RecoveryRequired;
            snapshot.ErrorMessage = "The process ended before the workflow reached a terminal state.";
            snapshot.UpdatedAtUtc = DateTime.UtcNow;
        }
        if (interrupted.Count > 0)
            await _context.SaveChangesAsync(ct);
        return interrupted.Count;
    }

    public async Task<int> MarkActiveRunsSafetyStoppedAsync(string reason, CancellationToken ct = default)
    {
        var active = await _context.WorkflowRunSnapshots
            .Where(item => item.Status == WorkflowRunStatuses.Pending
                || item.Status == WorkflowRunStatuses.Running)
            .ToListAsync(ct);
        foreach (var snapshot in active)
        {
            snapshot.Status = WorkflowRunStatuses.SafetyStopped;
            snapshot.ErrorMessage = reason;
            snapshot.UpdatedAtUtc = DateTime.UtcNow;
            snapshot.CompletedAtUtc = DateTime.UtcNow;
        }
        if (active.Count > 0)
            await _context.SaveChangesAsync(ct);
        return active.Count;
    }
}
