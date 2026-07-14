using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using HardwareModularWorkflow.Hardware.Enums;

namespace HardwareModularWorkflow.Db.Entities;

/// <summary>
/// 硬件定义表：硬件类型模板（电机、制温、制冷、自定义）
/// </summary>
public class HardwareDefinition
{
    [Key]
    public long Id { get; set; }

    [Required, MaxLength(100)]
    public string Name { get; set; } = string.Empty;

    [MaxLength(100)]
    public string? Alias { get; set; }

    [MaxLength(500)]
    public string? Note { get; set; }

    /// <summary>硬件类型：Motor / Temperature / Cooling / Custom</summary>
    [Required, MaxLength(50)]
    public string Type { get; set; } = string.Empty;

    /// <summary>自定义类型时的 JSON Schema（固定类型时为 null）</summary>
    [Column(TypeName = "TEXT")]
    public string? CustomSchemaJson { get; set; }

    /// <summary>默认参数 JSON（固定类型时存储标准属性）</summary>
    [Column(TypeName = "TEXT")]
    public string? DefaultParametersJson { get; set; }

    /// <summary>是否系统预定义（不可删除）</summary>
    public bool IsSystem { get; set; } = false;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    // --- Navigation ---
    public ICollection<HardwareInstance> HardwareInstances { get; set; } = new List<HardwareInstance>();
}
