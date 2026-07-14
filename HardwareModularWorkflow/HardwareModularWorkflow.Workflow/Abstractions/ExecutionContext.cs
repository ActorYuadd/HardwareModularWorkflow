using HardwareModularWorkflow.Workflow.Models;
using HardwareModularWorkflow.Workflow.Results;

namespace HardwareModularWorkflow.Workflow.Abstractions;

/// <summary>
/// 执行上下文：跟踪工作流的执行状态、路径、结果
/// 每个 WorkItem 有独立的 ExecutionContext，嵌套流时创建子上下文
/// </summary>
public sealed class ExecutionContext
{
    /// <summary>本次执行的全局唯一 ID</summary>
    public Guid ExecutionId { get; } = Guid.NewGuid();

    /// <summary>父上下文（根执行为 null）</summary>
    public ExecutionContext? ParentContext { get; init; }

    /// <summary>当前执行的流 ID（与数据库 FlowEntity.Id 对应）</summary>
    public long CurrentFlowId { get; set; }

    /// <summary>当前执行的模块 ID</summary>
    public Guid CurrentModuleId { get; set; }

    /// <summary>执行路径：已执行的流 ID 列表（用于循环检测）</summary>
    public List<long> ExecutionPath { get; set; } = new();

    /// <summary>执行结果收集</summary>
    public List<StepExecutionResult> StepResults { get; } = new();

    /// <summary>模块执行结果收集</summary>
    public List<ModuleExecutionResult> ModuleResults { get; } = new();

    /// <summary>流执行结果收集</summary>
    public List<FlowExecutionResult> FlowResults { get; } = new();

    /// <summary>当前嵌套深度</summary>
    public int NestingDepth => ParentContext?.NestingDepth + 1 ?? 0;

    /// <summary>全局开始时间</summary>
    public DateTime StartTime { get; } = DateTime.UtcNow;

    /// <summary>是否已取消</summary>
    public bool IsCancelled { get; set; }

    /// <summary>是否发生错误</summary>
    public bool HasError { get; set; }

    /// <summary>根上下文</summary>
    public ExecutionContext RootContext => ParentContext?.RootContext ?? this;

    /// <summary>记录流进入路径（用于循环检测）</summary>
    public void RecordFlowEntry(long flowId) => ExecutionPath.Add(flowId);

    /// <summary>检查是否已访问过该流（循环检测）</summary>
    public bool HasVisitedFlow(long flowId) => ExecutionPath.Contains(flowId);

    /// <summary>创建子上下文（用于嵌套流执行）</summary>
    public ExecutionContext CreateChildContext(long flowId)
    {
        return new ExecutionContext
        {
            ParentContext = this,
            CurrentFlowId = flowId,
            ExecutionPath = new List<long>(ExecutionPath) { flowId }
        };
    }
}
