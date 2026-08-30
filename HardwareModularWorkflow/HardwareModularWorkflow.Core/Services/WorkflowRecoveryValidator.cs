using HardwareModularWorkflow.Db.Entities;
using HardwareModularWorkflow.Workflow.Models;

namespace HardwareModularWorkflow.Core.Services;

public static class WorkflowRecoveryValidator
{
    public static bool TryValidateRestart(
        WorkflowRunSnapshot snapshot,
        Flow currentFlow,
        out string? errorMessage)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(currentFlow);

        if (snapshot.Status != WorkflowRunStatuses.RecoveryRequired)
        {
            errorMessage = $"Workflow run status '{snapshot.Status}' does not require recovery.";
            return false;
        }
        if (!Enum.TryParse<WorkflowRecoveryPolicy>(snapshot.RecoveryPolicy, true, out var policy)
            || policy != WorkflowRecoveryPolicy.RestartFromBeginning
            || currentFlow.RecoveryPolicy != WorkflowRecoveryPolicy.RestartFromBeginning)
        {
            errorMessage = "Workflow recovery policy does not allow restarting from the beginning.";
            return false;
        }
        if (currentFlow.FlowId != snapshot.FlowId)
        {
            errorMessage = "Resolved workflow does not match the interrupted run.";
            return false;
        }
        if (currentFlow.DefinitionVersion != snapshot.DefinitionVersion)
        {
            errorMessage = $"Flow definition changed from version {snapshot.DefinitionVersion} to {currentFlow.DefinitionVersion}.";
            return false;
        }

        errorMessage = null;
        return true;
    }
}
