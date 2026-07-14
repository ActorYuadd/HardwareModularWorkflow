using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HardwareModularWorkflow.Db.Entities;
using HardwareModularWorkflow.Db.Services;

namespace HardwareModularWorkflow.Wpf.ViewModels;

/// <summary>
/// 硬件管理 ViewModel：硬件列表、添加/编辑、控制器绑定
/// </summary>
public partial class HardwareViewModel : ObservableObject
{
    private readonly HardwareInstanceService _instanceService;
    private readonly HardwareDefinitionService _definitionService;
    private readonly ControllerService _controllerService;

    [ObservableProperty]
    private ObservableCollection<HardwareInstance> _hardwareInstances = new();

    [ObservableProperty]
    private ObservableCollection<HardwareInstance> _filteredInstances = new();

    [ObservableProperty]
    private ObservableCollection<HardwareDefinition> _hardwareDefinitions = new();

    [ObservableProperty]
    private ObservableCollection<ControllerEntity> _controllers = new();

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private bool _isLoading = false;

    [ObservableProperty]
    private bool _isEditing = false;

    [ObservableProperty]
    private HardwareEditModel? _editModel;

    [ObservableProperty]
    private string _editPanelTitle = "Add Hardware";

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    public HardwareViewModel(
        HardwareInstanceService instanceService,
        HardwareDefinitionService definitionService,
        ControllerService controllerService)
    {
        _instanceService = instanceService ?? throw new ArgumentNullException(nameof(instanceService));
        _definitionService = definitionService ?? throw new ArgumentNullException(nameof(definitionService));
        _controllerService = controllerService ?? throw new ArgumentNullException(nameof(controllerService));
    }

    partial void OnSearchTextChanged(string value)
    {
        ApplyFilter();
    }

    partial void OnHardwareInstancesChanged(ObservableCollection<HardwareInstance> value)
    {
        ApplyFilter();
    }

    private void ApplyFilter()
    {
        if (string.IsNullOrWhiteSpace(SearchText))
        {
            FilteredInstances = new ObservableCollection<HardwareInstance>(HardwareInstances);
        }
        else
        {
            var filter = SearchText.Trim();
            var filtered = HardwareInstances
                .Where(h => h.Name.Contains(filter, StringComparison.OrdinalIgnoreCase)
                    || (h.Alias?.Contains(filter, StringComparison.OrdinalIgnoreCase) ?? false)
                    || h.Definition.Type.Contains(filter, StringComparison.OrdinalIgnoreCase))
                .ToList();
            FilteredInstances = new ObservableCollection<HardwareInstance>(filtered);
        }
    }

    [RelayCommand]
    private async Task LoadHardwareAsync()
    {
        IsLoading = true;
        StatusMessage = "Loading hardware...";
        try
        {
            var instances = await _instanceService.GetAllAsync();
            HardwareInstances = new ObservableCollection<HardwareInstance>(instances);

            var definitions = await _definitionService.GetAllAsync();
            HardwareDefinitions = new ObservableCollection<HardwareDefinition>(definitions);

            var controllers = await _controllerService.GetAllAsync();
            Controllers = new ObservableCollection<ControllerEntity>(controllers);

            StatusMessage = $"Loaded {instances.Count} hardware instances";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Failed to load: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private void AddHardware()
    {
        EditModel = new HardwareEditModel();
        EditPanelTitle = "Add Hardware";
        IsEditing = true;
        StatusMessage = "Adding new hardware...";
    }

    [RelayCommand]
    private void EditHardware(HardwareInstance? instance)
    {
        if (instance is null) return;
        EditModel = new HardwareEditModel
        {
            Id = instance.Id,
            Name = instance.Name,
            Alias = instance.Alias,
            Note = instance.Note,
            Port = instance.Port,
            Address = instance.Address,
            DefinitionId = instance.DefinitionId,
            ParametersJson = instance.ParametersJson,
            ControllerId = instance.ControllerId,
            ControllerType = instance.ControllerType,
            IsEnabled = instance.IsEnabled
        };
        EditPanelTitle = $"Edit Hardware (ID: {instance.Id})";
        IsEditing = true;
        StatusMessage = $"Editing {instance.Name}...";
    }

    [RelayCommand]
    private async Task SaveHardwareAsync()
    {
        if (EditModel is null) return;

        if (string.IsNullOrWhiteSpace(EditModel.Name))
        {
            StatusMessage = "Name is required";
            return;
        }

        if (EditModel.DefinitionId == 0)
        {
            StatusMessage = "Please select a hardware definition";
            return;
        }

        IsLoading = true;
        try
        {
            // 同步 ControllerType
            if (EditModel.ControllerId.HasValue && EditModel.ControllerId.Value != 0)
            {
                var controller = Controllers.FirstOrDefault(c => c.Id == EditModel.ControllerId.Value);
                EditModel.ControllerType = controller?.ControllerType;
            }
            else
            {
                EditModel.ControllerType = null;
            }

            if (EditModel.Id == 0)
            {
                var entity = new HardwareInstance
                {
                    Name = EditModel.Name.Trim(),
                    Alias = EditModel.Alias,
                    Note = EditModel.Note,
                    Port = EditModel.Port,
                    Address = EditModel.Address,
                    DefinitionId = EditModel.DefinitionId,
                    ParametersJson = EditModel.ParametersJson,
                    ControllerId = EditModel.ControllerId,
                    ControllerType = EditModel.ControllerType,
                    IsEnabled = EditModel.IsEnabled
                };
                await _instanceService.CreateAsync(entity);
                StatusMessage = $"Hardware '{entity.Name}' created";
            }
            else
            {
                var entity = new HardwareInstance
                {
                    Id = EditModel.Id,
                    Name = EditModel.Name.Trim(),
                    Alias = EditModel.Alias,
                    Note = EditModel.Note,
                    Port = EditModel.Port,
                    Address = EditModel.Address,
                    DefinitionId = EditModel.DefinitionId,
                    ParametersJson = EditModel.ParametersJson,
                    ControllerId = EditModel.ControllerId,
                    ControllerType = EditModel.ControllerType,
                    IsEnabled = EditModel.IsEnabled
                };
                await _instanceService.UpdateAsync(entity);
                StatusMessage = $"Hardware '{entity.Name}' updated";
            }

            IsEditing = false;
            EditModel = null;
            await LoadHardwareAsync();
        }
        catch (Exception ex)
        {
            StatusMessage = $"Failed to save: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private void CancelEdit()
    {
        IsEditing = false;
        EditModel = null;
        StatusMessage = "Edit cancelled";
    }

    [RelayCommand]
    private async Task DeleteHardwareAsync(HardwareInstance? instance)
    {
        if (instance is null) return;

        IsLoading = true;
        try
        {
            await _instanceService.DeleteAsync(instance.Id);
            StatusMessage = $"Hardware '{instance.Name}' deleted";
            await LoadHardwareAsync();
        }
        catch (Exception ex)
        {
            StatusMessage = $"Failed to delete: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }
}

/// <summary>
/// 硬件编辑模型（用于添加/编辑表单）
/// </summary>
public partial class HardwareEditModel : ObservableObject
{
    public long Id { get; set; }

    [ObservableProperty]
    private string _name = string.Empty;

    [ObservableProperty]
    private string? _alias;

    [ObservableProperty]
    private string? _note;

    [ObservableProperty]
    private string? _port;

    [ObservableProperty]
    private string? _address;

    [ObservableProperty]
    private long _definitionId;

    [ObservableProperty]
    private string? _parametersJson;

    [ObservableProperty]
    private long? _controllerId;

    [ObservableProperty]
    private string? _controllerType;

    [ObservableProperty]
    private bool _isEnabled = true;
}
