using HardwareModularWorkflow.Workflow.Abstractions;
using HardwareModularWorkflow.Workflow.Models;
using HardwareModularWorkflow.Workflow.Results;
using HardwareModularWorkflow.Hardware.Enums;
using ExecutionContext = HardwareModularWorkflow.Workflow.Abstractions.ExecutionContext;

namespace HardwareModularWorkflow.Workflow.Engine;

/// <summary>
/// 流执行器：执行单个流的所有模块
/// 支持串行/并行模块执行，以及子流引用（通过委托回调将子流加入调度队列）
/// </summary>
public sealed class FlowExecutor
{
    private readonly ModuleExecutor _moduleExecutor;

    public FlowExecutor(ModuleExecutor moduleExecutor)
    {
        _moduleExecutor = moduleExecutor ?? throw new ArgumentNullException(nameof(moduleExecutor));
    }

    /// <summary>
    /// 执行流的所有模块
    /// </summary>
    /// <param name="flow">要执行的流</param>
    /// <param name="context">执行上下文</param>
    /// <param name="subFlowCallback">子流回调：遇到子流引用时调用此委托，将子流加入调度队列</param>
    /// <param name="ct">取消令牌</param>
    public async Task<FlowExecutionResult> ExecuteAsync(
        Flow flow,
        ExecutionContext context,
        Func<FlowReference, ExecutionContext, Task<WorkItemResult>>? subFlowCallback = null,
        CancellationToken ct = default)
    {
        context.CurrentFlowId = flow.FlowId;
        context.RecordFlowEntry(flow.FlowId);

        var flowStart = DateTime.UtcNow;
        var moduleResults = new List<ModuleExecutionResult>();
        bool flowSuccess = true;
        string? flowError = null;

        try
        {
            using var flowCts = flow.Timeout.HasValue
                ? CancellationTokenSource.CreateLinkedTokenSource(ct)
                : null;

            if (flowCts is not null)
                flowCts.CancelAfter(flow.Timeout.Value);

            var effectiveCt = flowCts?.Token ?? ct;

            // 1. 执行模块
            var modules = flow.GetOrderedModules().ToList();

            if (flow.ExecutionMode == ExecutionMode.Sequential)
            {
                // 串行：逐个执行模块
                foreach (var module in modules)
                {
                    if (effectiveCt.IsCancellationRequested)
                    {
                        flowSuccess = false;
                        flowError = "Flow execution cancelled";
                        break;
                    }

                    var moduleResult = await _moduleExecutor.ExecuteAsync(module, context, effectiveCt);
                    moduleResults.Add(moduleResult);

                    if (!moduleResult.IsSuccess && !module.ContinueOnFailure)
                    {
                        flowSuccess = false;
                        flowError = moduleResult.ErrorMessage;
                        break;
                    }
                }
            }
            else
            {
                // 并行：同时启动所有模块
                var moduleTasks = modules.Select(m => _moduleExecutor.ExecuteAsync(m, context, effectiveCt)).ToList();
                var results = await Task.WhenAll(moduleTasks);
                moduleResults.AddRange(results);

                if (results.Any(r => !r.IsSuccess) && !flow.ContinueOnFailure)
                {
                    flowSuccess = false;
                    flowError = results.First(r => !r.IsSuccess).ErrorMessage;
                }
            }

            // 2. 执行子流引用（通过回调加入调度队列，不递归）
            if (flowSuccess && subFlowCallback is not null)
            {
                foreach (var subFlowRef in flow.SubFlows.OrderBy(s => s.Order))
                {
                    if (effectiveCt.IsCancellationRequested)
                    {
                        flowSuccess = false;
                        flowError = "Flow execution cancelled during sub-flow dispatch";
                        break;
                    }

                    // 循环检测：如果子流已在当前路径中，跳过（防止死循环）
                    if (context.HasVisitedFlow(subFlowRef.ReferencedFlowId))
                    {
                        // 记录警告但不中断执行
                        continue;
                    }

                    var subFlowResult = await subFlowCallback(subFlowRef, context);
                    if (!subFlowResult.IsSuccess && !flow.ContinueOnFailure)
                    {
                        flowSuccess = false;
                        flowError = subFlowResult.ErrorMessage;
                        break;
                    }
                }
            }
        }
        catch (OperationCanceledException)
        {
            flowSuccess = false;
            flowError = "Flow execution cancelled by CancellationToken";
        }
        catch (Exception ex)
        {
            flowSuccess = false;
            flowError = ex.Message;
        }

        var flowDuration = DateTime.UtcNow - flowStart;
        var result = new FlowExecutionResult
        {
            FlowId = flow.FlowId,
            FlowName = flow.Name,
            IsSuccess = flowSuccess,
            Duration = flowDuration,
            ErrorMessage = flowError,
            ModuleResults = moduleResults
        };

        context.FlowResults.Add(result);
        return result;
    }
}
