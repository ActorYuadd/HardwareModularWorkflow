using System.Windows;
using System.Windows.Controls;
using HardwareModularWorkflow.Wpf.ViewModels;

namespace HardwareModularWorkflow.Wpf.Views;

/// <summary>
/// 工作流编辑器页面
/// </summary>
public partial class FlowView : UserControl
{
    public FlowView()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (DataContext is FlowViewModel vm)
        {
            await vm.LoadFlowsCommand.ExecuteAsync(null);
        }
    }
}
