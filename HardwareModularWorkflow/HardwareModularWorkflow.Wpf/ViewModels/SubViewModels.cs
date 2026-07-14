using CommunityToolkit.Mvvm.ComponentModel;

namespace HardwareModularWorkflow.Wpf.ViewModels;

/// <summary>
/// 工作流管理 ViewModel
/// </summary>
public partial class WorkflowViewModel : ObservableObject
{
    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private bool _isEditing = false;
}
