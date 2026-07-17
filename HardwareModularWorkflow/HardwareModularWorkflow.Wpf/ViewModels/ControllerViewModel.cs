using System.Collections.ObjectModel;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HardwareModularWorkflow.Db.Entities;
using HardwareModularWorkflow.Db.Services;
using HardwareModularWorkflow.Lang;

namespace HardwareModularWorkflow.Wpf.ViewModels;

/// <summary>
/// 控制器配置 ViewModel：控制器列表、添加/编辑、连接参数配置
/// </summary>
public partial class ControllerViewModel : ObservableObject
{
    private readonly ControllerService _controllerService;

    [ObservableProperty]
    private ObservableCollection<ControllerEntity> _controllers = new();

    [ObservableProperty]
    private ObservableCollection<ControllerEntity> _filteredControllers = new();

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private bool _isLoading = false;

    [ObservableProperty]
    private bool _isEditing = false;

    [ObservableProperty]
    private ControllerEditModel? _editModel;

    [ObservableProperty]
    private string _editPanelTitle = LangKeys.Title_AddController;

    [ObservableProperty]
    private string _statusMessage = LangKeys.Status_Ready;

    public ControllerViewModel(ControllerService controllerService)
    {
        _controllerService = controllerService ?? throw new ArgumentNullException(nameof(controllerService));
    }

    partial void OnSearchTextChanged(string value)
    {
        ApplyFilter();
    }

    partial void OnControllersChanged(ObservableCollection<ControllerEntity> value)
    {
        ApplyFilter();
    }

    private void ApplyFilter()
    {
        if (string.IsNullOrWhiteSpace(SearchText))
        {
            FilteredControllers = new ObservableCollection<ControllerEntity>(Controllers);
        }
        else
        {
            var filter = SearchText.Trim();
            var filtered = Controllers
                .Where(c => c.Name.Contains(filter, StringComparison.OrdinalIgnoreCase)
                    || (c.Description?.Contains(filter, StringComparison.OrdinalIgnoreCase) ?? false)
                    || c.ControllerType.Contains(filter, StringComparison.OrdinalIgnoreCase)
                    || c.VendorName.Contains(filter, StringComparison.OrdinalIgnoreCase))
                .ToList();
            FilteredControllers = new ObservableCollection<ControllerEntity>(filtered);
        }
    }

    [RelayCommand]
    private async Task LoadControllersAsync()
    {
        IsLoading = true;
        StatusMessage = LangKeys.Message_LoadingControllers;
        try
        {
            var list = await _controllerService.GetAllAsync();
            Controllers = new ObservableCollection<ControllerEntity>(list);
            StatusMessage = string.Format(LangKeys.Message_LoadedControllers, list.Count);
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
    private void AddController()
    {
        EditModel = new ControllerEditModel();
        EditPanelTitle = LangKeys.Title_AddController;
        IsEditing = true;
        StatusMessage = LangKeys.Message_AddingNewController;
    }

    [RelayCommand]
    private void EditController(ControllerEntity? controller)
    {
        if (controller is null) return;

        string configJson = controller.ConnectionConfigJson;
        if (string.IsNullOrWhiteSpace(configJson) || configJson == "{}")
            configJson = controller.ControllerType switch
            {
                "Plc" => "{\"IpAddress\":\"192.168.1.100\",\"Port\":502}",
                "Can" => "{\"DeviceType\":\"ZCAN_USBCAN_2E_U\",\"BaudRate\":1000000,\"ChannelIndex\":0}",
                _ => "{}"
            };

        EditModel = new ControllerEditModel
        {
            Id = controller.Id,
            Name = controller.Name,
            Description = controller.Description,
            ControllerType = controller.ControllerType,
            VendorName = controller.VendorName,
            ConnectionConfigJson = configJson,
            IsDefault = controller.IsDefault,
            IsEnabled = controller.IsEnabled
        };
        EditPanelTitle = string.Format(LangKeys.Title_EditController, controller.Id);
        IsEditing = true;
        StatusMessage = string.Format(LangKeys.Message_EditingName, controller.Name);
    }

    [RelayCommand]
    private async Task SaveControllerAsync()
    {
        if (EditModel is null) return;

        if (string.IsNullOrWhiteSpace(EditModel.Name))
        {
            StatusMessage = LangKeys.Validation_NameRequired;
            return;
        }

        if (string.IsNullOrWhiteSpace(EditModel.ControllerType))
        {
            StatusMessage = LangKeys.Validation_ControllerTypeRequired;
            return;
        }

        if (string.IsNullOrWhiteSpace(EditModel.VendorName))
        {
            StatusMessage = LangKeys.Validation_VendorNameRequired;
            return;
        }

        // 验证 JSON 格式
        try
        {
            _ = JsonDocument.Parse(EditModel.ConnectionConfigJson ?? "{}");
        }
        catch (JsonException)
        {
            StatusMessage = LangKeys.Validation_InvalidConnectionConfigJson;
            return;
        }

        IsLoading = true;
        try
        {
            if (EditModel.Id == 0)
            {
                var entity = new ControllerEntity
                {
                    Name = EditModel.Name.Trim(),
                    Description = EditModel.Description,
                    ControllerType = EditModel.ControllerType.Trim(),
                    VendorName = EditModel.VendorName.Trim(),
                    ConnectionConfigJson = EditModel.ConnectionConfigJson ?? "{}",
                    IsDefault = EditModel.IsDefault,
                    IsEnabled = EditModel.IsEnabled
                };
                await _controllerService.CreateAsync(entity);
                StatusMessage = string.Format(LangKeys.Message_ControllerCreated, entity.Name);
            }
            else
            {
                var entity = new ControllerEntity
                {
                    Id = EditModel.Id,
                    Name = EditModel.Name.Trim(),
                    Description = EditModel.Description,
                    ControllerType = EditModel.ControllerType.Trim(),
                    VendorName = EditModel.VendorName.Trim(),
                    ConnectionConfigJson = EditModel.ConnectionConfigJson ?? "{}",
                    IsDefault = EditModel.IsDefault,
                    IsEnabled = EditModel.IsEnabled
                };
                await _controllerService.UpdateAsync(entity);
                StatusMessage = string.Format(LangKeys.Message_ControllerUpdated, entity.Name);
            }

            IsEditing = false;
            EditModel = null;
            await LoadControllersAsync();
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
    private async Task DeleteControllerAsync(ControllerEntity? controller)
    {
        if (controller is null) return;

        IsLoading = true;
        try
        {
            await _controllerService.DeleteAsync(controller.Id);
            StatusMessage = string.Format(LangKeys.Message_ControllerDeleted, controller.Name);
            await LoadControllersAsync();
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

    [RelayCommand]
    private void TestConnection(ControllerEntity? controller)
    {
        if (controller is null) return;
        StatusMessage = string.Format(LangKeys.Message_TestingConnectionPlaceholder, controller.Name);
    }
}

/// <summary>
/// 控制器编辑模型
/// </summary>
public partial class ControllerEditModel : ObservableObject
{
    public long Id { get; set; }

    [ObservableProperty]
    private string _name = string.Empty;

    [ObservableProperty]
    private string? _description;

    [ObservableProperty]
    private string _controllerType = string.Empty;

    [ObservableProperty]
    private string _vendorName = string.Empty;

    [ObservableProperty]
    private string _connectionConfigJson = "{}";

    [ObservableProperty]
    private bool _isDefault = false;

    [ObservableProperty]
    private bool _isEnabled = true;
}
