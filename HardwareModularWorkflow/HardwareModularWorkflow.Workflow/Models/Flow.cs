using HardwareModularWorkflow.Hardware.Enums;
using HardwareModularWorkflow.Workflow.Resources;

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

    /// <summary>定义版本；一次运行启动后应固定使用该版本对应的定义快照。</summary>
    public int DefinitionVersion { get; set; } = 1;

    /// <summary>进程中断后允许采用的恢复方式；默认禁止自动或人工重放。</summary>
    public WorkflowRecoveryPolicy RecoveryPolicy { get; set; } = WorkflowRecoveryPolicy.NotRecoverable;

    /// <summary>标签</summary>
    public List<string> Tags { get; set; } = new();

    /// <summary>调用该工作流时允许提供的输入参数。</summary>
    public List<FlowParameterDefinition> InputParameters { get; set; } = new();

    /// <summary>工作流完成时允许向调用方公开的输出参数。</summary>
    public List<FlowParameterDefinition> OutputParameters { get; set; } = new();

    /// <summary>模块列表（按顺序执行）</summary>
    public List<Module> Modules { get; set; } = new();

    /// <summary>引用的子流（嵌套）</summary>
    public List<FlowReference> SubFlows { get; set; } = new();

    /// <summary>图工作流节点。为空时保持使用现有线性工作流行为。</summary>
    public List<WorkflowNode> GraphNodes { get; set; } = new();

    /// <summary>图工作流的有向连接。</summary>
    public List<WorkflowEdge> GraphEdges { get; set; } = new();

    /// <summary>
    /// 工作流启动后需要持续独占或共享的关键资源。适合轨迹、夹具等必须在整个流程内保持一致的硬件。
    /// </summary>
    public List<ResourceRequirement> ResourceRequirements { get; set; } = new();

    /// <summary>工作流级资源预约的基础优先级。</summary>
    public int ResourcePriority { get; set; }

    /// <summary>工作流级资源申请允许等待的最长时间；不包含取得租约后的执行时间。</summary>
    public TimeSpan? ResourceWaitTimeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>单次图执行允许经过节点的最大次数，防止配置回边造成无限循环。</summary>
    public int MaxGraphNodeVisits { get; set; } = 1000;

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

public enum WorkflowRecoveryPolicy
{
    NotRecoverable,
    RestartFromBeginning
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

    /// <summary>子工作流输入名到父工作流可见值名称的显式映射。</summary>
    public Dictionary<string, string> InputBindings { get; set; } = new(StringComparer.Ordinal);

    /// <summary>子工作流输出名到父工作流变量名的显式映射。</summary>
    public Dictionary<string, string> OutputBindings { get; set; } = new(StringComparer.Ordinal);

    /// <summary>同一子工作流已经运行时的调用策略。</summary>
    public FlowInvocationPolicy InvocationPolicy { get; set; } = FlowInvocationPolicy.Reentrant;
}

public enum FlowInvocationPolicy
{
    Reentrant,
    Exclusive,
    JoinRunning,
    RejectIfRunning
}
