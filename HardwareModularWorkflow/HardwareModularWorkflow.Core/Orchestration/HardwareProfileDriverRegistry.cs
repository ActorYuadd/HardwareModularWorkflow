using HardwareModularWorkflow.Hardware.Abstractions;
using HardwareModularWorkflow.Db.Entities;

namespace HardwareModularWorkflow.Core.Orchestration;

/// <summary>
/// Resolves the runtime adapter for a control profile. Profile data decides which
/// driver key is needed; this registry decides whether that driver is installed.
/// </summary>
public sealed class HardwareProfileDriverRegistry
{
    private readonly HashSet<string> _supportedDriverKeys = new(
        StringComparer.OrdinalIgnoreCase)
    {
        "Motor.Generic",
        "Motor.PlcAxis",
        "Motor.CanMotor",
        "Camera.Generic",
        "Camera.GigEVision",
        "Temperature.Generic",
        "Temperature.PlcModbus",
        "Cooling.Generic",
        "Cooling.PlcModbus",
        "Infrared.Generic",
        "Infrared.PlcDigitalIo",
        "Custom.Generic"
    };

    public IHardwareDriver CreateDriver(
        IHardware hardware,
        HardwareControlProfile profile,
        IController? controller)
    {
        if (!_supportedDriverKeys.Contains(profile.DriverKey))
        {
            throw new InvalidOperationException(
                $"Hardware driver '{profile.DriverKey}' is not installed.");
        }

        if (!string.IsNullOrWhiteSpace(profile.RequiredControllerType))
        {
            if (controller is null)
            {
                throw new InvalidOperationException(
                    $"Control profile '{profile.Name}' requires a {profile.RequiredControllerType} controller.");
            }

            if (!string.Equals(
                    controller.ControllerType,
                    profile.RequiredControllerType,
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"Control profile '{profile.Name}' requires a {profile.RequiredControllerType} controller, "
                    + $"but '{controller.ControllerType}' is bound.");
            }
        }

        if (controller is null)
        {
            throw new InvalidOperationException(
                $"Control profile '{profile.Name}' has no connected controller. "
                + "Direct SDK drivers will be added through a profile driver plugin.");
        }

        return new ProfileHardwareDriver(hardware, controller, profile);
    }
}

/// <summary>
/// Profile-aware bridge driver. Specialized profile drivers can replace this implementation
/// through the registry without changing workflow or hardware-instance code.
/// </summary>
public sealed class ProfileHardwareDriver : IHardwareDriver, IAsyncDisposable
{
    private readonly IHardware _hardware;
    private readonly IController _controller;
    private readonly HardwareControlProfile _profile;

    public ProfileHardwareDriver(
        IHardware hardware,
        IController controller,
        HardwareControlProfile profile)
    {
        _hardware = hardware ?? throw new ArgumentNullException(nameof(hardware));
        _controller = controller ?? throw new ArgumentNullException(nameof(controller));
        _profile = profile ?? throw new ArgumentNullException(nameof(profile));
    }

    public IHardware Hardware => _hardware;
    public string ProfileDriverKey => _profile.DriverKey;

    public async Task<HardwareModularWorkflow.Hardware.Results.CommandResult> ExecuteAsync(
        IHardwareCommand command,
        CancellationToken ct = default)
    {
        var start = DateTime.UtcNow;
        try
        {
            HardwareCommandCatalog.Validate(
                _profile,
                command.CommandName,
                command.Parameters);
            var response = await _controller.SendCommandAsync(
                HardwareCommandSerializer.Serialize(command),
                ct);
            return HardwareModularWorkflow.Hardware.Results.CommandResult.Success(
                DateTime.UtcNow - start,
                response);
        }
        catch (OperationCanceledException)
        {
            return HardwareModularWorkflow.Hardware.Results.CommandResult.Cancelled(
                DateTime.UtcNow - start);
        }
        catch (Exception ex)
        {
            return HardwareModularWorkflow.Hardware.Results.CommandResult.Failed(
                DateTime.UtcNow - start,
                "DRIVER_ERROR",
                ex.Message);
        }
    }

    public async Task<HardwareModularWorkflow.Hardware.Enums.HardwareState> GetStateAsync(
        CancellationToken ct = default)
    {
        try
        {
            var stateValue = await _controller.ReadRegisterAsync<int>(_hardware.Address, ct);
            return Enum.IsDefined(
                    typeof(HardwareModularWorkflow.Hardware.Enums.HardwareState),
                    stateValue)
                ? (HardwareModularWorkflow.Hardware.Enums.HardwareState)stateValue
                : HardwareModularWorkflow.Hardware.Enums.HardwareState.Idle;
        }
        catch
        {
            return HardwareModularWorkflow.Hardware.Enums.HardwareState.Offline;
        }
    }

    public Task<bool> TryStopAsync(CancellationToken ct = default) =>
        _controller is IStoppable stoppable
            ? stoppable.TryStopAsync(ct)
            : Task.FromResult(false);

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

internal static class HardwareCommandSerializer
{
    public static byte[] Serialize(IHardwareCommand command)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);

        WriteString(writer, command.CommandName);
        writer.Write((byte)(command.Parameters?.Count ?? 0));

        if (command.Parameters is not null)
        {
            foreach (var (key, value) in command.Parameters)
            {
                WriteString(writer, key);
                var valueBytes = System.Text.Encoding.UTF8.GetBytes(
                    System.Text.Json.JsonSerializer.Serialize(value));
                writer.Write(valueBytes.Length);
                writer.Write(valueBytes);
            }
        }

        writer.Flush();
        return stream.ToArray();
    }

    private static void WriteString(BinaryWriter writer, string value)
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes(value);
        if (bytes.Length > byte.MaxValue)
        {
            throw new InvalidOperationException("Hardware command metadata exceeds 255 bytes.");
        }

        writer.Write((byte)bytes.Length);
        writer.Write(bytes);
    }
}
