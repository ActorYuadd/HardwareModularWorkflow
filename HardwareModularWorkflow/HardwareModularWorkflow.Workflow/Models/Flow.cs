using HardwareModularWorkflow.Hardware.Enums;

namespace HardwareModularWorkflow.Workflow.Models;

/// <summary>
/// 流（Flow）：由多个模块组合的工作流，可独立命名和复用
/// 流级支持串行/并行执行，以及嵌套（引用其他流）
/// </summary>
public sealed class Flow
{
    /// <summary>流 ID（与数据库 FlowEntity.Id 对应）</summary>
    public required long FlowId { get; init; }

    /// <summary>流名称</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>别名</summary>
    public string Alias { get; set; } = string.Empty;

    /// <summary>备注</summary>
    public string Note { get; set; } = string.Empty;

    /// <summary>标签</summary>
    public List<string> Tags { get; set; } = new();

    /// <summary>模块列表（按顺序执行）</summary>
    public List<Module> Modules { get; set; } = new();

    /// <summary>引用的子流（嵌套）</summary>
    public List<FlowReference> SubFlows { get; set; } = new();

    /// <summary>流级默认执行模式：串行（逐个模块）或并行（所有模块同时）</summary>
    public ExecutionMode ExecutionMode { get; set; } = ExecutionMode.Sequential;

    /// <summary>核心事件</summary>
    public string? CoreEvent { get; set; }

    /// <summary>通知事件</summary>
    public string? NotifyEvent { get; set; }

    /// <summary>流执行超时</summary>
    public TimeSpan? Timeout { get; set; }

    /// <summary>执行失败后是否继续</summary>
    public bool ContinueOnFailure { get; set; } = false;

    /// <summary>最大允许嵌套深度（0 表示不限制）</summary>
    public int MaxNestingDepth { get; set; } = 0; // 0 = unlimited as per user decision

    /// <summary>获取按 Order 排序的模块（如果模块有 Order 属性）- 当前按列表顺序</summary>
    public IEnumerable<Module> GetOrderedModules() => Modules;
}

/// <summary>
/// 流引用：嵌套其他流时使用
/// </summary>
public sealed class FlowReference
{
    /// <summary>被引用的流 ID（与数据库 FlowEntity.Id 对应）</summary>
    public required long ReferencedFlowId { get; init; }

    /// <summary>引用名称</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>执行顺序</summary>
    public int Order { get; set; } = 0;

    /// <summary>执行模式</summary>
    public ExecutionMode ExecutionMode { get; set; } = ExecutionMode.Sequential;

    /// <summary>执行条件（为空表示无条件执行）</summary>
    public string? Condition { get; set; }
}
