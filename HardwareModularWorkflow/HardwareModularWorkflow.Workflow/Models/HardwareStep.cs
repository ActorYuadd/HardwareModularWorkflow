using HardwareModularWorkflow.Hardware.Abstractions;
using HardwareModularWorkflow.Hardware.Enums;

namespace HardwareModularWorkflow.Workflow.Models;

/// <summary>
/// 硬件步骤：模块内的最小执行单元，对应一个硬件实例和一个命令
/// </summary>
public sealed class HardwareStep
{
    /// <summary>步骤 ID（模块内唯一）</summary>
    public required Guid StepId { get; init; } = Guid.NewGuid();

    /// <summary>步骤名称/描述</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>执行顺序（仅 Sequential 模式下有效）</summary>
    public int Order { get; set; } = 0;

    /// <summary>关联的硬件实例</summary>
    public required IHardware Hardware { get; init; }

    /// <summary>要执行的硬件命令</summary>
    public required IHardwareCommand Command { get; init; }

    /// <summary>执行模式：串行或并行</summary>
    public ExecutionMode ExecutionMode { get; set; } = ExecutionMode.Sequential;

    /// <summary>执行超时（null 使用命令默认超时）</summary>
    public TimeSpan? Timeout { get; set; }

    /// <summary>执行失败后是否继续执行后续步骤</summary>
    public bool ContinueOnFailure { get; set; } = false;
}
