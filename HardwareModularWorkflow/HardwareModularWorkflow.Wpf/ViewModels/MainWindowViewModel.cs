using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HardwareModularWorkflow.Core.Events;
using HardwareModularWorkflow.Core.Services;
using HardwareModularWorkflow.Lang;
using HardwareModularWorkflow.Lang.Strings;
using HardwareModularWorkflow.Workflow.Events;
using HardwareModularWorkflow.Workflow.Resources;
using HardwareModularWorkflow.Wpf.ViewModels;

namespace HardwareModularWorkflow.Wpf.ViewModels;

/// <summary>
/// 主窗口 ViewModel：导航、运行时控制、状态管理
/// </summary>
public partial class MainWindowViewModel : ObservableObject
{
    private readonly WorkflowRuntimeService _runtimeService;
    private readonly IEventBus? _eventBus;
    private readonly IResourceReservationManager? _resourceReservationManager;
    private readonly DashboardViewModel _dashboardViewModel;
    private readonly HardwareViewModel _hardwareViewModel;
    private readonly HardwareDefinitionViewModel _hardwareDefinitionViewModel;
    private readonly ControllerViewModel _controllerViewModel;
    private readonly ModuleViewModel _moduleViewModel;
    private readonly FlowViewModel _flowViewModel;
    private readonly MonitorViewModel _monitorViewModel;
    private readonly ExecutionLogViewModel _executionLogViewModel;
    private readonly SettingsViewModel _settingsViewModel;
    private IDisposable? _eventSubscription;

    [ObservableProperty]
    private string _statusMessage = LangKeys.Status_Ready;

    [ObservableProperty]
    private string _hardwareStatus = LangKeys.Status_NoHardwareConnected;

    [ObservableProperty]
    private bool _isRuntimeRunning = false;

    [ObservableProperty]
    private NavigationItem? _selectedNavigationItem;

    [ObservableProperty]
    private object? _currentView;

    public ObservableCollection<NavigationItem> NavigationItems { get; } = new();

    // --- 底部面板 ---
    [ObservableProperty]
    private ObservableCollection<LiveEventItem> _liveEvents = new();

    [ObservableProperty]
    private string _systemStatus = "就绪";

    [ObservableProperty]
    private string _runtimeMetrics = string.Empty;

    [ObservableProperty]
    private string _resourceMetrics = string.Empty;

    [ObservableProperty]
    private double _runtimeSlotsUsed;

    [ObservableProperty]
    private double _runtimeSlotsMax = 50;

    public MainWindowViewModel(
        WorkflowRuntimeService runtimeService,
        DashboardViewModel dashboardViewModel,
        HardwareViewModel hardwareViewModel,
        HardwareDefinitionViewModel hardwareDefinitionViewModel,
        ControllerViewModel controllerViewModel,
        ModuleViewModel moduleViewModel,
        FlowViewModel flowViewModel,
        MonitorViewModel monitorViewModel,
        ExecutionLogViewModel executionLogViewModel,
        SettingsViewModel settingsViewModel,
        IEventBus? eventBus = null,
        IResourceReservationManager? resourceReservationManager = null)
    {
        _runtimeService = runtimeService ?? throw new ArgumentNullException(nameof(runtimeService));
        _eventBus = eventBus;
        _resourceReservationManager = resourceReservationManager;
        _dashboardViewModel = dashboardViewModel ?? throw new ArgumentNullException(nameof(dashboardViewModel));
        _hardwareViewModel = hardwareViewModel ?? throw new ArgumentNullException(nameof(hardwareViewModel));
        _hardwareDefinitionViewModel = hardwareDefinitionViewModel
            ?? throw new ArgumentNullException(nameof(hardwareDefinitionViewModel));
        _controllerViewModel = controllerViewModel ?? throw new ArgumentNullException(nameof(controllerViewModel));
        _moduleViewModel = moduleViewModel ?? throw new ArgumentNullException(nameof(moduleViewModel));
        _flowViewModel = flowViewModel ?? throw new ArgumentNullException(nameof(flowViewModel));
        _monitorViewModel = monitorViewModel ?? throw new ArgumentNullException(nameof(monitorViewModel));
        _executionLogViewModel = executionLogViewModel ?? throw new ArgumentNullException(nameof(executionLogViewModel));
        _settingsViewModel = settingsViewModel ?? throw new ArgumentNullException(nameof(settingsViewModel));
        _settingsViewModel.PropertyChanged += OnSettingsViewModelPropertyChanged;
        InitializeNavigation();

        if (_eventBus is not null)
            _eventSubscription = _eventBus.Subscribe<WorkflowEvent>(OnWorkflowEvent);

        UpdateMetrics();
    }

    private void OnSettingsViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SettingsViewModel.Language))
        {
            foreach (var item in NavigationItems)
            {
                item.RefreshDisplayTitle();
            }
        }
    }

    private void InitializeNavigation()
    {
        NavigationItems.Add(new NavigationItem(LangKeys.Navigation_Dashboard, "ViewDashboard", _dashboardViewModel));
        NavigationItems.Add(new NavigationItem(LangKeys.Navigation_Hardware, "Chip", _hardwareViewModel));
        NavigationItems.Add(new NavigationItem(LangKeys.Navigation_HardwareDefinitions, "Shape", _hardwareDefinitionViewModel));
        NavigationItems.Add(new NavigationItem(LangKeys.Navigation_Controller, "LanConnect", _controllerViewModel));
        NavigationItems.Add(new NavigationItem(LangKeys.Navigation_Module, "ViewModule", _moduleViewModel));
        NavigationItems.Add(new NavigationItem(LangKeys.Navigation_Workflow, "SourceFork", _flowViewModel));
        NavigationItems.Add(new NavigationItem(LangKeys.Navigation_Monitor, "Monitor", _monitorViewModel));
        NavigationItems.Add(new NavigationItem(LangKeys.Navigation_ExecutionLog, "ClipboardTextClock", _executionLogViewModel));
        NavigationItems.Add(new NavigationItem(LangKeys.Navigation_Settings, "Cog", _settingsViewModel));

        SelectedNavigationItem = NavigationItems.FirstOrDefault();
    }

    private Task OnWorkflowEvent(WorkflowEvent evt)
    {
        Application.Current.Dispatcher.InvokeAsync(() =>
        {
            LiveEvents.Insert(0, new LiveEventItem
            {
                Time = evt.Timestamp.ToLocalTime().ToString("HH:mm:ss.fff"),
                Type = evt.EventType.ToString(),
                FlowName = evt.FlowId > 0 ? evt.FlowId.ToString() : "-",
                Message = evt.Message ?? evt.EventType.ToString(),
                IsError = evt.IsError
            });
            while (LiveEvents.Count > 200) LiveEvents.RemoveAt(LiveEvents.Count - 1);
            UpdateMetrics();
        });
        return Task.CompletedTask;
    }

    private void UpdateMetrics()
    {
        try
        {
            RuntimeMetrics = $"运行中: {_runtimeService.CurrentRunningTasks} / 槽位: {_runtimeService.MaxConcurrencySlots} | 可用: {_runtimeService.CurrentAvailableSlots}";
            RuntimeSlotsUsed = _runtimeService.CurrentRunningTasks;
            RuntimeSlotsMax = _runtimeService.MaxConcurrencySlots;
            SystemStatus = _runtimeService.IsRuntimeRunning ? "运行中" : "已停止";
        }
        catch
        {
            SystemStatus = "未知";
        }
        if (_resourceReservationManager is not null)
        {
            try
            {
                var metrics = _resourceReservationManager.GetMetrics();
                ResourceMetrics = $"资源: 等待 {metrics.WaitingReservations}/{metrics.QueueCapacity} | 持有 {metrics.GrantedReservations} | 继承提升 {metrics.InheritedPriorityReservations} | 最长等待 {metrics.OldestWaitingDuration.TotalSeconds:F1}s";
            }
            catch { ResourceMetrics = string.Empty; }
        }
    }

    partial void OnSelectedNavigationItemChanged(NavigationItem? value)
    {
        if (value?.ViewModel is not null)
        {
            CurrentView = value.ViewModel;
        }
    }

    [RelayCommand]
    private async Task StartRuntimeAsync()
    {
        try
        {
            _runtimeService.Start();
            IsRuntimeRunning = true;
            StatusMessage = LangKeys.Message_RuntimeStarted;
        }
        catch (Exception ex)
        {
            StatusMessage = string.Format(LangKeys.Message_FailedToStartRuntime, ex.Message);
        }
    }

    [RelayCommand]
    private async Task StopRuntimeAsync()
    {
        try
        {
            await _runtimeService.StopAsync(TimeSpan.FromSeconds(5));
            IsRuntimeRunning = false;
            StatusMessage = LangKeys.Message_RuntimeStopped;
        }
        catch (Exception ex)
        {
            StatusMessage = string.Format(LangKeys.Message_FailedToStopRuntime, ex.Message);
        }
    }

    [RelayCommand]
    private void RefreshHardware()
    {
        StatusMessage = LangKeys.Message_HardwareRefreshed;
    }

    [RelayCommand]
    private void OpenSettings()
    {
        SelectedNavigationItem = NavigationItems.FirstOrDefault(n => n.Title == LangKeys.Navigation_Settings);
    }

    [RelayCommand]
    private void NavigateTo(string pageName)
    {
        var item = NavigationItems.FirstOrDefault(n => n.Title == pageName);
        if (item is not null)
        {
            SelectedNavigationItem = item;
        }
    }
}

/// <summary>
/// 导航项模型
/// </summary>
public class NavigationItem : INotifyPropertyChanged
{
    public string Title { get; }
    public string Icon { get; }
    public object ViewModel { get; }

    public string DisplayTitle => Resources.ResourceManager.GetString(Title, Resources.Culture ?? CultureInfo.CurrentUICulture) ?? Title;

    public NavigationItem(string title, string icon, object viewModel)
    {
        Title = title;
        Icon = icon;
        ViewModel = viewModel;
    }

    public void RefreshDisplayTitle()
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(DisplayTitle)));
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}

public class LiveEventItem
{
    public string Time { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public string FlowName { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public bool IsError { get; set; }
}
