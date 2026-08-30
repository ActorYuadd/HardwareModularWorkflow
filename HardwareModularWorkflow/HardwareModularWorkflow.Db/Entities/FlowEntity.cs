using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace HardwareModularWorkflow.Db.Entities;

/// <summary>
/// 流定义表：工作流/模组
/// </summary>
public class FlowEntity
{
    [Key]
    public long Id { get; set; }

    [Required, MaxLength(100)]
    public string Name { get; set; } = string.Empty;

    [MaxLength(100)]
    public string? Alias { get; set; }

    [MaxLength(500)]
    public string? Note { get; set; }

    [MaxLength(50)]
    public string? Tags { get; set; }

    public int DefinitionVersion { get; set; } = 1;

    [Required, MaxLength(40)]
    public string RecoveryPolicy { get; set; } = "NotRecoverable";

    /// <summary>流级执行模式：Sequential / Parallel</summary>
    [Required, MaxLength(50)]
    public string ExecutionMode { get; set; } = "Sequential";

    [MaxLength(100)]
    public string? CoreEvent { get; set; }

    [MaxLength(100)]
    public string? NotifyEvent { get; set; }

    /// <summary>流执行超时（毫秒）</summary>
    public int? TimeoutMs { get; set; }

    /// <summary>资源等待超时（毫秒），与工作流执行超时分开计算。</summary>
    public int ResourceWaitTimeoutMs { get; set; } = 30000;

    /// <summary>图执行的全局节点访问保险上限。</summary>
    public int MaxGraphNodeVisits { get; set; } = 1000;

    /// <summary>失败后是否继续</summary>
    public bool ContinueOnFailure { get; set; } = false;

    /// <summary>是否可作为子流被引用</summary>
    public bool IsReusable { get; set; } = true;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    // --- Navigation ---
    public ICollection<FlowModuleRelation> ModuleRelations { get; set; } = new List<FlowModuleRelation>();
    public ICollection<FlowSubFlowReference> SubFlowReferences { get; set; } = new List<FlowSubFlowReference>();
    public ICollection<FlowSubFlowReference> ParentFlowReferences { get; set; } = new List<FlowSubFlowReference>();
    public ICollection<FlowGraphNode> GraphNodes { get; set; } = new List<FlowGraphNode>();
    public ICollection<FlowGraphEdge> GraphEdges { get; set; } = new List<FlowGraphEdge>();
    public ICollection<FlowResourceReservation> ResourceReservations { get; set; } = new List<FlowResourceReservation>();
    public ICollection<FlowParameterEntity> Parameters { get; set; } = new List<FlowParameterEntity>();
}

/// <summary>持久化的工作流输入/输出契约。</summary>
public class FlowParameterEntity
{
    [Key]
    public long Id { get; set; }
    public long FlowId { get; set; }
    [ForeignKey(nameof(FlowId))]
    public FlowEntity Flow { get; set; } = null!;
    [Required, MaxLength(20)] public string Direction { get; set; } = "Input";
    [Required, MaxLength(100)] public string Name { get; set; } = string.Empty;
    [Required, MaxLength(30)] public string ParameterType { get; set; } = "String";
    public bool IsRequired { get; set; }
    public string? DefaultValueJson { get; set; }
    [MaxLength(500)] public string? Description { get; set; }
}

/// <summary>工作流运行期间持续持有的关键硬件资源。</summary>
public class FlowResourceReservation
{
    [Key]
    public long Id { get; set; }

    public long FlowId { get; set; }
    [ForeignKey(nameof(FlowId))]
    public FlowEntity Flow { get; set; } = null!;

    public long HardwareInstanceId { get; set; }
    [ForeignKey(nameof(HardwareInstanceId))]
    public HardwareInstance HardwareInstance { get; set; } = null!;

    [Required, MaxLength(50)]
    public string AccessMode { get; set; } = "Exclusive";

    public int Priority { get; set; }
}

/// <summary>持久化的图工作流节点。</summary>
public class FlowGraphNode
{
    [Key]
    public long Id { get; set; }

    public long FlowId { get; set; }
    [ForeignKey(nameof(FlowId))]
    public FlowEntity Flow { get; set; } = null!;

    /// <summary>运行时图使用的稳定节点标识。</summary>
    public Guid NodeId { get; set; } = Guid.NewGuid();

    [Required, MaxLength(50)]
    public string NodeType { get; set; } = "Module";

    [Required, MaxLength(100)]
    public string Name { get; set; } = string.Empty;

    public long? ModuleId { get; set; }
    [ForeignKey(nameof(ModuleId))]
    public ModuleEntity? Module { get; set; }

    public long? SubFlowId { get; set; }
    [ForeignKey(nameof(SubFlowId))]
    public FlowEntity? SubFlow { get; set; }

    [MaxLength(100)]
    public string? RouteKeyVariable { get; set; }

    [Required, MaxLength(30)]
    public string JoinMode { get; set; } = "WaitAll";

    public int? MaxVisits { get; set; }

    [Required, MaxLength(30)]
    public string SubFlowInvocationPolicy { get; set; } = "Reentrant";

    public double X { get; set; }
    public double Y { get; set; }

    public ICollection<FlowGraphEdge> OutgoingEdges { get; set; } = new List<FlowGraphEdge>();
    public ICollection<FlowGraphEdge> IncomingEdges { get; set; } = new List<FlowGraphEdge>();
    public ICollection<FlowSubFlowParameterBinding> ParameterBindings { get; set; } = new List<FlowSubFlowParameterBinding>();
}

/// <summary>
/// 子工作流节点的显式参数映射。一个记录只能属于线性子流引用或图 SubFlow 节点中的一个。
/// </summary>
public class FlowSubFlowParameterBinding
{
    [Key]
    public long Id { get; set; }

    public long? FlowSubFlowReferenceId { get; set; }
    [ForeignKey(nameof(FlowSubFlowReferenceId))]
    public FlowSubFlowReference? FlowSubFlowReference { get; set; }

    public long? FlowGraphNodeId { get; set; }
    [ForeignKey(nameof(FlowGraphNodeId))]
    public FlowGraphNode? FlowGraphNode { get; set; }

    [Required, MaxLength(20)]
    public string Direction { get; set; } = "Input";

    [Required, MaxLength(100)]
    public string ChildParameterName { get; set; } = string.Empty;

    [Required, MaxLength(100)]
    public string ParentValueName { get; set; } = string.Empty;
}

/// <summary>持久化的图工作流有向边。</summary>
public class FlowGraphEdge
{
    [Key]
    public long Id { get; set; }

    public long FlowId { get; set; }
    [ForeignKey(nameof(FlowId))]
    public FlowEntity Flow { get; set; } = null!;

    public long FromNodeId { get; set; }
    [ForeignKey(nameof(FromNodeId))]
    public FlowGraphNode FromNode { get; set; } = null!;

    public long ToNodeId { get; set; }
    [ForeignKey(nameof(ToNodeId))]
    public FlowGraphNode ToNode { get; set; } = null!;

    [MaxLength(100)]
    public string? RouteKey { get; set; }

    public bool IsDefault { get; set; }
    public int Priority { get; set; }
}

/// <summary>
/// 流-模块关联表：定义流包含哪些模块及执行顺序
/// </summary>
public class FlowModuleRelation
{
    [Key]
    public long Id { get; set; }

    public long FlowId { get; set; }
    [ForeignKey(nameof(FlowId))]
    public FlowEntity Flow { get; set; } = null!;

    public long ModuleId { get; set; }
    [ForeignKey(nameof(ModuleId))]
    public ModuleEntity Module { get; set; } = null!;

    /// <summary>执行顺序</summary>
    public int OrderIndex { get; set; } = 0;

    /// <summary>执行模式：Sequential / Parallel</summary>
    [MaxLength(50)]
    public string? ExecutionMode { get; set; } // null 表示使用流默认模式

    /// <summary>执行条件（为空表示无条件）</summary>
    [MaxLength(500)]
    public string? Condition { get; set; }

}

/// <summary>
/// 子流引用表：流引用其他流（嵌套）
/// </summary>
public class FlowSubFlowReference
{
    [Key]
    public long Id { get; set; }

    /// <summary>父流（引用者）</summary>
    public long ParentFlowId { get; set; }
    [ForeignKey(nameof(ParentFlowId))]
    public FlowEntity ParentFlow { get; set; } = null!;

    /// <summary>被引用的子流</summary>
    public long SubFlowId { get; set; }
    [ForeignKey(nameof(SubFlowId))]
    public FlowEntity SubFlow { get; set; } = null!;

    /// <summary>执行顺序</summary>
    public int OrderIndex { get; set; } = 0;

    /// <summary>执行模式</summary>
    [MaxLength(50)]
    public string? ExecutionMode { get; set; }

    /// <summary>执行条件</summary>
    [MaxLength(500)]
    public string? Condition { get; set; }

    [Required, MaxLength(30)]
    public string InvocationPolicy { get; set; } = "Reentrant";

    public ICollection<FlowSubFlowParameterBinding> ParameterBindings { get; set; } = new List<FlowSubFlowParameterBinding>();
}
