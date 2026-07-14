using System.Text.Json;
using System.Text.Json.Nodes;
using HardwareModularWorkflow.Hardware.Enums;

namespace HardwareModularWorkflow.Hardware.Models;

/// <summary>
/// 自定义硬件模型：通过 JSON Schema 动态定义属性和命令模板
/// 第一阶段实现：运行时解析 JSON Schema 生成动态命令集
/// </summary>
public sealed class CustomHardware : HardwareBase
{
    public override HardwareType Type => HardwareType.Custom;

    /// <summary>
    /// JSON Schema 定义：描述该自定义硬件的属性结构、数据类型、命令模板
    /// 运行时由 Hardware 层解析，生成 UI 配置界面和命令集合
    /// </summary>
    public JsonDocument? CustomSchema
    {
        get
        {
            var raw = GetParameter<string>(nameof(CustomSchema));
            return raw is null ? null : JsonDocument.Parse(raw);
        }
        set => SetParameter(nameof(CustomSchema), value?.RootElement.GetRawText() ?? string.Empty);
    }

    /// <summary>
    /// 自定义属性集合（JSON 对象形式）
    /// 属性名和类型由 CustomSchema 定义，运行时动态存取
    /// </summary>
    public JsonObject? CustomProperties
    {
        get
        {
            var raw = GetParameter<string>(nameof(CustomProperties));
            return raw is null ? null : JsonNode.Parse(raw)?.AsObject();
        }
        set => SetParameter(nameof(CustomProperties), value?.ToJsonString() ?? "{}");
    }

    /// <summary>
    /// 自定义命令模板列表（JSON 数组）
    /// 每个模板包含：命令名称、参数列表、参数类型、默认值
    /// </summary>
    public JsonArray? CommandTemplates
    {
        get
        {
            var raw = GetParameter<string>(nameof(CommandTemplates));
            return raw is null ? null : JsonNode.Parse(raw)?.AsArray();
        }
        set => SetParameter(nameof(CommandTemplates), value?.ToJsonString() ?? "[]");
    }

    /// <summary>
    /// 从 Schema 中提取属性定义列表（供 UI 动态生成配置表单）
    /// </summary>
    public IEnumerable<CustomPropertyDefinition> GetPropertyDefinitions()
    {
        if (CustomSchema is null) yield break;

        var root = CustomSchema.RootElement;
        if (!root.TryGetProperty("properties", out var properties)) yield break;

        foreach (var property in properties.EnumerateObject())
        {
            yield return new CustomPropertyDefinition
            {
                Name = property.Name,
                Type = property.Value.GetProperty("type").GetString() ?? "string",
                Description = property.Value.TryGetProperty("description", out var desc)
                    ? desc.GetString() : null,
                DefaultValue = property.Value.TryGetProperty("default", out var def)
                    ? def.GetRawText() : null
            };
        }
    }
}

/// <summary>
/// 自定义属性定义（供 UI 动态生成）
/// </summary>
public sealed class CustomPropertyDefinition
{
    public required string Name { get; init; }
    public required string Type { get; init; }
    public string? Description { get; init; }
    public string? DefaultValue { get; init; }
}
