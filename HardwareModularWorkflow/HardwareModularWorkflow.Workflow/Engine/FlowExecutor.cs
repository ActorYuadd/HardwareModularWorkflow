using HardwareModularWorkflow.Workflow.Abstractions;
using HardwareModularWorkflow.Workflow.Models;
using HardwareModularWorkflow.Workflow.Results;
using HardwareModularWorkflow.Hardware.Enums;
using HardwareModularWorkflow.Workflow.Resources;
using ExecutionContext = HardwareModularWorkflow.Workflow.Abstractions.ExecutionContext;

namespace HardwareModularWorkflow.Workflow.Engine;

/// <summary>
/// 流执行器：执行单个流的所有模块
/// 支持串行/并行模块执行，以及子流引用（通过委托回调将子流加入调度队列）
/// </summary>
public sealed class FlowExecutor
{
    private readonly ModuleExecutor _moduleExecutor;
    private readonly IResourceReservationManager _resourceReservationManager;

    public FlowExecutor(ModuleExecutor moduleExecutor, IResourceReservationManager? resourceReservationManager = null)
    {
        _moduleExecutor = moduleExecutor ?? throw new ArgumentNullException(nameof(moduleExecutor));
        _resourceReservationManager = resourceReservationManager ?? new ResourceReservationManager();
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
        if (flow.GraphNodes.Count > 0)
            return await ExecuteGraphAsync(flow, context, subFlowCallback, ct);

        context.CurrentFlowId = flow.FlowId;
        context.RecordFlowEntry(flow.FlowId);

        var flowStart = DateTime.UtcNow;
        var moduleResults = new List<ModuleExecutionResult>();
        bool flowSuccess = true;
        string? flowError = null;
        var flowRouteKey = "Success";
        ResourceLease? flowLease = null;

        try
        {
            using var flowCts = flow.Timeout.HasValue
                ? CancellationTokenSource.CreateLinkedTokenSource(ct)
                : null;

            if (flowCts is not null)
                flowCts.CancelAfter(flow.Timeout.Value);

            var effectiveCt = flowCts?.Token ?? ct;
            flowLease = await AcquireFlowLeaseAsync(flow, context, effectiveCt);

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
                        flowRouteKey = moduleResult.RouteKey;
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
                        flowRouteKey = subFlowResult.RouteKey;
                        break;
                    }
                }
            }
        }
        catch (ResourceReservationTimeoutException ex)
        {
            flowSuccess = false;
            flowRouteKey = "Timeout";
            flowError = ex.Message;
        }
        catch (ResourceReservationBusyException ex)
        {
            flowSuccess = false;
            flowRouteKey = "Busy";
            flowError = ex.Message;
        }
        catch (OperationCanceledException)
        {
            flowSuccess = false;
            flowRouteKey = "Cancelled";
            flowError = "Flow execution cancelled by CancellationToken";
        }
        catch (Exception ex)
        {
            flowSuccess = false;
            flowRouteKey = "Failed";
            flowError = ex.Message;
        }
        finally
        {
            flowLease?.Dispose();
            ClearFlowReservationContext(flow, context);
        }

        var flowDuration = DateTime.UtcNow - flowStart;
        var result = new FlowExecutionResult
        {
            FlowId = flow.FlowId,
            FlowName = flow.Name,
            IsSuccess = flowSuccess,
            Duration = flowDuration,
            ErrorMessage = flowError,
            RouteKey = flowRouteKey,
            ModuleResults = moduleResults,
            Outputs = new Dictionary<string, object?>(context.Outputs, StringComparer.Ordinal)
        };

        context.FlowResults.Add(result);
        return result;
    }

    private async Task<FlowExecutionResult> ExecuteGraphAsync(
        Flow flow,
        ExecutionContext context,
        Func<FlowReference, ExecutionContext, Task<WorkItemResult>>? subFlowCallback,
        CancellationToken ct)
    {
        context.CurrentFlowId = flow.FlowId;
        context.RecordFlowEntry(flow.FlowId);

        var startTime = DateTime.UtcNow;
        var moduleResults = new List<ModuleExecutionResult>();
        var success = false;
        string? error = null;
        var resultRouteKey = "Failed";
        ResourceLease? flowLease = null;

        try
        {
            if (!WorkflowGraphValidator.TryValidate(flow, out var validationError))
                throw new InvalidOperationException(validationError);

            using var flowCts = flow.Timeout.HasValue
                ? CancellationTokenSource.CreateLinkedTokenSource(ct)
                : null;
            if (flowCts is not null)
                flowCts.CancelAfter(flow.Timeout.Value);
            var effectiveCt = flowCts?.Token ?? ct;
            flowLease = await AcquireFlowLeaseAsync(flow, context, effectiveCt);

            var nodes = flow.GraphNodes.ToDictionary(node => node.NodeId);
            var currentNode = flow.GraphNodes.Single(node => node.Type == WorkflowNodeType.Start);
            var nodeVisits = 0;
            var visitsByNode = new Dictionary<Guid, int>();

            while (true)
            {
                effectiveCt.ThrowIfCancellationRequested();
                if (++nodeVisits > flow.MaxGraphNodeVisits)
                    throw new InvalidOperationException($"Workflow graph exceeded its node visit limit ({flow.MaxGraphNodeVisits}).");
                var currentVisits = visitsByNode.GetValueOrDefault(currentNode.NodeId) + 1;
                visitsByNode[currentNode.NodeId] = currentVisits;
                if (currentNode.MaxVisits.HasValue && currentVisits > currentNode.MaxVisits.Value)
                    throw new InvalidOperationException(
                        $"Node '{currentNode.Name}' exceeded its visit limit ({currentNode.MaxVisits.Value}).");

                if (currentNode.Type == WorkflowNodeType.End)
                {
                    success = true;
                    resultRouteKey = "Success";
                    break;
                }

                string routeKey;
                switch (currentNode.Type)
                {
                    case WorkflowNodeType.Start:
                        routeKey = "Success";
                        break;

                    case WorkflowNodeType.Module:
                    {
                        var moduleResult = await _moduleExecutor.ExecuteAsync(currentNode.Module!, context, effectiveCt);
                        moduleResults.Add(moduleResult);
                        routeKey = moduleResult.RouteKey;
                        break;
                    }

                    case WorkflowNodeType.SubFlow:
                    {
                        if (subFlowCallback is null)
                            throw new InvalidOperationException("Graph contains a sub-flow node but no sub-flow callback is configured.");
                        var subFlowResult = await subFlowCallback(currentNode.SubFlow!, context);
                        routeKey = subFlowResult.RouteKey;
                        break;
                    }

                    case WorkflowNodeType.Switch:
                        routeKey = ResolveRouteKey(currentNode, context);
                        break;

                    case WorkflowNodeType.Fork:
                    {
                        var joinNode = await ExecuteForkAsync(
                            flow, nodes, currentNode, context, subFlowCallback, moduleResults, effectiveCt);
                        var joinEdge = SelectNextEdge(flow.GraphEdges, joinNode.NodeId, "Success");
                        if (joinEdge is null)
                            throw new InvalidOperationException($"Join node '{joinNode.Name}' has no outgoing edge.");
                        currentNode = nodes[joinEdge.ToNodeId];
                        continue;
                    }

                    case WorkflowNodeType.Join:
                        throw new InvalidOperationException(
                            $"Join node '{currentNode.Name}' was reached outside its owning Fork execution.");

                    default:
                        throw new InvalidOperationException($"Unknown graph node type '{currentNode.Type}'.");
                }

                var edge = SelectNextEdge(flow.GraphEdges, currentNode.NodeId, routeKey);
                if (edge is null)
                    throw new InvalidOperationException($"Node '{currentNode.Name}' has no edge for route '{routeKey}'.");
                currentNode = nodes[edge.ToNodeId];
            }
        }
        catch (ResourceReservationTimeoutException ex)
        {
            resultRouteKey = "Timeout";
            error = ex.Message;
        }
        catch (ResourceReservationBusyException ex)
        {
            resultRouteKey = "Busy";
            error = ex.Message;
        }
        catch (OperationCanceledException)
        {
            resultRouteKey = "Cancelled";
            error = "Flow graph execution cancelled by CancellationToken";
        }
        catch (Exception ex)
        {
            error ??= ex.Message;
            resultRouteKey = "Failed";
        }
        finally
        {
            flowLease?.Dispose();
            ClearFlowReservationContext(flow, context);
        }

        var result = new FlowExecutionResult
        {
            FlowId = flow.FlowId,
            FlowName = flow.Name,
            IsSuccess = success,
            Duration = DateTime.UtcNow - startTime,
            ErrorMessage = error,
            RouteKey = resultRouteKey,
            ModuleResults = moduleResults,
            Outputs = new Dictionary<string, object?>(context.Outputs, StringComparer.Ordinal)
        };
        context.FlowResults.Add(result);
        return result;
    }

    private async Task<ResourceLease?> AcquireFlowLeaseAsync(Flow flow, ExecutionContext context, CancellationToken ct)
    {
        if (flow.ResourceRequirements.Count == 0)
            return null;

        var declaredResourceIds = flow.ResourceRequirements
            .Select(requirement => requirement.ResourceId)
            .ToHashSet(StringComparer.Ordinal);
        var referencedModules = flow.Modules
            .Concat(flow.GraphNodes.Where(node => node.Module is not null).Select(node => node.Module!));
        var requirements = flow.ResourceRequirements
            .Concat(referencedModules
                .SelectMany(module => module.Steps)
                .Select(step => new ResourceRequirement($"hardware:{step.Hardware.Id}", step.ResourceAccessMode))
                .Where(requirement => declaredResourceIds.Contains(requirement.ResourceId)))
            .GroupBy(requirement => requirement.ResourceId, StringComparer.Ordinal)
            .Select(group => group.Any(requirement => requirement.AccessMode == ResourceAccessMode.Exclusive)
                ? new ResourceRequirement(group.Key, ResourceAccessMode.Exclusive)
                : new ResourceRequirement(group.Key, ResourceAccessMode.SharedRead))
            .ToList();

        var lease = await _resourceReservationManager.AcquireAsync(new ResourceReservationRequest
        {
            WorkflowRunId = context.RootContext.ExecutionId,
            NodeRunId = Guid.NewGuid(),
            Requirements = requirements,
            BasePriority = flow.ResourcePriority,
            WaitTimeout = flow.ResourceWaitTimeout,
            Description = $"Flow: {flow.Name}"
        }, ct);
        foreach (var requirement in requirements)
            context.FlowReservedResourceIds.Add(requirement.ResourceId);
        return lease;
    }

    private static void ClearFlowReservationContext(Flow flow, ExecutionContext context)
    {
        foreach (var requirement in flow.ResourceRequirements)
            context.FlowReservedResourceIds.Remove(requirement.ResourceId);
    }

    private async Task<WorkflowNode> ExecuteForkAsync(
        Flow flow,
        IReadOnlyDictionary<Guid, WorkflowNode> nodes,
        WorkflowNode forkNode,
        ExecutionContext parentContext,
        Func<FlowReference, ExecutionContext, Task<WorkItemResult>>? subFlowCallback,
        List<ModuleExecutionResult> aggregateModuleResults,
        CancellationToken ct)
    {
        var branchStarts = flow.GraphEdges
            .Where(edge => edge.FromNodeId == forkNode.NodeId)
            .OrderByDescending(edge => edge.Priority)
            .Select(edge => nodes[edge.ToNodeId])
            .ToList();
        var joinIds = branchStarts
            .Select(branchStart => FindFirstJoinNode(flow, nodes, branchStart.NodeId))
            .Distinct()
            .ToList();
        if (joinIds.Count != 1)
            throw new InvalidOperationException(
                $"Fork node '{forkNode.Name}' branches must converge at the same Join node.");

        var joinNode = nodes[joinIds[0]];
        using var branchCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var branchTasks = branchStarts.Select(branchStart => ExecuteBranchToJoinAsync(
            flow, nodes, branchStart, parentContext.CreateForkBranchContext(), subFlowCallback, branchCts.Token)).ToList();

        if (joinNode.JoinMode == WorkflowJoinMode.WaitAll)
        {
            var branches = await Task.WhenAll(branchTasks);
            foreach (var branch in branches)
                aggregateModuleResults.AddRange(branch.ModuleResults);
        }
        else
        {
            var completedTask = await Task.WhenAny(branchTasks);
            GraphBranchResult? winner = null;
            Exception? winnerError = null;
            try
            {
                winner = await completedTask;
            }
            catch (Exception ex)
            {
                winnerError = ex;
            }

            branchCts.Cancel();
            try { await Task.WhenAll(branchTasks); }
            catch { /* Losers are cancelled and awaited so their resource leases finish releasing. */ }

            if (winnerError is not null)
                throw new InvalidOperationException($"WaitAny branch failed before reaching Join '{joinNode.Name}'.", winnerError);
            aggregateModuleResults.AddRange(winner!.ModuleResults);
        }

        return joinNode;
    }

    private static Guid FindFirstJoinNode(
        Flow flow,
        IReadOnlyDictionary<Guid, WorkflowNode> nodes,
        Guid branchStart)
    {
        var pending = new Queue<Guid>();
        var visited = new HashSet<Guid>();
        pending.Enqueue(branchStart);
        while (pending.TryDequeue(out var nodeId))
        {
            if (!visited.Add(nodeId)) continue;
            if (nodes[nodeId].Type == WorkflowNodeType.Join) return nodeId;
            foreach (var edge in flow.GraphEdges.Where(edge => edge.FromNodeId == nodeId))
                pending.Enqueue(edge.ToNodeId);
        }
        throw new InvalidOperationException("Fork branch has no reachable Join node.");
    }

    private async Task<GraphBranchResult> ExecuteBranchToJoinAsync(
        Flow flow,
        IReadOnlyDictionary<Guid, WorkflowNode> nodes,
        WorkflowNode currentNode,
        ExecutionContext context,
        Func<FlowReference, ExecutionContext, Task<WorkItemResult>>? subFlowCallback,
        CancellationToken ct)
    {
        var moduleResults = new List<ModuleExecutionResult>();
        var nodeVisits = 0;
        var visitsByNode = new Dictionary<Guid, int>();

        while (true)
        {
            ct.ThrowIfCancellationRequested();
            if (++nodeVisits > flow.MaxGraphNodeVisits)
                throw new InvalidOperationException($"Workflow graph branch exceeded its node visit limit ({flow.MaxGraphNodeVisits}).");
            var currentVisits = visitsByNode.GetValueOrDefault(currentNode.NodeId) + 1;
            visitsByNode[currentNode.NodeId] = currentVisits;
            if (currentNode.MaxVisits.HasValue && currentVisits > currentNode.MaxVisits.Value)
                throw new InvalidOperationException(
                    $"Node '{currentNode.Name}' exceeded its visit limit ({currentNode.MaxVisits.Value}).");

            if (currentNode.Type == WorkflowNodeType.Join)
                return new GraphBranchResult(currentNode.NodeId, moduleResults);
            if (currentNode.Type == WorkflowNodeType.End)
                throw new InvalidOperationException("A Fork branch reached End before a Join node.");

            string routeKey;
            switch (currentNode.Type)
            {
                case WorkflowNodeType.Start:
                    routeKey = "Success";
                    break;
                case WorkflowNodeType.Module:
                {
                    var result = await _moduleExecutor.ExecuteAsync(currentNode.Module!, context, ct);
                    moduleResults.Add(result);
                    routeKey = result.RouteKey;
                    break;
                }
                case WorkflowNodeType.SubFlow:
                {
                    if (subFlowCallback is null)
                        throw new InvalidOperationException("Graph contains a sub-flow node but no sub-flow callback is configured.");
                    var result = await subFlowCallback(currentNode.SubFlow!, context);
                    routeKey = result.RouteKey;
                    break;
                }
                case WorkflowNodeType.Switch:
                    routeKey = ResolveRouteKey(currentNode, context);
                    break;
                case WorkflowNodeType.Fork:
                {
                    var joinNode = await ExecuteForkAsync(
                        flow, nodes, currentNode, context, subFlowCallback, moduleResults, ct);
                    var joinEdge = SelectNextEdge(flow.GraphEdges, joinNode.NodeId, "Success");
                    if (joinEdge is null)
                        throw new InvalidOperationException($"Join node '{joinNode.Name}' has no outgoing edge.");
                    currentNode = nodes[joinEdge.ToNodeId];
                    continue;
                }
                default:
                    throw new InvalidOperationException($"Node '{currentNode.Name}' cannot execute inside a Fork branch.");
            }

            var edge = SelectNextEdge(flow.GraphEdges, currentNode.NodeId, routeKey);
            if (edge is null)
                throw new InvalidOperationException($"Node '{currentNode.Name}' has no edge for route '{routeKey}'.");
            currentNode = nodes[edge.ToNodeId];
        }
    }

    private static WorkflowEdge? SelectNextEdge(
        IEnumerable<WorkflowEdge> edges,
        Guid nodeId,
        string routeKey) =>
        edges
            .Where(edge => edge.FromNodeId == nodeId)
            .OrderByDescending(edge => edge.Priority)
            .FirstOrDefault(edge => string.Equals(edge.RouteKey, routeKey, StringComparison.Ordinal))
        ?? edges
            .Where(edge => edge.FromNodeId == nodeId && edge.IsDefault)
            .OrderByDescending(edge => edge.Priority)
            .FirstOrDefault()
        ?? (string.Equals(routeKey, "Success", StringComparison.Ordinal)
            ? SelectOnlyOutgoingEdge(edges, nodeId)
            : null);

    private static WorkflowEdge? SelectOnlyOutgoingEdge(IEnumerable<WorkflowEdge> edges, Guid nodeId)
    {
        var candidates = edges
            .Where(edge => edge.FromNodeId == nodeId)
            .OrderByDescending(edge => edge.Priority)
            .Take(2)
            .ToList();
        return candidates.Count == 1 ? candidates[0] : null;
    }

    private static string ResolveRouteKey(WorkflowNode node, ExecutionContext context)
    {
        var name = node.RouteKeyVariable!;
        if (context.Variables.TryGetValue(name, out var value)
            || context.Outputs.TryGetValue(name, out value)
            || context.Inputs.TryGetValue(name, out value))
            return value?.ToString() ?? string.Empty;

        throw new InvalidOperationException($"Switch node '{node.Name}' cannot find route-key variable '{name}'.");
    }

    private sealed record GraphBranchResult(Guid JoinNodeId, IReadOnlyList<ModuleExecutionResult> ModuleResults);
}
