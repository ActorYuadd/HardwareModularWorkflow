using System.Windows;
using System.Windows.Controls;
using HardwareModularWorkflow.Wpf.ViewModels;

namespace HardwareModularWorkflow.Wpf.Views;

/// <summary>
/// 执行日志页面：日志列表、过滤查询、会话详情、错误分析、导出
/// </summary>
public partial class ExecutionLogView : UserControl
{
    public ExecutionLogView()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (DataContext is ExecutionLogViewModel vm)
        {
            _ = vm.RefreshCommand.ExecuteAsync(null);
        }
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        // No event subscriptions to clean up in this view
    }
}
