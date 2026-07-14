using System.Windows;
using System.Windows.Controls;
using HardwareModularWorkflow.Wpf.ViewModels;

namespace HardwareModularWorkflow.Wpf.Views;

/// <summary>
/// 模块编辑器页面
/// </summary>
public partial class ModuleView : UserControl
{
    public ModuleView()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (DataContext is ModuleViewModel vm)
        {
            await vm.LoadModulesCommand.ExecuteAsync(null);
        }
    }
}
