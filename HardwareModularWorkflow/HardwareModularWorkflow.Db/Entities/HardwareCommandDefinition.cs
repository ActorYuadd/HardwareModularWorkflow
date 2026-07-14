using System.Text.Json;

namespace HardwareModularWorkflow.Db.Entities;

/// <summary>
/// Describes a semantic command offered by a hardware control profile.
/// </summary>
public sealed class HardwareCommandDefinition
{
    public required string Name { get; init; }
    public string? DisplayName { get; init; }
    public bool IsAsync { get; init; } = true;
    public IReadOnlyList<HardwareCommandParameterDefinition> Parameters { get; init; } =
        Array.Empty<HardwareCommandParameterDefinition>();
}

/// <summary>
/// Describes a required or optional command argument.
/// </summary>
public sealed class HardwareCommandParameterDefinition
{
    public required string Name { get; init; }
    public string Type { get; init; } = "string";
    public bool Required { get; init; }
}

/// <summary>
/// Parses and validates control-profile command definitions.
/// </summary>
public static class HardwareCommandCatalog
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public static IReadOnlyList<HardwareCommandDefinition> Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return Array.Empty<HardwareCommandDefinition>();
        }

        try
        {
            var commands = JsonSerializer.Deserialize<List<HardwareCommandDefinition>>(
                       json,
                       SerializerOptions);
            return commands?
                       .Where(command => !string.IsNullOrWhiteSpace(command.Name))
                       .ToList()
                   ?? new List<HardwareCommandDefinition>();
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException(
                "The control profile contains invalid command definitions.",
                ex);
        }
    }

    public static void Validate(
        HardwareControlProfile profile,
        string commandName,
        IReadOnlyDictionary<string, object>? parameters)
    {
        var command = Parse(profile.CommandDefinitionsJson)
            .FirstOrDefault(item => string.Equals(
                item.Name,
                commandName,
                StringComparison.OrdinalIgnoreCase));
        if (command is null)
        {
            throw new InvalidOperationException(
                $"Command '{commandName}' is not supported by control profile '{profile.Name}'.");
        }

        foreach (var parameter in command.Parameters.Where(item => item.Required))
        {
            if (parameters is null || !parameters.ContainsKey(parameter.Name))
            {
                throw new InvalidOperationException(
                    $"Command '{command.Name}' requires parameter '{parameter.Name}'.");
            }
        }
    }
}
