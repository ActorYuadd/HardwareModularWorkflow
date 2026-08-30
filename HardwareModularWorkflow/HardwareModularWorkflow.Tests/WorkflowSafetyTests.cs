using HardwareModularWorkflow.Db.DbContext;
using HardwareModularWorkflow.Db.Entities;
using HardwareModularWorkflow.Db.Services;
using HardwareModularWorkflow.Core.Services;
using HardwareModularWorkflow.Hardware.Abstractions;
using HardwareModularWorkflow.Hardware.Enums;
using HardwareModularWorkflow.Hardware.Models;
using HardwareModularWorkflow.Hardware.Results;
using HardwareModularWorkflow.Hardware.Simulation;
using HardwareModularWorkflow.Workflow.Abstractions;
using HardwareModularWorkflow.Workflow.Engine;
using HardwareModularWorkflow.Workflow.Models;
using HardwareModularWorkflow.Workflow.Resources;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;
using ExecutionContext = HardwareModularWorkflow.Workflow.Abstractions.ExecutionContext;

namespace HardwareModularWorkflow.Tests;

public sealed class WorkflowSafetyTests
{
    [Fact]
    public async Task Exclusive_resource_is_not_granted_to_two_runs_at_once()
    {
        var manager = new ResourceReservationManager();
        await using var first = await manager.AcquireAsync(Request("axis-x", TimeSpan.FromSeconds(1)));
        var secondTask = manager.AcquireAsync(Request("axis-x", TimeSpan.FromSeconds(1)));

        await Task.Delay(30);
        Assert.False(secondTask.IsCompleted);

        await first.DisposeAsync();
        await using var second = await secondTask.WaitAsync(TimeSpan.FromSeconds(1));
        Assert.Single(manager.GetSnapshot(), item => item.IsGranted);
    }

    [Fact]
    public async Task Resource_wait_timeout_fails_without_leaking_a_reservation()
    {
        var manager = new ResourceReservationManager();
        await using var held = await manager.AcquireAsync(Request("axis-x", TimeSpan.FromSeconds(1)));

        await Assert.ThrowsAsync<ResourceReservationTimeoutException>(() =>
            manager.AcquireAsync(Request("axis-x", TimeSpan.FromMilliseconds(40))));

        var metrics = manager.GetMetrics();
        Assert.Equal(0, metrics.WaitingReservations);
        Assert.Equal(1, metrics.GrantedReservations);
    }

    [Fact]
    public async Task Priority_inheritance_propagates_through_a_blocking_chain()
    {
        var manager = new ResourceReservationManager(agingStepSeconds: 60, maxAgingBoost: 0);
        var lowRun = Guid.NewGuid();
        var middleRun = Guid.NewGuid();
        var highRun = Guid.NewGuid();
        await using var lowOwnsX = await manager.AcquireAsync(Request("axis-x", null, lowRun, 1));
        await using var middleOwnsY = await manager.AcquireAsync(Request("axis-y", null, middleRun, 2));
        using var waitsY = new CancellationTokenSource();
        using var waitsX = new CancellationTokenSource();
        var lowWaitTask = manager.AcquireAsync(Request("axis-y", null, lowRun, 1), waitsY.Token);
        var highWaitTask = manager.AcquireAsync(Request("axis-x", null, highRun, 100), waitsX.Token);

        await Task.Delay(20);
        var snapshot = manager.GetSnapshot();
        Assert.All(snapshot.Where(item => item.WorkflowRunId == lowRun), item => Assert.Equal(100, item.EffectivePriority));
        Assert.Equal(100, snapshot.Single(item => item.WorkflowRunId == middleRun).EffectivePriority);

        waitsY.Cancel();
        waitsX.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => lowWaitTask);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => highWaitTask);
    }

    [Fact]
    public async Task Graph_cycle_stops_at_the_declared_node_visit_limit()
    {
        var start = new WorkflowNode { Type = WorkflowNodeType.Start, Name = "Start" };
        var loop = new WorkflowNode
        {
            Type = WorkflowNodeType.Switch,
            Name = "Loop",
            RouteKeyVariable = "route",
            MaxVisits = 2
        };
        var end = new WorkflowNode { Type = WorkflowNodeType.End, Name = "End" };
        var flow = new Flow
        {
            FlowId = 1,
            Name = "bounded-loop",
            GraphNodes = new List<WorkflowNode> { start, loop, end },
            GraphEdges = new List<WorkflowEdge>
            {
                new() { FromNodeId = start.NodeId, ToNodeId = loop.NodeId },
                new() { FromNodeId = loop.NodeId, ToNodeId = loop.NodeId, RouteKey = "Again" },
                new() { FromNodeId = loop.NodeId, ToNodeId = end.NodeId, IsDefault = true }
            }
        };
        var context = new ExecutionContext { CurrentFlowId = flow.FlowId };
        context.SetVariable("route", "Again");
        var executor = new FlowExecutor(new ModuleExecutor(new RecordingStepExecutor()));

        var result = await executor.ExecuteAsync(flow, context, null, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Contains("visit limit", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Failed_module_runs_compensation_in_reverse_order()
    {
        var hardware = Hardware(7);
        var stepExecutor = new RecordingStepExecutor(failedCommand: "Move");
        var module = new Module
        {
            ModuleId = Guid.NewGuid(),
            Name = "motion",
            Steps = new List<HardwareStep> { Step("Move", hardware, 0) },
            CompensationSteps = new List<HardwareStep>
            {
                Step("ReleaseClamp", hardware, 0),
                Step("StopAxis", hardware, 1)
            }
        };

        var result = await new ModuleExecutor(stepExecutor).ExecuteAsync(module, new ExecutionContext());

        Assert.False(result.IsSuccess);
        Assert.True(result.CompensationAttempted);
        Assert.True(result.CompensationSucceeded);
        Assert.Equal(new[] { "Move", "StopAxis", "ReleaseClamp" }, stepExecutor.Commands);
    }

    [Fact]
    public async Task Simulation_driver_stop_cancels_an_active_command_and_records_it()
    {
        var driver = new SimulationHardwareDriver(Hardware(9));
        driver.Configure("Move", new SimulationCommandBehavior(Delay: TimeSpan.FromSeconds(5)));
        var commandTask = driver.ExecuteAsync(new TestCommand("Move"));

        await Task.Delay(30);
        Assert.True(await driver.TryStopAsync());
        var result = await commandTask.WaitAsync(TimeSpan.FromSeconds(1));

        Assert.Equal(CommandStatus.Cancelled, result.Status);
        Assert.Equal(HardwareState.Idle, await driver.GetStateAsync());
        Assert.Equal(CommandStatus.Cancelled, Assert.Single(driver.CommandRecords).Status);
        await driver.DisposeAsync();
    }

    [Fact]
    public async Task Persistent_snapshot_marks_interrupted_run_as_recovery_required()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<HardwareModularWorkflowDbContext>()
            .UseSqlite(connection)
            .Options;
        await using var context = new HardwareModularWorkflowDbContext(options);
        await context.Database.EnsureCreatedAsync();
        var service = new WorkflowRunSnapshotService(context);
        var executionId = Guid.NewGuid();
        await service.UpsertAsync(new WorkflowRunSnapshot
        {
            ExecutionId = executionId,
            FlowId = 42,
            DefinitionVersion = 3,
            FlowName = "recoverable",
            Status = WorkflowRunStatuses.Running,
            RecoveryPolicy = WorkflowRecoveryPolicy.RestartFromBeginning.ToString(),
            InputsJson = "{\"count\":3}"
        });

        Assert.Equal(1, await service.MarkInterruptedRunsAsync());
        var stored = await service.GetAsync(executionId);
        Assert.NotNull(stored);
        Assert.Equal(WorkflowRunStatuses.RecoveryRequired, stored.Status);
        Assert.Equal(3, stored.DefinitionVersion);
        Assert.Contains("process ended", stored.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Recovery_restart_is_blocked_when_the_definition_version_changed()
    {
        var snapshot = new WorkflowRunSnapshot
        {
            ExecutionId = Guid.NewGuid(),
            FlowId = 42,
            DefinitionVersion = 3,
            FlowName = "recoverable",
            Status = WorkflowRunStatuses.RecoveryRequired,
            RecoveryPolicy = WorkflowRecoveryPolicy.RestartFromBeginning.ToString()
        };
        var currentFlow = new Flow
        {
            FlowId = 42,
            Name = "recoverable",
            DefinitionVersion = 4,
            RecoveryPolicy = WorkflowRecoveryPolicy.RestartFromBeginning
        };

        Assert.False(WorkflowRecoveryValidator.TryValidateRestart(snapshot, currentFlow, out var error));
        Assert.Contains("version 3", error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Recovery_restart_is_allowed_only_for_matching_explicit_policy_and_version()
    {
        var snapshot = new WorkflowRunSnapshot
        {
            ExecutionId = Guid.NewGuid(),
            FlowId = 42,
            DefinitionVersion = 3,
            FlowName = "recoverable",
            Status = WorkflowRunStatuses.RecoveryRequired,
            RecoveryPolicy = WorkflowRecoveryPolicy.RestartFromBeginning.ToString()
        };
        var currentFlow = new Flow
        {
            FlowId = 42,
            Name = "recoverable",
            DefinitionVersion = 3,
            RecoveryPolicy = WorkflowRecoveryPolicy.RestartFromBeginning
        };

        Assert.True(WorkflowRecoveryValidator.TryValidateRestart(snapshot, currentFlow, out var error));
        Assert.Null(error);
    }

    private static ResourceReservationRequest Request(
        string resourceId,
        TimeSpan? timeout,
        Guid? runId = null,
        int priority = 0) => new()
        {
            WorkflowRunId = runId ?? Guid.NewGuid(),
            NodeRunId = Guid.NewGuid(),
            Requirements = new[] { new ResourceRequirement(resourceId) },
            BasePriority = priority,
            WaitTimeout = timeout
        };

    private static CustomHardware Hardware(long id) => new()
    {
        Id = id,
        Name = $"hardware-{id}"
    };

    private static HardwareStep Step(string commandName, IHardware hardware, int order) => new()
    {
        StepId = Guid.NewGuid(),
        Name = commandName,
        Order = order,
        Hardware = hardware,
        Command = new TestCommand(commandName)
    };

    private sealed class TestCommand : IHardwareCommand
    {
        public TestCommand(string commandName) => CommandName = commandName;
        public string CommandName { get; }
        public Dictionary<string, object> Parameters { get; } = new();
        public bool IsAsync => true;
        public TimeSpan? Timeout => null;
        public Type? ExpectedReturnType => null;
    }

    private sealed class RecordingStepExecutor : IStepExecutor
    {
        private readonly string? _failedCommand;
        public RecordingStepExecutor(string? failedCommand = null) => _failedCommand = failedCommand;
        public List<string> Commands { get; } = new();

        public Task<CommandResult> ExecuteAsync(HardwareStep step, ExecutionContext context, CancellationToken ct = default)
        {
            Commands.Add(step.Command.CommandName);
            return Task.FromResult(string.Equals(step.Command.CommandName, _failedCommand, StringComparison.Ordinal)
                ? CommandResult.Failed(TimeSpan.Zero, "SIMULATED", "Simulated failure")
                : CommandResult.Success(TimeSpan.Zero));
        }
    }
}
