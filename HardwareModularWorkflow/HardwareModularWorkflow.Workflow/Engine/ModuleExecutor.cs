using HardwareModularWorkflow.Hardware.Abstractions;
using HardwareModularWorkflow.Hardware.Enums;
using HardwareModularWorkflow.Hardware.Results;
using HardwareModularWorkflow.Workflow.Abstractions;
using HardwareModularWorkflow.Workflow.Models;
using HardwareModularWorkflow.Workflow.Results;
using ExecutionContext = HardwareModularWorkflow.Workflow.Abstractions.ExecutionContext;

namespace HardwareModularWorkflow.Workflow.Engine;

/// <summary>
/// 模块执行器：执行单个模块内的所有硬件步骤
/// 支持串行（Sequential）和并行（Parallel）执行模式
/// </summary>
public sealed class ModuleExecutor
{
    private readonly IStepExecutor _stepExecutor;

    public ModuleExecutor(IStepExecutor stepExecutor)
    {
        _stepExecutor = stepExecutor ?? throw new ArgumentNullException(nameof(stepExecutor));
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

        try
        {
            using var moduleCts = module.Timeout.HasValue
                ? CancellationTokenSource.CreateLinkedTokenSource(ct)
                : null;

            if (moduleCts is not null)
                moduleCts.CancelAfter(module.Timeout.Value);

            var effectiveCt = moduleCts?.Token ?? ct;

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
                        var stepResult = await ExecuteStepAsync(step, context, effectiveCt);
                        stepResults.Add(stepResult);

                        if (!stepResult.IsSuccess && !step.ContinueOnFailure)
                        {
                            moduleSuccess = false;
                            moduleError = stepResult.ErrorMessage;
                            break;
                        }
                    }
                }
                else
                {
                    // 并行执行：同时启动所有 Task，等待全部完成
                    var stepTasks = orderedSteps.Select(s => ExecuteStepAsync(s, context, effectiveCt)).ToList();
                    var results = await Task.WhenAll(stepTasks);
                    stepResults.AddRange(results);

                    if (results.Any(r => !r.IsSuccess) && !module.ContinueOnFailure)
                    {
                        moduleSuccess = false;
                        moduleError = results.First(r => !r.IsSuccess).ErrorMessage;
                    }
                }

                if (!moduleSuccess && !module.ContinueOnFailure)
                    break;
            }
        }
        catch (OperationCanceledException)
        {
            moduleSuccess = false;
            moduleError = "Module execution cancelled by CancellationToken";
        }
        catch (Exception ex)
        {
            moduleSuccess = false;
            moduleError = ex.Message;
        }

        var moduleDuration = DateTime.UtcNow - moduleStart;
        var result = new ModuleExecutionResult
        {
            ModuleId = module.ModuleId,
            ModuleName = module.Name,
            IsSuccess = moduleSuccess,
            Duration = moduleDuration,
            ErrorMessage = moduleError,
            StepResults = stepResults
        };

        context.ModuleResults.Add(result);
        return result;
    }

    private async Task<StepExecutionResult> ExecuteStepAsync(HardwareStep step, ExecutionContext context, CancellationToken ct)
    {
        var stepStart = DateTime.UtcNow;

        try
        {
            using var stepCts = step.Timeout.HasValue
                ? CancellationTokenSource.CreateLinkedTokenSource(ct)
                : null;

            if (stepCts is not null)
                stepCts.CancelAfter(step.Timeout.Value);

            var effectiveCt = stepCts?.Token ?? ct;
            var commandResult = await _stepExecutor.ExecuteAsync(step, context, effectiveCt);

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
            };
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
            };
        }
    }
}
