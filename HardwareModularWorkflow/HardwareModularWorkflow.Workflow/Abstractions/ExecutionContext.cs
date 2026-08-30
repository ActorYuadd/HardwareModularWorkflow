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
    public Guid ExecutionId { get; init; } = Guid.NewGuid();

    /// <summary>父上下文（根执行为 null）</summary>
    public ExecutionContext? ParentContext { get; init; }

    /// <summary>本次调用的输入快照；调用开始后不可修改。</summary>
    public IReadOnlyDictionary<string, object?> Inputs { get; init; } =
        new Dictionary<string, object?>(StringComparer.Ordinal);

    /// <summary>本次调用内部变量。不同工作流运行实例之间绝不共享。</summary>
    public Dictionary<string, object?> Variables { get; } = new(StringComparer.Ordinal);

    /// <summary>本次调用向调用方公开的输出。只能通过参数绑定跨工作流边界传递。</summary>
    public Dictionary<string, object?> Outputs { get; } = new(StringComparer.Ordinal);

    /// <summary>当前流程已在工作流级别预留的资源；模块执行时不重复申请。</summary>
    public HashSet<string> FlowReservedResourceIds { get; } = new(StringComparer.Ordinal);

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

    /// <summary>读取当前调用的输入参数。</summary>
    public bool TryGetInput(string name, out object? value) => Inputs.TryGetValue(name, out value);

    /// <summary>写入当前调用的内部变量。</summary>
    public void SetVariable(string name, object? value) => Variables[name] = value;

    /// <summary>写入当前调用对外公开的输出参数。</summary>
    public void SetOutput(string name, object? value) => Outputs[name] = value;

    /// <summary>创建子上下文（用于嵌套流执行）</summary>
    public ExecutionContext CreateChildContext(long flowId, IReadOnlyDictionary<string, object?>? inputs = null)
    {
        var child = new ExecutionContext
        {
            ParentContext = this,
            CurrentFlowId = flowId,
            ExecutionPath = new List<long>(ExecutionPath) { flowId },
            Inputs = inputs ?? new Dictionary<string, object?>(StringComparer.Ordinal)
        };
        child.FlowReservedResourceIds.UnionWith(FlowReservedResourceIds);
        return child;
    }

    /// <summary>
    /// 创建图工作流 Fork 分支专用上下文。分支继承进入 Fork 时的可见状态，
    /// 但 Variables、Outputs 和执行结果均为私有副本，Join 不会隐式合并并发写入。
    /// </summary>
    public ExecutionContext CreateForkBranchContext()
    {
        var branch = new ExecutionContext
        {
            ParentContext = this,
            CurrentFlowId = CurrentFlowId,
            CurrentModuleId = CurrentModuleId,
            ExecutionPath = new List<long>(ExecutionPath),
            Inputs = Inputs
        };

        foreach (var (name, value) in Variables)
            branch.Variables[name] = value;
        foreach (var (name, value) in Outputs)
            branch.Outputs[name] = value;
        branch.FlowReservedResourceIds.UnionWith(FlowReservedResourceIds);

        return branch;
    }
}
