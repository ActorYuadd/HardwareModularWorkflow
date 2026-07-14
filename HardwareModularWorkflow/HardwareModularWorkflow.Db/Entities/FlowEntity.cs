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

    /// <summary>流级执行模式：Sequential / Parallel</summary>
    [Required, MaxLength(50)]
    public string ExecutionMode { get; set; } = "Sequential";

    [MaxLength(100)]
    public string? CoreEvent { get; set; }

    [MaxLength(100)]
    public string? NotifyEvent { get; set; }

    /// <summary>流执行超时（毫秒）</summary>
    public int? TimeoutMs { get; set; }

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
}
