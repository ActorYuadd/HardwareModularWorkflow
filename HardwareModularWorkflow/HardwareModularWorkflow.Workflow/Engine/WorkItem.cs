using HardwareModularWorkflow.Workflow.Models;
using HardwareModularWorkflow.Workflow.Abstractions;
using ExecutionContext = HardwareModularWorkflow.Workflow.Abstractions.ExecutionContext;

namespace HardwareModularWorkflow.Workflow.Engine;

/// <summary>
/// 工作项：调度器队列中的单个任务单元
/// 可以是模块执行、流执行或子流引用
/// </summary>
public sealed class WorkItem
{
    /// <summary>工作项 ID</summary>
    public Guid WorkItemId { get; } = Guid.NewGuid();

    /// <summary>工作项类型</summary>
    public WorkItemType Type { get; init; }

    /// <summary>关联的流（Type=Flow 或 SubFlow 时）</summary>
    public Flow? Flow { get; init; }

    /// <summary>关联的模块（Type=Module 时）</summary>
    public Module? Module { get; init; }

    /// <summary>关联的流引用（Type=SubFlow 时）</summary>
    public FlowReference? FlowReference { get; init; }

    /// <summary>执行上下文</summary>
    public required ExecutionContext Context { get; init; }

    /// <summary>完成通知的 TaskCompletionSource</summary>
    public TaskCompletionSource<WorkItemResult>? CompletionSource { get; init; }

    /// <summary>父工作项 ID（用于关联）</summary>
    public Guid? ParentWorkItemId { get; init; }

    /// <summary>创建模块工作项</summary>
    public static WorkItem CreateModule(Module module, ExecutionContext context, TaskCompletionSource<WorkItemResult>? tcs = null, Guid? parentId = null) =>
        new()
        {
            Type = WorkItemType.Module,
            Module = module,
            Context = context,
            CompletionSource = tcs,
            ParentWorkItemId = parentId
        };

    /// <summary>创建流工作项</summary>
    public static WorkItem CreateFlow(Flow flow, ExecutionContext context, TaskCompletionSource<WorkItemResult>? tcs = null) =>
        new()
        {
            Type = WorkItemType.Flow,
            Flow = flow,
            Context = context,
            CompletionSource = tcs
        };

    /// <summary>创建子流引用工作项</summary>
    public static WorkItem CreateSubFlow(FlowReference reference, ExecutionContext parentContext, Flow resolvedFlow, TaskCompletionSource<WorkItemResult>? tcs = null, Guid? parentId = null) =>
        new()
        {
            Type = WorkItemType.SubFlow,
            Flow = resolvedFlow,
            FlowReference = reference,
            Context = parentContext.CreateChildContext(resolvedFlow.FlowId),
            CompletionSource = tcs,
            ParentWorkItemId = parentId
        };
}

public enum WorkItemType
{
    Flow,
    Module,
    SubFlow
}

/// <summary>
/// 工作项执行结果
/// </summary>
public sealed class WorkItemResult
{
    /// <summary>是否成功</summary>
    public required bool IsSuccess { get; init; }

    /// <summary>执行耗时</summary>
    public TimeSpan Duration { get; init; }

    /// <summary>错误信息（失败时）</summary>
    public string? ErrorMessage { get; init; }

    /// <summary>执行上下文（包含所有结果）</summary>
    public ExecutionContext? Context { get; init; }

    /// <summary>是否被取消</summary>
    public bool IsCancelled { get; init; }

    public static WorkItemResult Success(TimeSpan duration, ExecutionContext context) =>
        new() { IsSuccess = true, Duration = duration, Context = context };

    public static WorkItemResult Failed(TimeSpan duration, string error, ExecutionContext? context = null) =>
        new() { IsSuccess = false, Duration = duration, ErrorMessage = error, Context = context };

    public static WorkItemResult Cancelled(TimeSpan duration, ExecutionContext? context = null) =>
        new() { IsSuccess = false, Duration = duration, IsCancelled = true, Context = context };
}
