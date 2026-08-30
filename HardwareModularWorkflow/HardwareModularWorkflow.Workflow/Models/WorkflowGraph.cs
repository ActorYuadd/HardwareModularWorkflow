namespace HardwareModularWorkflow.Workflow.Models;

/// <summary>图工作流节点类型。</summary>
public enum WorkflowNodeType
{
    Start,
    End,
    Module,
    SubFlow,
    Switch,
    Fork,
    Join
}

/// <summary>Join 对并行分支的等待策略。</summary>
public enum WorkflowJoinMode
{
    WaitAll,
    WaitAny
}

/// <summary>图工作流中的一个节点。</summary>
public sealed class WorkflowNode
{
    public Guid NodeId { get; init; } = Guid.NewGuid();
    public WorkflowNodeType Type { get; init; }
    public string Name { get; set; } = string.Empty;
    public Module? Module { get; init; }
    public FlowReference? SubFlow { get; init; }

    /// <summary>Join 节点等待策略；默认等待全部上游分支。</summary>
    public WorkflowJoinMode JoinMode { get; init; } = WorkflowJoinMode.WaitAll;

    /// <summary>该节点在单次图运行中允许被访问的次数；用于明确限制回边。</summary>
    public int? MaxVisits { get; init; }

    /// <summary>
    /// Switch 路由键来源。按 Variables、Outputs、Inputs 的顺序读取，
    /// 并与边的 RouteKey 比较。
    /// </summary>
    public string? RouteKeyVariable { get; init; }
}

/// <summary>图工作流中的有向连接。</summary>
public sealed class WorkflowEdge
{
    public Guid EdgeId { get; init; } = Guid.NewGuid();
    public required Guid FromNodeId { get; init; }
    public required Guid ToNodeId { get; init; }
    public string? RouteKey { get; init; }
    public bool IsDefault { get; init; }
    public int Priority { get; init; }
}

/// <summary>图定义保存前和运行前的结构验证。</summary>
public static class WorkflowGraphValidator
{
    public static bool TryValidate(Flow flow, out string? errorMessage)
    {
        ArgumentNullException.ThrowIfNull(flow);
        var nodes = flow.GraphNodes;
        var nodeIds = nodes.Select(node => node.NodeId).ToList();

        if (flow.MaxGraphNodeVisits <= 0)
        {
            errorMessage = "Workflow graph global node-visit limit must be greater than zero.";
            return false;
        }

        if (nodeIds.Count != nodeIds.Distinct().Count())
        {
            errorMessage = "Workflow graph contains duplicate node identifiers.";
            return false;
        }

        if (nodes.Count(node => node.Type == WorkflowNodeType.Start) != 1)
        {
            errorMessage = "Workflow graph must contain exactly one Start node.";
            return false;
        }

        if (nodes.All(node => node.Type != WorkflowNodeType.End))
        {
            errorMessage = "Workflow graph must contain at least one End node.";
            return false;
        }

        var knownNodes = nodeIds.ToHashSet();
        if (flow.GraphEdges.Any(edge => !knownNodes.Contains(edge.FromNodeId) || !knownNodes.Contains(edge.ToNodeId)))
        {
            errorMessage = "Workflow graph contains an edge that references an unknown node.";
            return false;
        }

        foreach (var node in nodes)
        {
            if (node.MaxVisits.HasValue && node.MaxVisits.Value <= 0)
            {
                errorMessage = $"Node '{node.Name}' visit limit must be greater than zero.";
                return false;
            }
            var outgoing = flow.GraphEdges.Where(edge => edge.FromNodeId == node.NodeId).ToList();
            if (node.Type == WorkflowNodeType.End && outgoing.Count > 0)
            {
                errorMessage = $"End node '{node.Name}' cannot have outgoing edges.";
                return false;
            }

            if (node.Type == WorkflowNodeType.Module && node.Module is null)
            {
                errorMessage = $"Module node '{node.Name}' has no module definition.";
                return false;
            }

            if (node.Type == WorkflowNodeType.SubFlow && node.SubFlow is null)
            {
                errorMessage = $"Sub-flow node '{node.Name}' has no referenced flow.";
                return false;
            }

            if (node.Type == WorkflowNodeType.Switch)
            {
                if (string.IsNullOrWhiteSpace(node.RouteKeyVariable))
                {
                    errorMessage = $"Switch node '{node.Name}' has no route-key variable.";
                    return false;
                }

                if (outgoing.Count(edge => edge.IsDefault) > 1)
                {
                    errorMessage = $"Switch node '{node.Name}' has more than one default edge.";
                    return false;
                }

                var duplicateRoute = outgoing
                    .Where(edge => !edge.IsDefault)
                    .GroupBy(edge => edge.RouteKey, StringComparer.Ordinal)
                    .Any(group => group.Count() > 1);
                if (duplicateRoute)
                {
                    errorMessage = $"Switch node '{node.Name}' has duplicate route keys.";
                    return false;
                }
            }

            if (node.Type == WorkflowNodeType.Fork && outgoing.Count < 2)
            {
                errorMessage = $"Fork node '{node.Name}' requires at least two outgoing edges.";
                return false;
            }

            if (node.Type == WorkflowNodeType.Join)
            {
                var incomingCount = flow.GraphEdges.Count(edge => edge.ToNodeId == node.NodeId);
                if (incomingCount < 2)
                {
                    errorMessage = $"Join node '{node.Name}' requires at least two incoming edges.";
                    return false;
                }
            }

            if (node.Type != WorkflowNodeType.End && outgoing.Count == 0)
            {
                errorMessage = $"Node '{node.Name}' has no outgoing edge.";
                return false;
            }
        }

        var adjacency = flow.GraphEdges
            .GroupBy(edge => edge.FromNodeId)
            .ToDictionary(group => group.Key, group => group.Select(edge => edge.ToNodeId).ToList());
        var reverseAdjacency = flow.GraphEdges
            .GroupBy(edge => edge.ToNodeId)
            .ToDictionary(group => group.Key, group => group.Select(edge => edge.FromNodeId).ToList());
        var startId = nodes.Single(node => node.Type == WorkflowNodeType.Start).NodeId;
        var reachable = Traverse(startId, adjacency);
        if (reachable.Count != knownNodes.Count)
        {
            errorMessage = "Workflow graph contains a node that is unreachable from Start.";
            return false;
        }

        var endIds = nodes.Where(node => node.Type == WorkflowNodeType.End).Select(node => node.NodeId);
        var canReachEnd = Traverse(endIds, reverseAdjacency);
        if (reachable.Any(nodeId => !canReachEnd.Contains(nodeId)))
        {
            errorMessage = "Workflow graph contains an execution path that cannot reach an End node.";
            return false;
        }

        foreach (var component in FindStronglyConnectedComponents(knownNodes, adjacency, reverseAdjacency))
        {
            var isCycle = component.Count > 1
                || flow.GraphEdges.Any(edge => edge.FromNodeId == edge.ToNodeId && component.Contains(edge.FromNodeId));
            if (!isCycle) continue;
            if (flow.Timeout is null
                && !nodes.Where(node => component.Contains(node.NodeId)).Any(node => node.MaxVisits.HasValue))
            {
                errorMessage = "Every graph cycle must contain a node visit limit or the workflow must define a total timeout.";
                return false;
            }
        }

        foreach (var fork in nodes.Where(node => node.Type == WorkflowNodeType.Fork))
        {
            var branchJoins = flow.GraphEdges
                .Where(edge => edge.FromNodeId == fork.NodeId)
                .Select(edge => FindFirstJoinEndpoints(edge.ToNodeId, nodes, adjacency))
                .ToList();
            if (branchJoins.Any(joins => joins.Count != 1)
                || branchJoins.SelectMany(joins => joins).Distinct().Count() != 1)
            {
                errorMessage = $"Fork node '{fork.Name}' branches must each reach the same single Join node before End.";
                return false;
            }
        }

        errorMessage = null;
        return true;
    }

    private static HashSet<Guid> Traverse(Guid start, IReadOnlyDictionary<Guid, List<Guid>> adjacency) =>
        Traverse(new[] { start }, adjacency);

    private static HashSet<Guid> Traverse(IEnumerable<Guid> starts, IReadOnlyDictionary<Guid, List<Guid>> adjacency)
    {
        var visited = new HashSet<Guid>();
        var pending = new Queue<Guid>(starts);
        while (pending.TryDequeue(out var nodeId))
        {
            if (!visited.Add(nodeId) || !adjacency.TryGetValue(nodeId, out var next))
                continue;
            foreach (var nextNodeId in next)
                pending.Enqueue(nextNodeId);
        }
        return visited;
    }

    private static IEnumerable<HashSet<Guid>> FindStronglyConnectedComponents(
        IReadOnlySet<Guid> nodeIds,
        IReadOnlyDictionary<Guid, List<Guid>> adjacency,
        IReadOnlyDictionary<Guid, List<Guid>> reverseAdjacency)
    {
        var remaining = new HashSet<Guid>(nodeIds);
        while (remaining.Count > 0)
        {
            var seed = remaining.First();
            var forward = Traverse(seed, adjacency);
            var backward = Traverse(seed, reverseAdjacency);
            forward.IntersectWith(backward);
            remaining.ExceptWith(forward);
            yield return forward;
        }
    }

    private static HashSet<Guid> FindFirstJoinEndpoints(
        Guid branchStart,
        IReadOnlyList<WorkflowNode> nodes,
        IReadOnlyDictionary<Guid, List<Guid>> adjacency)
    {
        var nodesById = nodes.ToDictionary(node => node.NodeId);
        var visited = new HashSet<Guid>();
        var pending = new Queue<Guid>();
        var joins = new HashSet<Guid>();
        pending.Enqueue(branchStart);

        while (pending.TryDequeue(out var nodeId))
        {
            if (!visited.Add(nodeId))
                continue;
            var node = nodesById[nodeId];
            if (node.Type == WorkflowNodeType.Join)
            {
                joins.Add(nodeId);
                continue;
            }
            if (node.Type == WorkflowNodeType.End)
                continue;
            if (adjacency.TryGetValue(nodeId, out var next))
            {
                foreach (var nextNodeId in next)
                    pending.Enqueue(nextNodeId);
            }
        }

        return joins;
    }
}
