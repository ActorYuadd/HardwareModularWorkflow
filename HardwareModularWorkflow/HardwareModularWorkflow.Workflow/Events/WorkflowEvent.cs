namespace HardwareModularWorkflow.Workflow.Events;

/// <summary>
/// 工作流事件：用于 Core 层的事件总线或 UI 层的通知
/// </summary>
public sealed class WorkflowEvent
{
    /// <summary>事件类型</summary>
    public required WorkflowEventType EventType { get; init; }

    /// <summary>事件时间戳</summary>
    public DateTime Timestamp { get; init; } = DateTime.UtcNow;

    /// <summary>执行 ID</summary>
    public Guid ExecutionId { get; init; }

    /// <summary>流 ID</summary>
    public long? FlowId { get; init; }

    /// <summary>模块 ID</summary>
    public Guid? ModuleId { get; init; }

    /// <summary>步骤 ID</summary>
    public Guid? StepId { get; init; }

    /// <summary>硬件 ID</summary>
    public long? HardwareId { get; init; }

    /// <summary>消息内容</summary>
    public string Message { get; init; } = string.Empty;

    /// <summary>详细数据（JSON 或字典）</summary>
    public object? Data { get; init; }

    /// <summary>是否错误事件</summary>
    public bool IsError => EventType == WorkflowEventType.Error || EventType == WorkflowEventType.StepFailed || EventType == WorkflowEventType.ModuleFailed || EventType == WorkflowEventType.FlowFailed;

    // --- 工厂方法 ---

    public static WorkflowEvent StepStarted(Guid executionId, Guid stepId, string hardwareName, string commandName) =>
        new() { EventType = WorkflowEventType.StepStarted, ExecutionId = executionId, StepId = stepId, Message = $"Step started: {hardwareName} - {commandName}" };

    public static WorkflowEvent StepCompleted(Guid executionId, Guid stepId, TimeSpan duration, string hardwareName) =>
        new() { EventType = WorkflowEventType.StepCompleted, ExecutionId = executionId, StepId = stepId, Message = $"Step completed: {hardwareName} ({duration.TotalMilliseconds:F0}ms)" };

    public static WorkflowEvent StepFailed(Guid executionId, Guid stepId, string error, string hardwareName) =>
        new() { EventType = WorkflowEventType.StepFailed, ExecutionId = executionId, StepId = stepId, Message = $"Step failed: {hardwareName} - {error}" };

    public static WorkflowEvent ModuleStarted(Guid executionId, Guid moduleId, string moduleName) =>
        new() { EventType = WorkflowEventType.ModuleStarted, ExecutionId = executionId, ModuleId = moduleId, Message = $"Module started: {moduleName}" };

    public static WorkflowEvent ModuleCompleted(Guid executionId, Guid moduleId, TimeSpan duration, string moduleName) =>
        new() { EventType = WorkflowEventType.ModuleCompleted, ExecutionId = executionId, ModuleId = moduleId, Message = $"Module completed: {moduleName} ({duration.TotalMilliseconds:F0}ms)" };

    public static WorkflowEvent ModuleFailed(Guid executionId, Guid moduleId, string error, string moduleName) =>
        new() { EventType = WorkflowEventType.ModuleFailed, ExecutionId = executionId, ModuleId = moduleId, Message = $"Module failed: {moduleName} - {error}" };

    public static WorkflowEvent FlowStarted(Guid executionId, long flowId, string flowName) =>
        new() { EventType = WorkflowEventType.FlowStarted, ExecutionId = executionId, FlowId = flowId, Message = $"Flow started: {flowName}" };

    public static WorkflowEvent FlowCompleted(Guid executionId, long flowId, TimeSpan duration, string flowName) =>
        new() { EventType = WorkflowEventType.FlowCompleted, ExecutionId = executionId, FlowId = flowId, Message = $"Flow completed: {flowName} ({duration.TotalMilliseconds:F0}ms)" };

    public static WorkflowEvent FlowFailed(Guid executionId, long flowId, string error, string flowName) =>
        new() { EventType = WorkflowEventType.FlowFailed, ExecutionId = executionId, FlowId = flowId, Message = $"Flow failed: {flowName} - {error}" };

    public static WorkflowEvent Cancelled(Guid executionId) =>
        new() { EventType = WorkflowEventType.Cancelled, ExecutionId = executionId, Message = "Execution cancelled" };

    public static WorkflowEvent Error(Guid executionId, string error) =>
        new() { EventType = WorkflowEventType.Error, ExecutionId = executionId, Message = error };
}

public enum WorkflowEventType
{
    StepStarted,
    StepCompleted,
    StepFailed,
    ModuleStarted,
    ModuleCompleted,
    ModuleFailed,
    FlowStarted,
    FlowCompleted,
    FlowFailed,
    Cancelled,
    Error,
    Info
}
