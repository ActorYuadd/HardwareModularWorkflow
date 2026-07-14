using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HardwareModularWorkflow.Core.Events;
using HardwareModularWorkflow.Core.Services;
using HardwareModularWorkflow.Db.Entities;
using HardwareModularWorkflow.Db.Services;
using HardwareModularWorkflow.Workflow.Events;

namespace HardwareModularWorkflow.Wpf.ViewModels;

/// <summary>
/// 仪表盘 ViewModel：硬件统计、控制器状态、今日执行、快捷入口、实时日志预览
/// </summary>
public partial class DashboardViewModel : ObservableObject
{
    private readonly HardwareInstanceService _hardwareService;
    private readonly ControllerService _controllerService;
    private readonly ExecutionLogService _logService;
    private readonly FlowService _flowService;
    private readonly IEventBus _eventBus;
    private readonly WorkflowRuntimeService _runtimeService;
    private IDisposable? _eventSubscription;

    // --- 硬件统计 ---
    [ObservableProperty]
    private int _totalHardwareCount;

    [ObservableProperty]
    private int _motorCount;

    [ObservableProperty]
    private int _temperatureCount;

    [ObservableProperty]
    private int _coolingCount;

    [ObservableProperty]
    private int _customCount;

    // --- 控制器统计 ---
    [ObservableProperty]
    private int _totalControllerCount;

    [ObservableProperty]
    private int _plcCount;

    [ObservableProperty]
    private int _canCount;

    // --- 执行统计 ---
    [ObservableProperty]
    private int _todaySessionCount;

    [ObservableProperty]
    private int _todaySuccessCount;

    [ObservableProperty]
    private int _todayFailedCount;

    // --- 工作流统计 ---
    [ObservableProperty]
    private int _totalFlowCount;

    [ObservableProperty]
    private int _reusableFlowCount;

    // --- 实时日志预览 ---
    [ObservableProperty]
    private ObservableCollection<WorkflowEvent> _recentEvents = new();

    [ObservableProperty]
    private int _maxRecentEvents = 5;

    // --- 运行时状态 ---
    [ObservableProperty]
    private bool _isRuntimeRunning;

    [ObservableProperty]
    private string _statusMessage = "Ready";

    [ObservableProperty]
    private bool _isLoading;

    public DashboardViewModel(
        HardwareInstanceService hardwareService,
        ControllerService controllerService,
        ExecutionLogService logService,
        FlowService flowService,
        IEventBus eventBus,
        WorkflowRuntimeService runtimeService)
    {
        _hardwareService = hardwareService ?? throw new ArgumentNullException(nameof(hardwareService));
        _controllerService = controllerService ?? throw new ArgumentNullException(nameof(controllerService));
        _logService = logService ?? throw new ArgumentNullException(nameof(logService));
        _flowService = flowService ?? throw new ArgumentNullException(nameof(flowService));
        _eventBus = eventBus ?? throw new ArgumentNullException(nameof(eventBus));
        _runtimeService = runtimeService ?? throw new ArgumentNullException(nameof(runtimeService));
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
        if (App.Current?.Dispatcher is not null)
        {
            App.Current.Dispatcher.Invoke(() =>
            {
                RecentEvents.Insert(0, evt);
                while (RecentEvents.Count > MaxRecentEvents)
                {
                    RecentEvents.RemoveAt(RecentEvents.Count - 1);
                }
            });
        }
        else
        {
            RecentEvents.Insert(0, evt);
            while (RecentEvents.Count > MaxRecentEvents)
            {
                RecentEvents.RemoveAt(RecentEvents.Count - 1);
            }
        }
        return Task.CompletedTask;
    }

    [RelayCommand]
    private async Task RefreshAsync()
    {
        IsLoading = true;
        StatusMessage = "Refreshing dashboard...";
        try
        {
            // 硬件统计
            var hardware = await _hardwareService.GetAllAsync();
            TotalHardwareCount = hardware.Count;
            MotorCount = hardware.Count(h => h.Definition.Type == "Motor");
            TemperatureCount = hardware.Count(h => h.Definition.Type == "Temperature");
            CoolingCount = hardware.Count(h => h.Definition.Type == "Cooling");
            CustomCount = hardware.Count(h => h.Definition.Type == "Custom");

            // 控制器统计
            var controllers = await _controllerService.GetAllAsync();
            TotalControllerCount = controllers.Count;
            PlcCount = controllers.Count(c => c.ControllerType == "Plc");
            CanCount = controllers.Count(c => c.ControllerType == "Can");

            // 今日执行统计（UTC 日期）
            var today = DateTime.UtcNow.Date;
            var sessions = await _logService.GetRecentSessionsAsync(200);
            var todaySessions = sessions.Where(s => s.StartedAt >= today).ToList();
            TodaySessionCount = todaySessions.Count;
            TodaySuccessCount = todaySessions.Count(s => s.IsSuccess);
            TodayFailedCount = todaySessions.Count(s => !s.IsSuccess);

            // 工作流统计
            var flows = await _flowService.GetAllAsync();
            TotalFlowCount = flows.Count;
            ReusableFlowCount = flows.Count(f => f.IsReusable);

            // 运行时状态（简单判断，实际应根据调度器状态）
            IsRuntimeRunning = true; // 占位：运行时服务通常在启动时已启动

            StatusMessage = $"Dashboard refreshed at {DateTime.Now:HH:mm:ss}";
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
    private void NavigateTo(string pageName)
    {
        if (App.Current?.MainWindow?.DataContext is MainWindowViewModel mainVm)
        {
            var item = mainVm.NavigationItems.FirstOrDefault(n => n.Title == pageName);
            if (item is not null)
                mainVm.SelectedNavigationItem = item;
        }
    }
}
