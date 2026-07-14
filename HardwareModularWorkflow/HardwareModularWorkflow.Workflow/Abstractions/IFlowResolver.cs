using HardwareModularWorkflow.Workflow.Models;

namespace HardwareModularWorkflow.Workflow.Abstractions;

/// <summary>
/// 流解析器接口：将数据库中的流 ID 解析为运行时 Flow 模型
/// 由 Core 层实现并注入 WorkflowScheduler
/// </summary>
public interface IFlowResolver
{
    /// <summary>
    /// 根据流 ID 解析为运行时 Flow 模型（包含模块和子流引用）
    /// </summary>
    /// <param name="flowId">数据库 FlowEntity.Id</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>解析后的 Flow 模型，未找到时返回 null</returns>
    Task<Flow?> ResolveAsync(long flowId, CancellationToken ct = default);
}
