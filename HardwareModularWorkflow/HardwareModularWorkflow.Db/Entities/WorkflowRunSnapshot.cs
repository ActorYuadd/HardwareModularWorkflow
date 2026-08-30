using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace HardwareModularWorkflow.Db.Entities;

/// <summary>跨进程保存的工作流运行快照。恢复只允许按已声明策略重新启动，不从未知硬件状态中间续跑。</summary>
public sealed class WorkflowRunSnapshot
{
    [Key]
    public Guid ExecutionId { get; set; }

    public Guid? RecoveredFromExecutionId { get; set; }
    public long FlowId { get; set; }
    public int DefinitionVersion { get; set; }

    [Required, MaxLength(100)]
    public string FlowName { get; set; } = string.Empty;

    [Required, MaxLength(40)]
    public string Status { get; set; } = WorkflowRunStatuses.Pending;

    [Required, MaxLength(40)]
    public string RecoveryPolicy { get; set; } = "NotRecoverable";

    [Column(TypeName = "TEXT")]
    public string InputsJson { get; set; } = "{}";

    [Column(TypeName = "TEXT")]
    public string? OutputsJson { get; set; }

    [Column(TypeName = "TEXT")]
    public string? ErrorMessage { get; set; }

    public DateTime StartedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? CompletedAtUtc { get; set; }
}

public static class WorkflowRunStatuses
{
    public const string Pending = "Pending";
    public const string Running = "Running";
    public const string Succeeded = "Succeeded";
    public const string Failed = "Failed";
    public const string Cancelled = "Cancelled";
    public const string RecoveryRequired = "RecoveryRequired";
    public const string Recovered = "Recovered";
    public const string SafetyStopped = "SafetyStopped";
}
