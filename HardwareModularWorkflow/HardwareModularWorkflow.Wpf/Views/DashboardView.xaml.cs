using System.Windows;
using System.Windows.Controls;
using HardwareModularWorkflow.Wpf.ViewModels;

namespace HardwareModularWorkflow.Wpf.Views;

/// <summary>
/// 仪表盘首页
/// </summary>
public partial class DashboardView : UserControl
{
    public DashboardView()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (DataContext is DashboardViewModel vm)
        {
            vm.Initialize();
            await vm.RefreshCommand.ExecuteAsync(null);
        }
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        if (DataContext is DashboardViewModel vm)
        {
            vm.Cleanup();
        }
    }
}
