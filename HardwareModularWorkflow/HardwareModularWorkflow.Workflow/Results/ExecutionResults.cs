using HardwareModularWorkflow.Hardware.Results;

namespace HardwareModularWorkflow.Workflow.Results;

/// <summary>
/// 单个步骤执行结果
/// </summary>
public sealed class StepExecutionResult
{
    public required Guid StepId { get; init; }
    public string StepName { get; init; } = string.Empty;
    public required long HardwareId { get; init; }
    public string HardwareName { get; init; } = string.Empty;
    public string CommandName { get; init; } = string.Empty;
    public bool IsSuccess { get; init; }
    public TimeSpan Duration { get; init; }
    public CommandResult? CommandResult { get; init; }
    public string? ErrorMessage { get; init; }
}

/// <summary>
/// 模块执行结果
/// </summary>
public sealed class ModuleExecutionResult
{
    public required Guid ModuleId { get; init; }
    public string ModuleName { get; init; } = string.Empty;
    public bool IsSuccess { get; init; }
    public TimeSpan Duration { get; init; }
    public string? ErrorMessage { get; init; }
    public List<StepExecutionResult> StepResults { get; init; } = new();
}

/// <summary>
/// 流执行结果
/// </summary>
public sealed class FlowExecutionResult
{
    public required long FlowId { get; init; }
    public string FlowName { get; init; } = string.Empty;
    public bool IsSuccess { get; init; }
    public TimeSpan Duration { get; init; }
    public string? ErrorMessage { get; init; }
    public List<ModuleExecutionResult> ModuleResults { get; init; } = new();
}
