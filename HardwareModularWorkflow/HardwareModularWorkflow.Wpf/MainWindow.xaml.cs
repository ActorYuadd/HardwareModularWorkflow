using System.Windows;
using HardwareModularWorkflow.Wpf.ViewModels;

namespace HardwareModularWorkflow.Wpf;

/// <summary>
/// 主窗口
/// </summary>
public partial class MainWindow : Window
{
    public MainWindow(MainWindowViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }
}
