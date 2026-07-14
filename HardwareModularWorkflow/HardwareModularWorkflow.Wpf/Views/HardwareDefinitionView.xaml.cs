using System.Windows;
using System.Windows.Controls;
using HardwareModularWorkflow.Wpf.ViewModels;

namespace HardwareModularWorkflow.Wpf.Views;

/// <summary>
/// Hardware definition management page.
/// </summary>
public partial class HardwareDefinitionView : UserControl
{
    public HardwareDefinitionView()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (DataContext is HardwareDefinitionViewModel viewModel)
        {
            await viewModel.LoadDefinitionsCommand.ExecuteAsync(null);
        }
    }
}
