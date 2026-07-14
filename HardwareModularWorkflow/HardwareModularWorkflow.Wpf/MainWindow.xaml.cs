using System.Windows;
using MahApps.Metro.Controls;
using HardwareModularWorkflow.Wpf.ViewModels;

namespace HardwareModularWorkflow.Wpf;

/// <summary>
/// 主窗口：MetroWindow + MaterialDesign 风格
/// </summary>
public partial class MainWindow : MetroWindow
{
    public MainWindow(MainWindowViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }
}
