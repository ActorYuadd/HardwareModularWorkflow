using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace HardwareModularWorkflow.Db.Entities;

/// <summary>
/// Hardware category describes what a device is, independent of its control mechanism.
/// </summary>
public class HardwareCategory
{
    [Key]
    public long Id { get; set; }

    [Required, MaxLength(50)]
    public string Code { get; set; } = string.Empty;

    [Required, MaxLength(100)]
    public string Name { get; set; } = string.Empty;

    [MaxLength(500)]
    public string? Description { get; set; }

    public bool IsSystem { get; set; } = true;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public ICollection<HardwareControlProfile> ControlProfiles { get; set; } =
        new List<HardwareControlProfile>();

    public ICollection<HardwareDefinition> HardwareDefinitions { get; set; } =
        new List<HardwareDefinition>();
}

/// <summary>
/// Control profile describes how a hardware category is controlled.
/// </summary>
public class HardwareControlProfile
{
    [Key]
    public long Id { get; set; }

    public long CategoryId { get; set; }

    [ForeignKey(nameof(CategoryId))]
    public HardwareCategory Category { get; set; } = null!;

    [Required, MaxLength(50)]
    public string Code { get; set; } = string.Empty;

    [Required, MaxLength(100)]
    public string Name { get; set; } = string.Empty;

    [Required, MaxLength(100)]
    public string DriverKey { get; set; } = string.Empty;

    /// <summary>
    /// Optional controller type required by this profile, such as Plc or Can.
    /// A null value represents direct SDK or protocol control.
    /// </summary>
    [MaxLength(50)]
    public string? RequiredControllerType { get; set; }

    [MaxLength(500)]
    public string? Description { get; set; }

    [Column(TypeName = "TEXT")]
    public string? CommandDefinitionsJson { get; set; }

    public bool IsSystem { get; set; } = true;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public ICollection<HardwareDefinition> HardwareDefinitions { get; set; } =
        new List<HardwareDefinition>();
}
