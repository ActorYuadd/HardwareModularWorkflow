using System.Text.Json;
using HardwareModularWorkflow.Hardware.Abstractions;
using HardwareModularWorkflow.Hardware.Enums;
using HardwareModularWorkflow.Hardware.Models;
using HardwareModularWorkflow.Workflow.Abstractions;
using HardwareModularWorkflow.Workflow.Models;
using HardwareModularWorkflow.Db.Services;
using HardwareModularWorkflow.Db.Entities;

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
            ExecutionMode = ParseExecutionMode(entity.ExecutionMode),
            CoreEvent = entity.CoreEvent,
            NotifyEvent = entity.NotifyEvent,
            Timeout = entity.TimeoutMs.HasValue ? TimeSpan.FromMilliseconds(entity.TimeoutMs.Value) : null,
            ContinueOnFailure = entity.ContinueOnFailure
        };

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
                Condition = refEntity.Condition
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

            module.Steps.Add(new HardwareStep
            {
                StepId = Guid.NewGuid(),
                Name = stepEntity.Name,
                Order = stepEntity.OrderIndex,
                Hardware = hardware,
                Command = command,
                ExecutionMode = ParseExecutionMode(stepEntity.ExecutionMode),
                ContinueOnFailure = stepEntity.ContinueOnFailure
            });
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
