using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using HardwareModularWorkflow.Wpf.ViewModels;

namespace HardwareModularWorkflow.Wpf.Views;

public partial class FlowView : UserControl
{
    private readonly DispatcherTimer _resourceRefreshTimer;

    public FlowView()
    {
        InitializeComponent();
        _resourceRefreshTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _resourceRefreshTimer.Tick += OnResourceRefreshTimerTick;
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (DataContext is FlowViewModel vm)
        {
            await vm.LoadFlowsCommand.ExecuteAsync(null);
            vm.RefreshResourceReservationsCommand.Execute(null);
            _resourceRefreshTimer.Start();
        }
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        _resourceRefreshTimer.Stop();
    }

    private void OnResourceRefreshTimerTick(object? sender, EventArgs e)
    {
        if (DataContext is FlowViewModel vm)
            vm.RefreshResourceReservationsCommand.Execute(null);
    }
}
