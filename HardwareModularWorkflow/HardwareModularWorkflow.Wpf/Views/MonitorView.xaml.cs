using System.Windows;
using System.Windows.Controls;
using HardwareModularWorkflow.Wpf.ViewModels;

namespace HardwareModularWorkflow.Wpf.Views;

/// <summary>
/// 执行监控页面
/// </summary>
public partial class MonitorView : UserControl
{
    public MonitorView()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (DataContext is MonitorViewModel vm)
        {
            vm.Initialize();
            _ = vm.RefreshCommand.ExecuteAsync(null);
        }
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        if (DataContext is MonitorViewModel vm)
        {
            vm.Cleanup();
        }
    }
}
