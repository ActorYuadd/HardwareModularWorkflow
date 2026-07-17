using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HardwareModularWorkflow.Db.Entities;
using HardwareModularWorkflow.Db.Services;
using HardwareModularWorkflow.Lang;

namespace HardwareModularWorkflow.Wpf.ViewModels;

/// <summary>
/// 硬件管理 ViewModel：硬件列表、添加/编辑、控制器绑定
/// </summary>
public partial class HardwareViewModel : ObservableObject
{
    private readonly HardwareInstanceService _instanceService;
    private readonly HardwareDefinitionService _definitionService;
    private readonly HardwareCatalogService _catalogService;
    private readonly ControllerService _controllerService;

    [ObservableProperty]
    private ObservableCollection<HardwareInstance> _hardwareInstances = new();

    [ObservableProperty]
    private ObservableCollection<HardwareInstance> _filteredInstances = new();

    [ObservableProperty]
    private ObservableCollection<HardwareDefinition> _hardwareDefinitions = new();

    [ObservableProperty]
    private ObservableCollection<HardwareDefinition> _availableDefinitions = new();

    [ObservableProperty]
    private ObservableCollection<HardwareCategory> _hardwareCategories = new();

    [ObservableProperty]
    private ObservableCollection<ControllerEntity> _controllers = new();

    [ObservableProperty]
    private ObservableCollection<ControllerEntity> _compatibleControllers = new();

    [ObservableProperty]
    private long? _selectedCategoryId;

    [ObservableProperty]
    private long _selectedDefinitionId;

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private bool _isLoading = false;

    [ObservableProperty]
    private bool _isEditing = false;

    [ObservableProperty]
    private HardwareEditModel? _editModel;

    [ObservableProperty]
    private string _editPanelTitle = LangKeys.Title_AddHardware;

    [ObservableProperty]
    private string _statusMessage = LangKeys.Status_Ready;

    public HardwareViewModel(
        HardwareInstanceService instanceService,
        HardwareDefinitionService definitionService,
        HardwareCatalogService catalogService,
        ControllerService controllerService)
    {
        _instanceService = instanceService ?? throw new ArgumentNullException(nameof(instanceService));
        _definitionService = definitionService ?? throw new ArgumentNullException(nameof(definitionService));
        _catalogService = catalogService ?? throw new ArgumentNullException(nameof(catalogService));
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
                    || (h.Definition.Category?.Name.Contains(filter, StringComparison.OrdinalIgnoreCase) ?? false)
                    || (h.Definition.ControlProfile?.Name.Contains(filter, StringComparison.OrdinalIgnoreCase) ?? false))
                .ToList();
            FilteredInstances = new ObservableCollection<HardwareInstance>(filtered);
        }
    }

    partial void OnSelectedCategoryIdChanged(long? value)
    {
        AvailableDefinitions = new ObservableCollection<HardwareDefinition>(
            HardwareDefinitions.Where(definition => definition.CategoryId == value));

        if (SelectedDefinitionId != 0
            && !AvailableDefinitions.Any(definition => definition.Id == SelectedDefinitionId))
        {
            SelectedDefinitionId = 0;
        }
    }

    partial void OnSelectedDefinitionIdChanged(long value)
    {
        if (EditModel is not null)
        {
            EditModel.DefinitionId = value;
        }

        var definition = HardwareDefinitions.FirstOrDefault(item => item.Id == value);
        var requiredControllerType = definition?.ControlProfile?.RequiredControllerType;
        CompatibleControllers = new ObservableCollection<ControllerEntity>(
            Controllers.Where(controller =>
                controller.IsEnabled
                && (string.IsNullOrWhiteSpace(requiredControllerType)
                    || string.Equals(
                        controller.ControllerType,
                        requiredControllerType,
                        StringComparison.OrdinalIgnoreCase))));

        if (EditModel?.ControllerId is long controllerId
            && !CompatibleControllers.Any(controller => controller.Id == controllerId))
        {
            EditModel.ControllerId = null;
        }
    }

    [RelayCommand]
    private async Task LoadHardwareAsync()
    {
        IsLoading = true;
        StatusMessage = LangKeys.Message_LoadingHardware;
        try
        {
            var instances = await _instanceService.GetAllAsync();
            HardwareInstances = new ObservableCollection<HardwareInstance>(instances);

            var definitions = await _definitionService.GetAllAsync();
            HardwareDefinitions = new ObservableCollection<HardwareDefinition>(definitions);

            var categories = await _catalogService.GetCategoriesAsync();
            HardwareCategories = new ObservableCollection<HardwareCategory>(categories);
            OnSelectedCategoryIdChanged(SelectedCategoryId);

            var controllers = await _controllerService.GetAllAsync();
            Controllers = new ObservableCollection<ControllerEntity>(controllers);
            OnSelectedDefinitionIdChanged(SelectedDefinitionId);

            StatusMessage = string.Format(LangKeys.Message_LoadedHardwareInstances, instances.Count);
        }
        catch (Exception ex)
        {
            StatusMessage = string.Format(LangKeys.Message_FailedToLoad, ex.Message);
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
        SelectedCategoryId = null;
        SelectedDefinitionId = 0;
        AvailableDefinitions = new ObservableCollection<HardwareDefinition>();
        CompatibleControllers = new ObservableCollection<ControllerEntity>(
            Controllers.Where(controller => controller.IsEnabled));
        EditPanelTitle = LangKeys.Title_AddHardware;
        IsEditing = true;
        StatusMessage = LangKeys.Message_AddingNewHardware;
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
        SelectedCategoryId = instance.Definition.CategoryId;
        SelectedDefinitionId = instance.DefinitionId;
        EditPanelTitle = string.Format(LangKeys.Title_EditHardware, instance.Id);
        IsEditing = true;
        StatusMessage = string.Format(LangKeys.Message_EditingName, instance.Name);
    }

    [RelayCommand]
    private async Task SaveHardwareAsync()
    {
        if (EditModel is null) return;

        if (string.IsNullOrWhiteSpace(EditModel.Name))
        {
            StatusMessage = LangKeys.Validation_NameRequired;
            return;
        }

        if (EditModel.DefinitionId == 0)
        {
            StatusMessage = LangKeys.Validation_HardwareDefinitionRequired;
            return;
        }

        IsLoading = true;
        try
        {
            var definition = HardwareDefinitions.FirstOrDefault(
                item => item.Id == EditModel.DefinitionId);
            if (definition is null)
            {
                StatusMessage = LangKeys.Message_HardwareDefinitionUnavailable;
                return;
            }

            // 同步 ControllerType
            if (EditModel.ControllerId.HasValue && EditModel.ControllerId.Value != 0)
            {
                var controller = Controllers.FirstOrDefault(c => c.Id == EditModel.ControllerId.Value);
                var requiredControllerType = definition.ControlProfile?.RequiredControllerType;
                if (controller is null
                    || (!string.IsNullOrWhiteSpace(requiredControllerType)
                        && !string.Equals(
                            controller.ControllerType,
                            requiredControllerType,
                            StringComparison.OrdinalIgnoreCase)))
                {
                    StatusMessage = LangKeys.Message_ControllerNotCompatible;
                    return;
                }

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
                StatusMessage = string.Format(LangKeys.Message_HardwareCreated, entity.Name);
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
                StatusMessage = string.Format(LangKeys.Message_HardwareUpdated, entity.Name);
            }

            IsEditing = false;
            EditModel = null;
            await LoadHardwareAsync();
        }
        catch (Exception ex)
        {
            StatusMessage = string.Format(LangKeys.Message_FailedToSave, ex.Message);
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
        StatusMessage = LangKeys.Message_EditCancelled;
    }

    [RelayCommand]
    private async Task DeleteHardwareAsync(HardwareInstance? instance)
    {
        if (instance is null) return;

        IsLoading = true;
        try
        {
            await _instanceService.DeleteAsync(instance.Id);
            StatusMessage = string.Format(LangKeys.Message_HardwareDeleted, instance.Name);
            await LoadHardwareAsync();
        }
        catch (Exception ex)
        {
            StatusMessage = string.Format(LangKeys.Message_FailedToDelete, ex.Message);
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
