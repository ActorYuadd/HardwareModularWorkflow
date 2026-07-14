using HardwareModularWorkflow.Hardware.Abstractions;
using HardwareModularWorkflow.Hardware.Enums;
using HardwareModularWorkflow.Hardware.Results;
using HardwareModularWorkflow.Workflow.Abstractions;
using HardwareModularWorkflow.Workflow.Events;
using HardwareModularWorkflow.Workflow.Models;
using HardwareModularWorkflow.Core.Events;
using HardwareModularWorkflow.Core.Orchestration;
using ExecutionContext = HardwareModularWorkflow.Workflow.Abstractions.ExecutionContext;

namespace HardwareModularWorkflow.Core.Orchestration;

/// <summary>
/// Core 层步骤执行器：实现 IStepExecutor 接口
/// 
/// 执行链路：
/// HardwareStep → HardwareDriverFactory 获取/创建驱动 → 调用 driver.ExecuteAsync →
/// 结果回传 → 发布事件 → 记录日志
/// </summary>
public sealed class CoreStepExecutor : IStepExecutor
{
    private readonly HardwareDriverFactory _driverFactory;
    private readonly IEventBus _eventBus;

    public CoreStepExecutor(HardwareDriverFactory driverFactory, IEventBus eventBus)
    {
        _driverFactory = driverFactory ?? throw new ArgumentNullException(nameof(driverFactory));
        _eventBus = eventBus ?? throw new ArgumentNullException(nameof(eventBus));
    }

    public async Task<CommandResult> ExecuteAsync(HardwareStep step, ExecutionContext context, CancellationToken ct = default)
    {
        var hardware = step.Hardware;
        var command = step.Command;

        // 1. 发布步骤开始事件
        await _eventBus.PublishAsync(WorkflowEvent.StepStarted(
            context.ExecutionId,
            step.StepId,
            hardware.Name,
            command.CommandName
        ));

        CommandResult result;
        var startTime = DateTime.UtcNow;

        try
        {
            // 2. 获取或创建硬件驱动
            var driver = await _driverFactory.GetOrCreateDriverAsync(hardware.Id, ct);

            // 3. 执行命令
            result = await driver.ExecuteAsync(command, ct);

            // 4. 更新硬件状态
            if (result.IsSuccess)
            {
                hardware.State = HardwareState.Idle; // 执行完成回到空闲
            }
            else
            {
                hardware.State = HardwareState.Error;
            }
        }
        catch (OperationCanceledException)
        {
            result = CommandResult.Cancelled(DateTime.UtcNow - startTime);
            hardware.State = HardwareState.Idle;
        }
        catch (Exception ex)
        {
            result = CommandResult.Failed(DateTime.UtcNow - startTime, "EXECUTOR_ERROR", ex.Message);
            hardware.State = HardwareState.Error;
        }

        var duration = DateTime.UtcNow - startTime;

        // 5. 发布完成/失败事件
        if (result.IsSuccess)
        {
            await _eventBus.PublishAsync(WorkflowEvent.StepCompleted(
                context.ExecutionId,
                step.StepId,
                duration,
                hardware.Name
            ));
        }
        else
        {
            await _eventBus.PublishAsync(WorkflowEvent.StepFailed(
                context.ExecutionId,
                step.StepId,
                result.ErrorMessage ?? "Unknown error",
                hardware.Name
            ));
        }

        return result;
    }
}
