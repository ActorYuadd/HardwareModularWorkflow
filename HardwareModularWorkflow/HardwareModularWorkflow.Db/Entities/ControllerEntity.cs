using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace HardwareModularWorkflow.Db.Entities;

/// <summary>
/// 控制器配置表：支持多个 Plc/Can 控制器
/// </summary>
public class ControllerEntity
{
    [Key]
    public long Id { get; set; }

    [Required, MaxLength(50)]
    public string ControllerType { get; set; } = string.Empty; // "Plc" / "Can"

    [Required, MaxLength(50)]
    public string VendorName { get; set; } = string.Empty; // "ZLG" / "LeadSys"

    [Required, MaxLength(100)]
    public string Name { get; set; } = string.Empty; // 用户自定义名称

    [MaxLength(255)]
    public string? Description { get; set; }

    /// <summary>连接参数 JSON（设备类型、波特率、IP、端口等）</summary>
    [Column(TypeName = "TEXT")]
    public string ConnectionConfigJson { get; set; } = "{}";

    /// <summary>是否默认控制器</summary>
    public bool IsDefault { get; set; } = false;

    /// <summary>创建时间</summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>更新时间</summary>
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>是否启用</summary>
    public bool IsEnabled { get; set; } = true;

    // --- Navigation ---
    public ICollection<HardwareInstance> HardwareInstances { get; set; } = new List<HardwareInstance>();
}
