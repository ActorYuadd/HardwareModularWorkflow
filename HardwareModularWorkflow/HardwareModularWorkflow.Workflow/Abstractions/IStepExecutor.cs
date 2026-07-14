using HardwareModularWorkflow.Workflow.Models;

namespace HardwareModularWorkflow.Workflow.Abstractions;

/// <summary>
/// 步骤执行器接口：Workflow 层定义执行契约，Core 层实现具体执行逻辑
/// Core 层实现时负责：查找控制器 → 创建 IHardwareDriver → 调用 ExecuteAsync
/// </summary>
public interface IStepExecutor
{
    /// <summary>
    /// 执行单个硬件步骤
    /// </summary>
    /// <param name="step">硬件步骤</param>
    /// <param name="context">执行上下文</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>命令执行结果</returns>
    Task<Hardware.Results.CommandResult> ExecuteAsync(HardwareStep step, ExecutionContext context, CancellationToken ct = default);
}
