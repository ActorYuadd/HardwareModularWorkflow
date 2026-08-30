using System.Collections.Concurrent;
using HardwareModularWorkflow.Hardware.Abstractions;
using HardwareModularWorkflow.Hardware.Enums;
using HardwareModularWorkflow.Hardware.Results;

namespace HardwareModularWorkflow.Hardware.Simulation;

/// <summary>可配置、可停止且保留命令轨迹的硬件模拟驱动，用于离线调试和自动化安全测试。</summary>
public sealed class SimulationHardwareDriver : IHardwareDriver, IAsyncDisposable
{
    private readonly ConcurrentDictionary<string, SimulationCommandBehavior> _behaviors =
        new(StringComparer.Ordinal);
    private readonly ConcurrentQueue<SimulationCommandRecord> _records = new();
    private readonly object _activeSync = new();
    private readonly HashSet<CancellationTokenSource> _activeCommands = new();
    private bool _disposed;

    public SimulationHardwareDriver(IHardware hardware)
    {
        Hardware = hardware ?? throw new ArgumentNullException(nameof(hardware));
    }

    public IHardware Hardware { get; }

    public HardwareState State { get; set; } = HardwareState.Idle;

    public IReadOnlyList<SimulationCommandRecord> CommandRecords => _records.ToArray();

    public void Configure(string commandName, SimulationCommandBehavior behavior)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(commandName);
        _behaviors[commandName] = behavior;
    }

    public async Task<CommandResult> ExecuteAsync(IHardwareCommand command, CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(command);

        var startedAt = DateTime.UtcNow;
        var behavior = _behaviors.GetValueOrDefault(command.CommandName) ?? new SimulationCommandBehavior();
        using var commandCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        lock (_activeSync)
            _activeCommands.Add(commandCts);

        State = HardwareState.Running;
        Hardware.State = HardwareState.Running;
        CommandResult result;
        try
        {
            if (behavior.Delay > TimeSpan.Zero)
                await Task.Delay(behavior.Delay, commandCts.Token).ConfigureAwait(false);

            result = behavior.ErrorCode is null
                ? CommandResult.Success(DateTime.UtcNow - startedAt, behavior.ReturnData)
                : CommandResult.Failed(
                    DateTime.UtcNow - startedAt,
                    behavior.ErrorCode,
                    behavior.ErrorMessage ?? "Simulated command failure.");
        }
        catch (OperationCanceledException)
        {
            result = CommandResult.Cancelled(DateTime.UtcNow - startedAt);
        }
        finally
        {
            lock (_activeSync)
                _activeCommands.Remove(commandCts);
        }

        State = result.Status is CommandStatus.Success or CommandStatus.Cancelled
            ? HardwareState.Idle
            : HardwareState.Error;
        Hardware.State = State;
        _records.Enqueue(new SimulationCommandRecord(
            command.CommandName,
            startedAt,
            DateTime.UtcNow,
            result.Status,
            new Dictionary<string, object>(command.Parameters ?? new Dictionary<string, object>())));
        return result;
    }

    public Task<HardwareState> GetStateAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        return Task.FromResult(State);
    }

    public Task<bool> TryStopAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        CancellationTokenSource[] active;
        lock (_activeSync)
            active = _activeCommands.ToArray();
        foreach (var command in active)
            command.Cancel();
        State = HardwareState.Idle;
        Hardware.State = HardwareState.Idle;
        return Task.FromResult(true);
    }

    public ValueTask DisposeAsync()
    {
        if (_disposed) return ValueTask.CompletedTask;
        _disposed = true;
        lock (_activeSync)
        {
            foreach (var command in _activeCommands)
                command.Cancel();
            _activeCommands.Clear();
        }
        return ValueTask.CompletedTask;
    }
}

public sealed record SimulationCommandBehavior(
    TimeSpan Delay = default,
    object? ReturnData = null,
    string? ErrorCode = null,
    string? ErrorMessage = null);

public sealed record SimulationCommandRecord(
    string CommandName,
    DateTime StartedAtUtc,
    DateTime CompletedAtUtc,
    CommandStatus Status,
    IReadOnlyDictionary<string, object> Parameters);
