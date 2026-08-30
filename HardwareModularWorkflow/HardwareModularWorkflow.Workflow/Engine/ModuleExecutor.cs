using HardwareModularWorkflow.Hardware.Abstractions;
using HardwareModularWorkflow.Hardware.Enums;
using HardwareModularWorkflow.Hardware.Results;
using HardwareModularWorkflow.Workflow.Abstractions;
using HardwareModularWorkflow.Workflow.Models;
using HardwareModularWorkflow.Workflow.Results;
using HardwareModularWorkflow.Workflow.Resources;
using ExecutionContext = HardwareModularWorkflow.Workflow.Abstractions.ExecutionContext;

namespace HardwareModularWorkflow.Workflow.Engine;

/// <summary>
/// 模块执行器：执行单个模块内的所有硬件步骤
/// 支持串行（Sequential）和并行（Parallel）执行模式
/// </summary>
public sealed class ModuleExecutor
{
    private readonly IStepExecutor _stepExecutor;
    private readonly IResourceReservationManager _resourceReservationManager;

    public ModuleExecutor(
        IStepExecutor stepExecutor,
        IResourceReservationManager? resourceReservationManager = null)
    {
        _stepExecutor = stepExecutor ?? throw new ArgumentNullException(nameof(stepExecutor));
        _resourceReservationManager = resourceReservationManager ?? new ResourceReservationManager();
    }

    /// <summary>
    /// 执行模块内的所有硬件步骤
    /// </summary>
    public async Task<ModuleExecutionResult> ExecuteAsync(Module module, ExecutionContext context, CancellationToken ct = default)
    {
        context.CurrentModuleId = module.ModuleId;
        var moduleStart = DateTime.UtcNow;
        var stepResults = new List<StepExecutionResult>();
        bool moduleSuccess = true;
        string? moduleError = null;
        var moduleRouteKey = "Success";
        ResourceLease? resourceLease = null;
        var moduleReservedResourceIds = new HashSet<string>(StringComparer.Ordinal);
        var compensationResults = new List<StepExecutionResult>();
        var compensationAttempted = false;
        var compensationSucceeded = false;

        try
        {
            using var moduleCts = module.Timeout.HasValue
                ? CancellationTokenSource.CreateLinkedTokenSource(ct)
                : null;

            if (moduleCts is not null)
                moduleCts.CancelAfter(module.Timeout.Value);

            var effectiveCt = moduleCts?.Token ?? ct;

            var declaredScopeResourceIds = module.ResourceRequirements
                .Select(requirement => requirement.ResourceId)
                .ToHashSet(StringComparer.Ordinal);
            var requirements = module.ResourceRequirements
                .Concat(module.Steps
                    .Select(step => new ResourceRequirement($"hardware:{step.Hardware.Id}", step.ResourceAccessMode))
                    .Where(requirement => declaredScopeResourceIds.Contains(requirement.ResourceId)))
                .Where(requirement => !context.FlowReservedResourceIds.Contains(requirement.ResourceId))
                .GroupBy(requirement => requirement.ResourceId, StringComparer.Ordinal)
                .Select(group => group.Any(requirement => requirement.AccessMode == ResourceAccessMode.Exclusive)
                    ? new ResourceRequirement(group.Key, ResourceAccessMode.Exclusive)
                    : new ResourceRequirement(group.Key, ResourceAccessMode.SharedRead))
                .ToList();

            if (requirements.Count > 0)
            {
                resourceLease = await _resourceReservationManager.AcquireAsync(new ResourceReservationRequest
                {
                    WorkflowRunId = context.RootContext.ExecutionId,
                    NodeRunId = Guid.NewGuid(),
                    Requirements = requirements,
                    WaitTimeout = module.ResourceWaitTimeout,
                    Description = module.Name
                }, effectiveCt);
            }

            moduleReservedResourceIds.UnionWith(requirements.Select(requirement => requirement.ResourceId));

            // 按 ExecutionMode 分组执行
            // 相同模式的步骤可以一起执行
            var groups = module.Steps
                .GroupBy(s => s.ExecutionMode)
                .OrderBy(g => g.Min(s => s.Order))
                .ToList();

            foreach (var group in groups)
            {
                if (effectiveCt.IsCancellationRequested)
                {
                    moduleSuccess = false;
                    moduleError = "Module execution cancelled";
                    break;
                }

                var orderedSteps = group.OrderBy(s => s.Order).ToList();

                if (group.Key == ExecutionMode.Sequential)
                {
                    // 串行执行：逐个 await
                    foreach (var step in orderedSteps)
                    {
                        var stepResult = await ExecuteStepAsync(step, context, moduleReservedResourceIds, module.ResourceWaitTimeout, effectiveCt);
                        stepResults.Add(stepResult);

                        if (!stepResult.IsSuccess && !step.ContinueOnFailure)
                        {
                            moduleSuccess = false;
                            moduleError = stepResult.ErrorMessage;
                            moduleRouteKey = stepResult.RouteKey;
                            break;
                        }
                    }
                }
                else
                {
                    // 并行执行：同时启动所有 Task，等待全部完成
                    var stepTasks = orderedSteps
                        .Select(step => ExecuteStepAsync(step, context, moduleReservedResourceIds, module.ResourceWaitTimeout, effectiveCt))
                        .ToList();
                    var results = await Task.WhenAll(stepTasks);
                    stepResults.AddRange(results);

                    if (results.Any(r => !r.IsSuccess) && !module.ContinueOnFailure)
                    {
                        moduleSuccess = false;
                    moduleError = results.First(r => !r.IsSuccess).ErrorMessage;
                    moduleRouteKey = results.First(r => !r.IsSuccess).RouteKey;
                    }
                }

                if (!moduleSuccess && !module.ContinueOnFailure)
                    break;
            }
        }
        catch (ResourceReservationTimeoutException ex)
        {
            moduleSuccess = false;
            moduleRouteKey = "Timeout";
            moduleError = ex.Message;
        }
        catch (ResourceReservationBusyException ex)
        {
            moduleSuccess = false;
            moduleRouteKey = "Busy";
            moduleError = ex.Message;
        }
        catch (OperationCanceledException)
        {
            moduleSuccess = false;
            moduleRouteKey = "Cancelled";
            moduleError = "Module execution cancelled by CancellationToken";
        }
        catch (Exception ex)
        {
            moduleSuccess = false;
            moduleRouteKey = "Failed";
            moduleError = ex.Message;
        }
        finally
        {
            if (!moduleSuccess && module.CompensationSteps.Count > 0)
            {
                compensationAttempted = true;
                var compensationTimeout = module.CompensationTimeout > TimeSpan.Zero
                    ? module.CompensationTimeout
                    : TimeSpan.FromSeconds(10);
                using var compensationCts = new CancellationTokenSource(compensationTimeout);
                foreach (var compensationStep in module.CompensationSteps.OrderByDescending(step => step.Order))
                {
                    var compensationResult = await ExecuteStepAsync(
                        compensationStep,
                        context,
                        moduleReservedResourceIds,
                        module.ResourceWaitTimeout,
                        compensationCts.Token);
                    compensationResults.Add(compensationResult);
                    if (!compensationResult.IsSuccess && !compensationStep.ContinueOnFailure)
                        break;
                }
                compensationSucceeded = compensationResults.Count == module.CompensationSteps.Count
                    && compensationResults.All(item => item.IsSuccess);
                if (!compensationSucceeded)
                {
                    var compensationError = compensationResults.FirstOrDefault(item => !item.IsSuccess)?.ErrorMessage
                        ?? "Compensation did not complete.";
                    moduleError = $"{moduleError} Compensation failed: {compensationError}".Trim();
                }
            }

            if (resourceLease is not null)
                await resourceLease.DisposeAsync();
        }

        var moduleDuration = DateTime.UtcNow - moduleStart;
        var result = new ModuleExecutionResult
        {
            ModuleId = module.ModuleId,
            ModuleName = module.Name,
            IsSuccess = moduleSuccess,
            Duration = moduleDuration,
            ErrorMessage = moduleError,
            RouteKey = moduleRouteKey,
            StepResults = stepResults,
            CompensationAttempted = compensationAttempted,
            CompensationSucceeded = compensationSucceeded,
            CompensationResults = compensationResults
        };

        context.ModuleResults.Add(result);
        return result;
    }

    private async Task<StepExecutionResult> ExecuteStepAsync(
        HardwareStep step,
        ExecutionContext context,
        IReadOnlySet<string> moduleReservedResourceIds,
        TimeSpan? resourceWaitTimeout,
        CancellationToken ct)
    {
        var stepStart = DateTime.UtcNow;
        ResourceLease? commandLease = null;

        try
        {
            using var stepCts = step.Timeout.HasValue
                ? CancellationTokenSource.CreateLinkedTokenSource(ct)
                : null;

            if (stepCts is not null)
                stepCts.CancelAfter(step.Timeout.Value);

            var effectiveCt = stepCts?.Token ?? ct;
            var resourceId = $"hardware:{step.Hardware.Id}";
            if (!context.FlowReservedResourceIds.Contains(resourceId)
                && !moduleReservedResourceIds.Contains(resourceId))
            {
                commandLease = await _resourceReservationManager.AcquireAsync(new ResourceReservationRequest
                {
                    WorkflowRunId = context.RootContext.ExecutionId,
                    NodeRunId = step.StepId,
                    Requirements = new[] { new ResourceRequirement(resourceId, step.ResourceAccessMode) },
                    WaitTimeout = resourceWaitTimeout,
                    Description = $"Command: {step.Name}"
                }, effectiveCt);
            }

            var commandResult = await _stepExecutor.ExecuteAsync(step, context, effectiveCt);
            if (!string.IsNullOrWhiteSpace(step.ResultVariable))
                context.SetVariable(step.ResultVariable, commandResult.Data);

            var stepDuration = DateTime.UtcNow - stepStart;

            return new StepExecutionResult
            {
                StepId = step.StepId,
                StepName = step.Name,
                HardwareId = step.Hardware.Id,
                HardwareName = step.Hardware.Name,
                CommandName = step.Command.CommandName,
                IsSuccess = commandResult.IsSuccess,
                Duration = stepDuration,
                CommandResult = commandResult,
                ErrorMessage = commandResult.IsSuccess ? null : commandResult.ErrorMessage
                ,RouteKey = commandResult.Status.ToString()
            };
        }
        catch (ResourceReservationTimeoutException ex)
        {
            return CreateFailedStepResult(step, stepStart, "Timeout", ex.Message);
        }
        catch (ResourceReservationBusyException ex)
        {
            return CreateFailedStepResult(step, stepStart, "Busy", ex.Message);
        }
        catch (OperationCanceledException)
        {
            return new StepExecutionResult
            {
                StepId = step.StepId,
                StepName = step.Name,
                HardwareId = step.Hardware.Id,
                HardwareName = step.Hardware.Name,
                CommandName = step.Command.CommandName,
                IsSuccess = false,
                Duration = DateTime.UtcNow - stepStart,
                ErrorMessage = "Step execution cancelled"
                ,RouteKey = "Cancelled"
            };
        }
        catch (Exception ex)
        {
            return new StepExecutionResult
            {
                StepId = step.StepId,
                StepName = step.Name,
                HardwareId = step.Hardware.Id,
                HardwareName = step.Hardware.Name,
                CommandName = step.Command.CommandName,
                IsSuccess = false,
                Duration = DateTime.UtcNow - stepStart,
                ErrorMessage = ex.Message
                ,RouteKey = "Failed"
            };
        }
        finally
        {
            if (commandLease is not null)
                await commandLease.DisposeAsync();
        }
    }

    private static StepExecutionResult CreateFailedStepResult(
        HardwareStep step,
        DateTime startedAt,
        string routeKey,
        string errorMessage) => new()
        {
            StepId = step.StepId,
            StepName = step.Name,
            HardwareId = step.Hardware.Id,
            HardwareName = step.Hardware.Name,
            CommandName = step.Command.CommandName,
            IsSuccess = false,
            Duration = DateTime.UtcNow - startedAt,
            ErrorMessage = errorMessage,
            RouteKey = routeKey
        };
}
