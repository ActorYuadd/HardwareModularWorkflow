using System.Windows;
using System.Windows.Controls;
using HardwareModularWorkflow.Wpf.ViewModels;

namespace HardwareModularWorkflow.Wpf.Views;

/// <summary>
/// 控制器配置页面
/// </summary>
public partial class ControllerView : UserControl
{
    public ControllerView()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (DataContext is ControllerViewModel vm)
        {
            await vm.LoadControllersCommand.ExecuteAsync(null);
        }
    }
}
