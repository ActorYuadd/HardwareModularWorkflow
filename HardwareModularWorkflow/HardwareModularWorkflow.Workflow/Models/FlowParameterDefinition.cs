namespace HardwareModularWorkflow.Workflow.Models;

/// <summary>
/// 工作流公开参数的类型。参数契约用于工作流启动和子工作流调用，
/// 不用于直接描述硬件驱动的原始命令参数。
/// </summary>
public enum FlowParameterType
{
    String,
    Boolean,
    Integer,
    Decimal,
    DateTime,
    Guid,
    Json
}

/// <summary>
/// 工作流输入或输出参数定义。
/// </summary>
public sealed class FlowParameterDefinition
{
    /// <summary>参数名称；同一输入或输出契约内必须唯一。</summary>
    public required string Name { get; init; }

    /// <summary>参数数据类型。</summary>
    public FlowParameterType Type { get; init; } = FlowParameterType.String;

    /// <summary>调用方是否必须提供该参数。</summary>
    public bool IsRequired { get; init; }

    /// <summary>调用方未提供时使用的默认值。</summary>
    public object? DefaultValue { get; init; }

    /// <summary>面向编辑器和调用方的说明。</summary>
    public string? Description { get; init; }
}
