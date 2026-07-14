using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace HardwareModularWorkflow.Db.Entities;

/// <summary>
/// 硬件实例表：具体的硬件设备配置，绑定到控制器
/// </summary>
public class HardwareInstance
{
    [Key]
    public long Id { get; set; }

    [Required, MaxLength(100)]
    public string Name { get; set; } = string.Empty;

    [MaxLength(100)]
    public string? Alias { get; set; }

    [MaxLength(500)]
    public string? Note { get; set; }

    /// <summary>端口/通道标识</summary>
    [MaxLength(50)]
    public string? Port { get; set; }

    /// <summary>设备在控制器上的地址</summary>
    [MaxLength(100)]
    public string? Address { get; set; }

    /// <summary>绑定到 HardwareDefinition 模板</summary>
    public long DefinitionId { get; set; }
    [ForeignKey(nameof(DefinitionId))]
    public HardwareDefinition Definition { get; set; } = null!;

    /// <summary>实例特定参数 JSON（覆盖/扩展模板的默认参数）</summary>
    [Column(TypeName = "TEXT")]
    public string? ParametersJson { get; set; }

    /// <summary>绑定的控制器 ID（外键）</summary>
    public long? ControllerId { get; set; }
    [ForeignKey(nameof(ControllerId))]
    public ControllerEntity? Controller { get; set; }

    /// <summary>控制器类型：Plc / Can（冗余字段，便于快速查询）</summary>
    [MaxLength(50)]
    public string? ControllerType { get; set; }

    /// <summary>是否启用</summary>
    public bool IsEnabled { get; set; } = true;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    // --- Navigation ---
    public ICollection<ModuleStepEntity> ModuleSteps { get; set; } = new List<ModuleStepEntity>();
}
