using HardwareModularWorkflow.Hardware.Enums;
using HardwareModularWorkflow.Workflow.Resources;

namespace HardwareModularWorkflow.Workflow.Models;

/// <summary>
/// 模块（Module）：工作流最小单元，由多个硬件步骤组合
/// 模块内步骤可串行或并行执行
/// </summary>
public sealed class Module
{
    /// <summary>模块 ID</summary>
    public required Guid ModuleId { get; init; } = Guid.NewGuid();

    /// <summary>模块名称</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>别名</summary>
    public string Alias { get; set; } = string.Empty;

    /// <summary>备注</summary>
    public string Note { get; set; } = string.Empty;

    /// <summary>标签</summary>
    public List<string> Tags { get; set; } = new();

    /// <summary>硬件步骤列表</summary>
    public List<HardwareStep> Steps { get; set; } = new();

    /// <summary>模块失败或取消后，在释放模块范围资源前按逆序执行的显式安全补偿步骤。</summary>
    public List<HardwareStep> CompensationSteps { get; set; } = new();

    /// <summary>补偿链的独立超时；原执行令牌取消后仍允许在此安全窗口内执行。</summary>
    public TimeSpan CompensationTimeout { get; set; } = TimeSpan.FromSeconds(10);

    /// <summary>模块级恢复声明，供运行快照、人工恢复判断和后续状态核验使用。</summary>
    public ModuleRecoveryPolicy RecoveryPolicy { get; set; } = ModuleRecoveryPolicy.NotRecoverable;

    /// <summary>
    /// 模块开始前额外预留的资源。模块步骤引用的硬件会由执行器自动加入独占资源，
    /// 此列表用于轨迹关联轴、夹具等未在当前步骤直接执行但必须保持一致的硬件。
    /// </summary>
    public List<ResourceRequirement> ResourceRequirements { get; set; } = new();

    /// <summary>模块范围租约和模块内命令租约允许等待的最长时间。</summary>
    public TimeSpan? ResourceWaitTimeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>模块内默认执行模式（步骤可覆盖）</summary>
    public ExecutionMode DefaultExecutionMode { get; set; } = ExecutionMode.Sequential;

    /// <summary>核心事件：模块执行完成/失败时触发</summary>
    public string? CoreEvent { get; set; }

    /// <summary>通知事件：用于 UI 通知</summary>
    public string? NotifyEvent { get; set; }

    /// <summary>模块执行超时（整个模块）</summary>
    public TimeSpan? Timeout { get; set; }

    /// <summary>执行失败后是否继续执行后续模块（流级）</summary>
    public bool ContinueOnFailure { get; set; } = false;

    /// <summary>获取按 Order 排序的步骤</summary>
    public IEnumerable<HardwareStep> GetOrderedSteps() => Steps.OrderBy(s => s.Order);

    /// <summary>按 ExecutionMode 分组步骤</summary>
    public IEnumerable<IGrouping<ExecutionMode, HardwareStep>> GetGroupedSteps() =>
        Steps.GroupBy(s => s.ExecutionMode);
}

public enum ModuleRecoveryPolicy
{
    NotRecoverable,
    Reexecute,
    VerifyHardwareState,
    ReturnToSafePositionThenReexecute
}
