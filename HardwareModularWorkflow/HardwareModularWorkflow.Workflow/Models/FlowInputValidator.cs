namespace HardwareModularWorkflow.Workflow.Models;

/// <summary>
/// 工作流启动输入验证器。输入在进入调度队列前验证，避免无效任务占用并发槽位或硬件资源。
/// </summary>
public static class FlowInputValidator
{
    public static bool TryCreateInputSnapshot(
        Flow flow,
        IReadOnlyDictionary<string, object?>? suppliedInputs,
        out IReadOnlyDictionary<string, object?> inputSnapshot,
        out string? errorMessage)
    {
        ArgumentNullException.ThrowIfNull(flow);

        var definitions = flow.InputParameters;
        var duplicateName = definitions
            .GroupBy(parameter => parameter.Name, StringComparer.Ordinal)
            .FirstOrDefault(group => group.Count() > 1);

        if (duplicateName is not null)
        {
            inputSnapshot = Empty;
            errorMessage = $"Flow '{flow.Name}' defines input parameter '{duplicateName.Key}' more than once.";
            return false;
        }

        var supplied = suppliedInputs ?? Empty;
        var declaredNames = definitions
            .Select(parameter => parameter.Name)
            .ToHashSet(StringComparer.Ordinal);

        var unknownName = supplied.Keys.FirstOrDefault(name => !declaredNames.Contains(name));
        if (unknownName is not null)
        {
            inputSnapshot = Empty;
            errorMessage = $"Flow '{flow.Name}' does not declare input parameter '{unknownName}'.";
            return false;
        }

        var snapshot = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var definition in definitions)
        {
            var hasValue = supplied.TryGetValue(definition.Name, out var value);
            if (!hasValue)
                value = definition.DefaultValue;

            if (value is null)
            {
                if (definition.IsRequired)
                {
                    inputSnapshot = Empty;
                    errorMessage = $"Flow '{flow.Name}' requires input parameter '{definition.Name}'.";
                    return false;
                }

                snapshot[definition.Name] = null;
                continue;
            }

            if (!IsExpectedType(definition.Type, value))
            {
                inputSnapshot = Empty;
                errorMessage = $"Flow '{flow.Name}' input parameter '{definition.Name}' expects {definition.Type}, but received {value.GetType().Name}.";
                return false;
            }

            snapshot[definition.Name] = value;
        }

        inputSnapshot = snapshot;
        errorMessage = null;
        return true;
    }

    private static bool IsExpectedType(FlowParameterType type, object value) => type switch
    {
        FlowParameterType.String => value is string,
        FlowParameterType.Boolean => value is bool,
        FlowParameterType.Integer => value is sbyte or byte or short or ushort or int or uint or long or ulong,
        FlowParameterType.Decimal => value is decimal or double or float or sbyte or byte or short or ushort or int or uint or long or ulong,
        FlowParameterType.DateTime => value is DateTime,
        FlowParameterType.Guid => value is Guid,
        FlowParameterType.Json => true,
        _ => false
    };

    private static readonly IReadOnlyDictionary<string, object?> Empty =
        new Dictionary<string, object?>(StringComparer.Ordinal);
}
