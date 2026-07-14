using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace HardwareModularWorkflow.Db.Entities;

/// <summary>
/// 执行日志表：记录每次工作流/模块/步骤的执行结果
/// </summary>
public class ExecutionLog
{
    [Key]
    public long Id { get; set; }

    /// <summary>执行全局 ID（关联一次完整的执行会话）</summary>
    public Guid ExecutionId { get; set; }

    /// <summary>日志类型：Flow / Module / Step / Controller</summary>
    [Required, MaxLength(50)]
    public string LogType { get; set; } = string.Empty;

    /// <summary>关联的流 ID（可空）</summary>
    public long? FlowId { get; set; }

    /// <summary>关联的流名称</summary>
    [MaxLength(100)]
    public string? FlowName { get; set; }

    /// <summary>关联的模块 ID（可空）</summary>
    public long? ModuleId { get; set; }

    [MaxLength(100)]
    public string? ModuleName { get; set; }

    /// <summary>步骤 ID（可空）</summary>
    public long? StepId { get; set; }

    [MaxLength(100)]
    public string? StepName { get; set; }

    /// <summary>硬件实例 ID（可空）</summary>
    public long? HardwareInstanceId { get; set; }

    [MaxLength(100)]
    public string? HardwareName { get; set; }

    /// <summary>命令名称</summary>
    [MaxLength(100)]
    public string? CommandName { get; set; }

    /// <summary>是否成功</summary>
    public bool IsSuccess { get; set; }

    /// <summary>执行耗时（毫秒）</summary>
    public long? DurationMs { get; set; }

    /// <summary>错误信息</summary>
    [Column(TypeName = "TEXT")]
    public string? ErrorMessage { get; set; }

    /// <summary>错误码</summary>
    [MaxLength(100)]
    public string? ErrorCode { get; set; }

    /// <summary>详细数据 JSON（命令结果、硬件状态等）</summary>
    [Column(TypeName = "TEXT")]
    public string? DetailsJson { get; set; }

    /// <summary>是否被取消</summary>
    public bool IsCancelled { get; set; }

    /// <summary>执行时间</summary>
    public DateTime ExecutedAt { get; set; } = DateTime.UtcNow;

    /// <summary>执行时间戳（高精度）</summary>
    public long TimestampTicks { get; set; } = DateTime.UtcNow.Ticks;
}

/// <summary>
/// 执行会话表：记录一次完整的执行会话汇总
/// </summary>
public class ExecutionSession
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    [MaxLength(100)]
    public string? Name { get; set; }

    [MaxLength(500)]
    public string? Description { get; set; }

    /// <summary>是否整体成功</summary>
    public bool IsSuccess { get; set; }

    /// <summary>总耗时（毫秒）</summary>
    public long? TotalDurationMs { get; set; }

    /// <summary>执行的流数量</summary>
    public int FlowCount { get; set; }

    /// <summary>执行的模块数量</summary>
    public int ModuleCount { get; set; }

    /// <summary>执行的步骤数量</summary>
    public int StepCount { get; set; }

    /// <summary>成功步骤数</summary>
    public int SuccessStepCount { get; set; }

    /// <summary>失败步骤数</summary>
    public int FailedStepCount { get; set; }

    /// <summary>是否被取消</summary>
    public bool IsCancelled { get; set; }

    /// <summary>取消原因</summary>
    [MaxLength(500)]
    public string? CancelReason { get; set; }

    public DateTime StartedAt { get; set; } = DateTime.UtcNow;
    public DateTime? CompletedAt { get; set; }

    // --- Navigation ---
    public ICollection<ExecutionLog> Logs { get; set; } = new List<ExecutionLog>();
}
