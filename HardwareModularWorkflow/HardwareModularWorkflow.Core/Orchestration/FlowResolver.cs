using System.Text.Json;
using HardwareModularWorkflow.Hardware.Abstractions;
using HardwareModularWorkflow.Hardware.Enums;
using HardwareModularWorkflow.Hardware.Models;
using HardwareModularWorkflow.Workflow.Abstractions;
using HardwareModularWorkflow.Workflow.Models;
using HardwareModularWorkflow.Db.Services;
using HardwareModularWorkflow.Db.Entities;
using HardwareModularWorkflow.Workflow.Resources;

namespace HardwareModularWorkflow.Core.Orchestration;

/// <summary>
/// Core 层流解析器实现：从数据库加载 FlowEntity 并转换为运行时 Flow 模型
/// </summary>
public sealed class FlowResolver : IFlowResolver
{
    private readonly FlowService _flowService;
    private readonly HardwareInstanceService _hardwareInstanceService;

    public FlowResolver(FlowService flowService, HardwareInstanceService hardwareInstanceService)
    {
        _flowService = flowService ?? throw new ArgumentNullException(nameof(flowService));
        _hardwareInstanceService = hardwareInstanceService ?? throw new ArgumentNullException(nameof(hardwareInstanceService));
    }

    public async Task<Flow?> ResolveAsync(long flowId, CancellationToken ct = default)
    {
        var entity = await _flowService.GetByIdAsync(flowId, ct);
        if (entity is null)
            return null;

        var flow = new Flow
        {
            FlowId = entity.Id,
            Name = entity.Name,
            Alias = entity.Alias ?? string.Empty,
            Note = entity.Note ?? string.Empty,
            Tags = ParseTags(entity.Tags),
            DefinitionVersion = entity.DefinitionVersion,
            RecoveryPolicy = ParseWorkflowRecoveryPolicy(entity.RecoveryPolicy),
            ExecutionMode = ParseExecutionMode(entity.ExecutionMode),
            CoreEvent = entity.CoreEvent,
            NotifyEvent = entity.NotifyEvent,
            Timeout = entity.TimeoutMs.HasValue ? TimeSpan.FromMilliseconds(entity.TimeoutMs.Value) : null,
            ResourceWaitTimeout = entity.ResourceWaitTimeoutMs > 0
                ? TimeSpan.FromMilliseconds(entity.ResourceWaitTimeoutMs)
                : null,
            MaxGraphNodeVisits = entity.MaxGraphNodeVisits,
            ContinueOnFailure = entity.ContinueOnFailure
        };

        foreach (var reservation in entity.ResourceReservations)
        {
            flow.ResourceRequirements.Add(new ResourceRequirement(
                $"hardware:{reservation.HardwareInstanceId}",
                ParseResourceAccessMode(reservation.AccessMode)));
            flow.ResourcePriority = Math.Max(flow.ResourcePriority, reservation.Priority);
        }

        foreach (var parameter in entity.Parameters)
        {
            if (!Enum.TryParse<FlowParameterType>(parameter.ParameterType, true, out var type))
                type = FlowParameterType.String;
            var definition = new FlowParameterDefinition
            {
                Name = parameter.Name,
                Type = type,
                IsRequired = parameter.IsRequired,
                DefaultValue = DeserializeDefaultValue(parameter.DefaultValueJson, type),
                Description = parameter.Description
            };
            (string.Equals(parameter.Direction, "Output", StringComparison.OrdinalIgnoreCase)
                ? flow.OutputParameters
                : flow.InputParameters).Add(definition);
        }

        // 转换模块
        foreach (var relation in entity.ModuleRelations.OrderBy(r => r.OrderIndex))
        {
            var module = await ResolveModuleAsync(relation.Module, ct);
            if (module is not null)
                flow.Modules.Add(module);
        }

        // 转换子流引用（不递归加载子流内容，执行时按需解析）
        foreach (var refEntity in entity.SubFlowReferences.OrderBy(r => r.OrderIndex))
        {
            flow.SubFlows.Add(new FlowReference
            {
                ReferencedFlowId = refEntity.SubFlowId,
                Name = refEntity.SubFlow?.Name ?? string.Empty,
                Order = refEntity.OrderIndex,
                ExecutionMode = ParseExecutionMode(refEntity.ExecutionMode),
                Condition = refEntity.Condition,
                InvocationPolicy = ParseInvocationPolicy(refEntity.InvocationPolicy),
                InputBindings = ToBindingMap(refEntity.ParameterBindings, "Input"),
                OutputBindings = ToBindingMap(refEntity.ParameterBindings, "Output")
            });
        }

        var graphNodeIds = new Dictionary<long, Guid>();
        foreach (var graphNodeEntity in entity.GraphNodes.OrderBy(node => node.Id))
        {
            var nodeType = ParseWorkflowNodeType(graphNodeEntity.NodeType);
            Module? nodeModule = null;
            if (graphNodeEntity.Module is not null)
                nodeModule = await ResolveModuleAsync(graphNodeEntity.Module, ct);

            FlowReference? nodeSubFlow = null;
            if (graphNodeEntity.SubFlowId.HasValue)
            {
                nodeSubFlow = new FlowReference
                {
                    ReferencedFlowId = graphNodeEntity.SubFlowId.Value,
                    Name = graphNodeEntity.SubFlow?.Name ?? string.Empty,
                    InvocationPolicy = ParseInvocationPolicy(graphNodeEntity.SubFlowInvocationPolicy),
                    InputBindings = ToBindingMap(graphNodeEntity.ParameterBindings, "Input"),
                    OutputBindings = ToBindingMap(graphNodeEntity.ParameterBindings, "Output")
                };
            }

            flow.GraphNodes.Add(new WorkflowNode
            {
                NodeId = graphNodeEntity.NodeId,
                Type = nodeType,
                Name = graphNodeEntity.Name,
                Module = nodeModule,
                SubFlow = nodeSubFlow,
                JoinMode = ParseJoinMode(graphNodeEntity.JoinMode),
                MaxVisits = graphNodeEntity.MaxVisits,
                RouteKeyVariable = graphNodeEntity.RouteKeyVariable
            });
            graphNodeIds.Add(graphNodeEntity.Id, graphNodeEntity.NodeId);
        }

        foreach (var graphEdgeEntity in entity.GraphEdges.OrderBy(edge => edge.Id))
        {
            if (!graphNodeIds.TryGetValue(graphEdgeEntity.FromNodeId, out var fromNodeId)
                || !graphNodeIds.TryGetValue(graphEdgeEntity.ToNodeId, out var toNodeId))
                continue;

            flow.GraphEdges.Add(new WorkflowEdge
            {
                FromNodeId = fromNodeId,
                ToNodeId = toNodeId,
                RouteKey = graphEdgeEntity.RouteKey,
                IsDefault = graphEdgeEntity.IsDefault,
                Priority = graphEdgeEntity.Priority
            });
        }

        return flow;
    }

    private async Task<Module?> ResolveModuleAsync(ModuleEntity? entity, CancellationToken ct)
    {
        if (entity is null) return null;

        var module = new Module
        {
            ModuleId = Guid.NewGuid(),
            Name = entity.Name,
            Alias = entity.Alias ?? string.Empty,
            Note = entity.Note ?? string.Empty,
            Tags = ParseTags(entity.Tags),
            DefaultExecutionMode = ParseExecutionMode(entity.DefaultExecutionMode),
            CoreEvent = entity.CoreEvent,
            NotifyEvent = entity.NotifyEvent,
            Timeout = entity.TimeoutMs.HasValue ? TimeSpan.FromMilliseconds(entity.TimeoutMs.Value) : null,
            ResourceWaitTimeout = entity.ResourceWaitTimeoutMs > 0
                ? TimeSpan.FromMilliseconds(entity.ResourceWaitTimeoutMs)
                : null,
            CompensationTimeout = entity.CompensationTimeoutMs > 0
                ? TimeSpan.FromMilliseconds(entity.CompensationTimeoutMs)
                : TimeSpan.FromSeconds(10),
            RecoveryPolicy = ParseModuleRecoveryPolicy(entity.RecoveryPolicy),
            ContinueOnFailure = entity.ContinueOnFailure
        };

        // 转换步骤
        foreach (var stepEntity in entity.Steps.OrderBy(s => s.OrderIndex))
        {
            var hardwareInstance = await _hardwareInstanceService.GetByIdAsync(stepEntity.HardwareInstanceId, ct);
            if (hardwareInstance is null)
                continue;

            var hardware = HardwareDriverFactory.CreateHardwareModel(hardwareInstance);

            var parameters = string.IsNullOrEmpty(stepEntity.CommandParametersJson)
                ? new Dictionary<string, object>()
                : JsonSerializer.Deserialize<Dictionary<string, object>>(stepEntity.CommandParametersJson)
                  ?? new Dictionary<string, object>();

            var command = new HardwareCommand
            {
                CommandName = stepEntity.CommandName,
                Parameters = parameters,
                IsAsync = stepEntity.IsAsync,
                Timeout = stepEntity.TimeoutMs.HasValue ? TimeSpan.FromMilliseconds(stepEntity.TimeoutMs.Value) : null
            };

            var step = new HardwareStep
            {
                StepId = Guid.NewGuid(),
                Name = stepEntity.Name,
                Order = stepEntity.OrderIndex,
                Hardware = hardware,
                Command = command,
                ResourceAccessMode = ParseResourceAccessMode(stepEntity.ResourceAccessMode),
                ResultVariable = stepEntity.ResultVariable,
                ExecutionMode = ParseExecutionMode(stepEntity.ExecutionMode),
                Timeout = stepEntity.TimeoutMs.HasValue ? TimeSpan.FromMilliseconds(stepEntity.TimeoutMs.Value) : null,
                ContinueOnFailure = stepEntity.ContinueOnFailure
            };
            (stepEntity.IsCompensation ? module.CompensationSteps : module.Steps).Add(step);
        }

        foreach (var reservation in entity.ResourceReservations)
        {
            module.ResourceRequirements.Add(new ResourceRequirement(
                $"hardware:{reservation.HardwareInstanceId}",
                ParseResourceAccessMode(reservation.AccessMode)));
        }

        return module;
    }

    private static ExecutionMode ParseExecutionMode(string? value)
    {
        if (string.IsNullOrEmpty(value))
            return ExecutionMode.Sequential;

        return Enum.TryParse<ExecutionMode>(value, ignoreCase: true, out var mode)
            ? mode
            : ExecutionMode.Sequential;
    }

    private static ResourceAccessMode ParseResourceAccessMode(string? value) =>
        Enum.TryParse<ResourceAccessMode>(value, ignoreCase: true, out var mode)
            ? mode
            : ResourceAccessMode.Exclusive;

    private static object? DeserializeDefaultValue(string? json, FlowParameterType type)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            return type switch
            {
                FlowParameterType.String => JsonSerializer.Deserialize<string>(json),
                FlowParameterType.Boolean => JsonSerializer.Deserialize<bool>(json),
                FlowParameterType.Integer => JsonSerializer.Deserialize<long>(json),
                FlowParameterType.Decimal => JsonSerializer.Deserialize<decimal>(json),
                FlowParameterType.DateTime => JsonSerializer.Deserialize<DateTime>(json),
                FlowParameterType.Guid => JsonSerializer.Deserialize<Guid>(json),
                _ => JsonSerializer.Deserialize<JsonElement>(json)
            };
        }
        catch (JsonException) { return null; }
    }

    private static WorkflowNodeType ParseWorkflowNodeType(string? value) =>
        Enum.TryParse<WorkflowNodeType>(value, ignoreCase: true, out var type)
            ? type
            : WorkflowNodeType.Module;

    private static WorkflowJoinMode ParseJoinMode(string? value) =>
        Enum.TryParse<WorkflowJoinMode>(value, ignoreCase: true, out var mode)
            ? mode
            : WorkflowJoinMode.WaitAll;

    private static FlowInvocationPolicy ParseInvocationPolicy(string? value) =>
        Enum.TryParse<FlowInvocationPolicy>(value, ignoreCase: true, out var policy)
            ? policy
            : FlowInvocationPolicy.Reentrant;

    private static WorkflowRecoveryPolicy ParseWorkflowRecoveryPolicy(string? value) =>
        Enum.TryParse<WorkflowRecoveryPolicy>(value, true, out var policy)
            ? policy
            : WorkflowRecoveryPolicy.NotRecoverable;

    private static ModuleRecoveryPolicy ParseModuleRecoveryPolicy(string? value) =>
        Enum.TryParse<ModuleRecoveryPolicy>(value, true, out var policy)
            ? policy
            : ModuleRecoveryPolicy.NotRecoverable;

    private static Dictionary<string, string> ToBindingMap(
        IEnumerable<FlowSubFlowParameterBinding> bindings,
        string direction) =>
        bindings
            .Where(binding => string.Equals(binding.Direction, direction, StringComparison.OrdinalIgnoreCase))
            .ToDictionary(binding => binding.ChildParameterName, binding => binding.ParentValueName, StringComparer.Ordinal);

    private static List<string> ParseTags(string? tags)
    {
        if (string.IsNullOrEmpty(tags))
            return new List<string>();

        return tags.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();
    }
}

/// <summary>
/// 硬件命令的简单实现（供 FlowResolver 使用）
/// </summary>
public sealed class HardwareCommand : IHardwareCommand
{
    public string CommandName { get; set; } = string.Empty;
    public Dictionary<string, object> Parameters { get; set; } = new();
    public bool IsAsync { get; set; } = true;
    public TimeSpan? Timeout { get; set; }
    public Type? ExpectedReturnType { get; set; }
}
