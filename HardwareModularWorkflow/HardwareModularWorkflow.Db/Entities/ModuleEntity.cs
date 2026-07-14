using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace HardwareModularWorkflow.Db.Entities;

/// <summary>
/// 模块定义表：工作流最小单元
/// </summary>
public class ModuleEntity
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
    public string? Tags { get; set; } // 逗号分隔的标签

    /// <summary>模块内默认执行模式：Sequential / Parallel</summary>
    [Required, MaxLength(50)]
    public string DefaultExecutionMode { get; set; } = "Sequential";

    /// <summary>核心事件</summary>
    [MaxLength(100)]
    public string? CoreEvent { get; set; }

    /// <summary>通知事件</summary>
    [MaxLength(100)]
    public string? NotifyEvent { get; set; }

    /// <summary>模块执行超时（毫秒）</summary>
    public int? TimeoutMs { get; set; }

    /// <summary>执行失败后是否继续</summary>
    public bool ContinueOnFailure { get; set; } = false;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    // --- Navigation ---
    public ICollection<ModuleStepEntity> Steps { get; set; } = new List<ModuleStepEntity>();
    public ICollection<FlowModuleRelation> FlowRelations { get; set; } = new List<FlowModuleRelation>();
}

/// <summary>
/// 模块步骤表：模块内的硬件执行步骤
/// </summary>
public class ModuleStepEntity
{
    [Key]
    public long Id { get; set; }

    [Required, MaxLength(100)]
    public string Name { get; set; } = string.Empty;

    /// <summary>执行顺序</summary>
    public int OrderIndex { get; set; } = 0;

    /// <summary>执行模式：Sequential / Parallel</summary>
    [Required, MaxLength(50)]
    public string ExecutionMode { get; set; } = "Sequential";

    /// <summary>关联的硬件实例</summary>
    public long HardwareInstanceId { get; set; }
    [ForeignKey(nameof(HardwareInstanceId))]
    public HardwareInstance HardwareInstance { get; set; } = null!;

    /// <summary>命令名称（如 MoveTo、SetSpeed、Start）</summary>
    [Required, MaxLength(100)]
    public string CommandName { get; set; } = string.Empty;

    /// <summary>命令参数 JSON</summary>
    [Column(TypeName = "TEXT")]
    public string? CommandParametersJson { get; set; }

    /// <summary>是否异步命令</summary>
    public bool IsAsync { get; set; } = true;

    /// <summary>步骤超时（毫秒）</summary>
    public int? TimeoutMs { get; set; }

    /// <summary>失败后是否继续</summary>
    public bool ContinueOnFailure { get; set; } = false;

    /// <summary>所属模块</summary>
    public long ModuleId { get; set; }
    [ForeignKey(nameof(ModuleId))]
    public ModuleEntity Module { get; set; } = null!;
}
