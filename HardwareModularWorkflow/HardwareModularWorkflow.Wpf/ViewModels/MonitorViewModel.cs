using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HardwareModularWorkflow.Core.Events;
using HardwareModularWorkflow.Core.Orchestration;
using HardwareModularWorkflow.Core.Services;
using HardwareModularWorkflow.Db.Entities;
using HardwareModularWorkflow.Db.Services;
using HardwareModularWorkflow.Hardware.Enums;
using HardwareModularWorkflow.Workflow.Events;

namespace HardwareModularWorkflow.Wpf.ViewModels;

/// <summary>
/// 硬件监控项：硬件实例 + 运行时状态
/// </summary>
public sealed partial class HardwareMonitorItem : ObservableObject
{
    public long Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public string? ControllerName { get; set; }
    public bool IsEnabled { get; set; }
    public string Port { get; set; } = string.Empty;
    public string Address { get; set; } = string.Empty;

    [ObservableProperty]
    private HardwareState _state = HardwareState.Offline;
}

/// <summary>
/// 执行监控 ViewModel：实时执行面板、硬件状态看板、实时日志流、启停控制
/// </summary>
public partial class MonitorViewModel : ObservableObject
{
    private readonly IEventBus _eventBus;
    private readonly ExecutionLogService _logService;
    private readonly HardwareInstanceService _hardwareService;
    private readonly WorkflowRuntimeService _runtimeService;
    private readonly HardwareDriverFactory _driverFactory;
    private IDisposable? _eventSubscription;

    [ObservableProperty]
    private ObservableCollection<WorkflowEvent> _liveLogs = new();

    [ObservableProperty]
    private int _maxLogEntries = 200;

    [ObservableProperty]
    private ObservableCollection<HardwareMonitorItem> _hardwareItems = new();

    [ObservableProperty]
    private ObservableCollection<ExecutionSession> _recentSessions = new();

    [ObservableProperty]
    private ExecutionSession? _selectedSession;

    [ObservableProperty]
    private ObservableCollection<ExecutionLog> _selectedSessionLogs = new();

    [ObservableProperty]
    private bool _isRuntimeRunning = false;

    [ObservableProperty]
    private int _maxSlots = 50;

    [ObservableProperty]
    private int _currentAvailableSlots = 50;

    [ObservableProperty]
    private int _currentRunningTasks = 0;

    [ObservableProperty]
    private string _statusMessage = "Ready";

    [ObservableProperty]
    private bool _isLoading = false;

    public MonitorViewModel(
        IEventBus eventBus,
        ExecutionLogService logService,
        HardwareInstanceService hardwareService,
        WorkflowRuntimeService runtimeService,
        HardwareDriverFactory driverFactory)
    {
        _eventBus = eventBus ?? throw new ArgumentNullException(nameof(eventBus));
        _logService = logService ?? throw new ArgumentNullException(nameof(logService));
        _hardwareService = hardwareService ?? throw new ArgumentNullException(nameof(hardwareService));
        _runtimeService = runtimeService ?? throw new ArgumentNullException(nameof(runtimeService));
        _driverFactory = driverFactory ?? throw new ArgumentNullException(nameof(driverFactory));
    }

    /// <summary>
    /// 初始化：订阅事件总线
    /// </summary>
    public void Initialize()
    {
        if (_eventSubscription is not null) return;
        _eventSubscription = _eventBus.Subscribe<WorkflowEvent>(OnWorkflowEventAsync);
    }

    /// <summary>
    /// 清理：取消事件订阅
    /// </summary>
    public void Cleanup()
    {
        _eventSubscription?.Dispose();
        _eventSubscription = null;
    }

    private Task OnWorkflowEventAsync(WorkflowEvent evt)
    {
        // 确保在 UI 线程更新集合
        if (App.Current?.Dispatcher is not null)
        {
            App.Current.Dispatcher.Invoke(() =>
            {
                LiveLogs.Insert(0, evt);
                while (LiveLogs.Count > MaxLogEntries)
                {
                    LiveLogs.RemoveAt(LiveLogs.Count - 1);
                }
            });
        }
        else
        {
            LiveLogs.Insert(0, evt);
            while (LiveLogs.Count > MaxLogEntries)
            {
                LiveLogs.RemoveAt(LiveLogs.Count - 1);
            }
        }
        return Task.CompletedTask;
    }

    [RelayCommand]
    private async Task RefreshAsync()
    {
        IsLoading = true;
        StatusMessage = "Refreshing monitor data...";
        try
        {
            // 加载硬件实例 + 实时状态
            var instances = await _hardwareService.GetAllAsync();
            var states = await _driverFactory.GetAllDriverStatesAsync();

            var items = instances.Select(i => new HardwareMonitorItem
            {
                Id = i.Id,
                Name = i.Name,
                Type = i.Definition?.Type ?? "Unknown",
                ControllerName = i.Controller?.Name,
                IsEnabled = i.IsEnabled,
                Port = i.Port ?? string.Empty,
                Address = i.Address ?? string.Empty,
                State = states.TryGetValue(i.Id, out var s) ? s : HardwareState.Offline
            }).ToList();

            HardwareItems = new ObservableCollection<HardwareMonitorItem>(items);

            var sessions = await _logService.GetRecentSessionsAsync(20);
            RecentSessions = new ObservableCollection<ExecutionSession>(sessions);

            // 同步运行时状态
            SyncRuntimeStatus();

            StatusMessage = $"Monitor refreshed: {items.Count} hardware, {sessions.Count} sessions";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Failed to refresh: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private void StartRuntime()
    {
        try
        {
            _runtimeService.Start();
            SyncRuntimeStatus();
            StatusMessage = "Runtime started";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Failed to start runtime: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task StopRuntimeAsync()
    {
        try
        {
            await _runtimeService.StopAsync(TimeSpan.FromSeconds(5));
            SyncRuntimeStatus();
            StatusMessage = "Runtime stopped";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Failed to stop runtime: {ex.Message}";
        }
    }

    [RelayCommand]
    private void CancelAll()
    {
        try
        {
            _runtimeService.CancelAll();
            SyncRuntimeStatus();
            StatusMessage = "All executions cancelled";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Failed to cancel: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task LoadSessionLogsAsync(ExecutionSession? session)
    {
        if (session is null)
        {
            SelectedSessionLogs.Clear();
            return;
        }

        SelectedSession = session;
        try
        {
            var logs = await _logService.GetBySessionIdAsync(session.Id);
            SelectedSessionLogs = new ObservableCollection<ExecutionLog>(logs);
            StatusMessage = $"Loaded {logs.Count} logs for session '{session.Name}'";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Failed to load logs: {ex.Message}";
        }
    }

    [RelayCommand]
    private void ClearLogs()
    {
        LiveLogs.Clear();
        StatusMessage = "Live logs cleared";
    }

    /// <summary>
    /// 从运行时服务同步状态到 UI 属性
    /// </summary>
    private void SyncRuntimeStatus()
    {
        IsRuntimeRunning = _runtimeService.IsRuntimeRunning;
        MaxSlots = _runtimeService.MaxConcurrencySlots;
        CurrentAvailableSlots = _runtimeService.CurrentAvailableSlots;
        CurrentRunningTasks = _runtimeService.CurrentRunningTasks;
    }
}
