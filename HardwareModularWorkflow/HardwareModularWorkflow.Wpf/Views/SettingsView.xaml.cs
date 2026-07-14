using System.Windows;
using System.Windows.Controls;
using HardwareModularWorkflow.Wpf.ViewModels;

namespace HardwareModularWorkflow.Wpf.Views;

/// <summary>
/// 系统设置页面：语言、主题、数据库、调度器、日志、DLL 路径
/// </summary>
public partial class SettingsView : UserControl
{
    public SettingsView()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (DataContext is SettingsViewModel vm)
        {
            _ = vm.LoadSettingsCommand.ExecuteAsync(null);
        }
    }
}
