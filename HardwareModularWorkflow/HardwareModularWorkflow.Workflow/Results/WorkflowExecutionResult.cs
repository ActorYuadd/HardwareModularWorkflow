namespace HardwareModularWorkflow.Workflow.Results;

/// <summary>
/// 工作流执行结果汇总
/// </summary>
public sealed class WorkflowExecutionResult
{
    /// <summary>执行 ID</summary>
    public required Guid ExecutionId { get; init; }

    /// <summary>是否整体成功</summary>
    public bool IsSuccess { get; init; }

    /// <summary>总耗时</summary>
    public TimeSpan TotalDuration { get; init; }

    /// <summary>错误信息（失败时）</summary>
    public string? ErrorMessage { get; init; }

    /// <summary>是否被取消</summary>
    public bool IsCancelled { get; init; }

    /// <summary>执行的流数量</summary>
    public int FlowCount { get; init; }

    /// <summary>执行的模块数量</summary>
    public int ModuleCount { get; init; }

    /// <summary>执行的步骤数量</summary>
    public int StepCount { get; init; }

    /// <summary>成功步骤数</summary>
    public int SuccessStepCount { get; init; }

    /// <summary>失败步骤数</summary>
    public int FailedStepCount { get; init; }
}
