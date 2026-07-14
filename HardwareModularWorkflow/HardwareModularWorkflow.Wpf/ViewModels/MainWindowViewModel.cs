using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HardwareModularWorkflow.Core.Services;
using HardwareModularWorkflow.Wpf.ViewModels;

namespace HardwareModularWorkflow.Wpf.ViewModels;

/// <summary>
/// 主窗口 ViewModel：导航、运行时控制、状态管理
/// </summary>
public partial class MainWindowViewModel : ObservableObject
{
    private readonly WorkflowRuntimeService _runtimeService;
    private readonly DashboardViewModel _dashboardViewModel;
    private readonly HardwareViewModel _hardwareViewModel;
    private readonly ControllerViewModel _controllerViewModel;
    private readonly ModuleViewModel _moduleViewModel;
    private readonly FlowViewModel _flowViewModel;
    private readonly MonitorViewModel _monitorViewModel;
    private readonly ExecutionLogViewModel _executionLogViewModel;
    private readonly SettingsViewModel _settingsViewModel;

    [ObservableProperty]
    private string _statusMessage = "Ready";

    [ObservableProperty]
    private string _hardwareStatus = "No hardware connected";

    [ObservableProperty]
    private bool _isRuntimeRunning = false;

    [ObservableProperty]
    private NavigationItem? _selectedNavigationItem;

    [ObservableProperty]
    private object? _currentView;

    public ObservableCollection<NavigationItem> NavigationItems { get; } = new();

    public MainWindowViewModel(
        WorkflowRuntimeService runtimeService,
        DashboardViewModel dashboardViewModel,
        HardwareViewModel hardwareViewModel,
        ControllerViewModel controllerViewModel,
        ModuleViewModel moduleViewModel,
        FlowViewModel flowViewModel,
        MonitorViewModel monitorViewModel,
        ExecutionLogViewModel executionLogViewModel,
        SettingsViewModel settingsViewModel)
    {
        _runtimeService = runtimeService ?? throw new ArgumentNullException(nameof(runtimeService));
        _dashboardViewModel = dashboardViewModel ?? throw new ArgumentNullException(nameof(dashboardViewModel));
        _hardwareViewModel = hardwareViewModel ?? throw new ArgumentNullException(nameof(hardwareViewModel));
        _controllerViewModel = controllerViewModel ?? throw new ArgumentNullException(nameof(controllerViewModel));
        _moduleViewModel = moduleViewModel ?? throw new ArgumentNullException(nameof(moduleViewModel));
        _flowViewModel = flowViewModel ?? throw new ArgumentNullException(nameof(flowViewModel));
        _monitorViewModel = monitorViewModel ?? throw new ArgumentNullException(nameof(monitorViewModel));
        _executionLogViewModel = executionLogViewModel ?? throw new ArgumentNullException(nameof(executionLogViewModel));
        _settingsViewModel = settingsViewModel ?? throw new ArgumentNullException(nameof(settingsViewModel));
        InitializeNavigation();
    }

    private void InitializeNavigation()
    {
        NavigationItems.Add(new NavigationItem("Dashboard", "ViewDashboard", _dashboardViewModel));
        NavigationItems.Add(new NavigationItem("Hardware", "Chip", _hardwareViewModel));
        NavigationItems.Add(new NavigationItem("Controller", "LanConnect", _controllerViewModel));
        NavigationItems.Add(new NavigationItem("Module", "ViewModule", _moduleViewModel));
        NavigationItems.Add(new NavigationItem("Workflow", "SourceFork", _flowViewModel));
        NavigationItems.Add(new NavigationItem("Monitor", "Monitor", _monitorViewModel));
        NavigationItems.Add(new NavigationItem("Execution Log", "ClipboardTextClock", _executionLogViewModel));
        NavigationItems.Add(new NavigationItem("Settings", "Cog", _settingsViewModel));

        SelectedNavigationItem = NavigationItems.FirstOrDefault();
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
            IsRuntimeRunning = false;
            StatusMessage = "Runtime stopped";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Failed to stop runtime: {ex.Message}";
        }
    }

    [RelayCommand]
    private void RefreshHardware()
    {
        StatusMessage = "Hardware refreshed";
    }

    [RelayCommand]
    private void OpenSettings()
    {
        SelectedNavigationItem = NavigationItems.FirstOrDefault(n => n.Title == "Settings");
    }
}

/// <summary>
/// 导航项模型
/// </summary>
public class NavigationItem
{
    public string Title { get; }
    public string Icon { get; }
    public object ViewModel { get; }

    public NavigationItem(string title, string icon, object viewModel)
    {
        Title = title;
        Icon = icon;
        ViewModel = viewModel;
    }
}
