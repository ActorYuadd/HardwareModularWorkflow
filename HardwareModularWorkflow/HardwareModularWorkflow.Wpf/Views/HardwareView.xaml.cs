using System.Windows;
using System.Windows.Controls;
using HardwareModularWorkflow.Wpf.ViewModels;

namespace HardwareModularWorkflow.Wpf.Views;

/// <summary>
/// 硬件管理页面
/// </summary>
public partial class HardwareView : UserControl
{
    public HardwareView()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (DataContext is HardwareViewModel vm)
        {
            await vm.LoadHardwareCommand.ExecuteAsync(null);
        }
    }
}
