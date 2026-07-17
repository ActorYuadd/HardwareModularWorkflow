using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HardwareModularWorkflow.Core.Services;
using HardwareModularWorkflow.Lang;
using HardwareModularWorkflow.Lang.Strings;
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
    private readonly HardwareDefinitionViewModel _hardwareDefinitionViewModel;
    private readonly ControllerViewModel _controllerViewModel;
    private readonly ModuleViewModel _moduleViewModel;
    private readonly FlowViewModel _flowViewModel;
    private readonly MonitorViewModel _monitorViewModel;
    private readonly ExecutionLogViewModel _executionLogViewModel;
    private readonly SettingsViewModel _settingsViewModel;

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
        SettingsViewModel settingsViewModel)
    {
        _runtimeService = runtimeService ?? throw new ArgumentNullException(nameof(runtimeService));
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
