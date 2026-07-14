using System.Collections.ObjectModel;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HardwareModularWorkflow.Db.Entities;
using HardwareModularWorkflow.Db.Services;

namespace HardwareModularWorkflow.Wpf.ViewModels;

/// <summary>
/// Hardware definition ViewModel: hardware template listing and editing.
/// </summary>
public partial class HardwareDefinitionViewModel : ObservableObject
{
    private readonly HardwareDefinitionService _definitionService;
    private readonly HardwareCatalogService _catalogService;

    [ObservableProperty]
    private ObservableCollection<HardwareDefinition> _hardwareDefinitions = new();

    [ObservableProperty]
    private ObservableCollection<HardwareDefinition> _filteredDefinitions = new();

    [ObservableProperty]
    private ObservableCollection<HardwareCategory> _hardwareCategories = new();

    [ObservableProperty]
    private ObservableCollection<HardwareControlProfile> _controlProfiles = new();

    [ObservableProperty]
    private ObservableCollection<HardwareControlProfile> _availableControlProfiles = new();

    [ObservableProperty]
    private long? _selectedCategoryId;

    [ObservableProperty]
    private long? _selectedControlProfileId;

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private bool _isEditing;

    [ObservableProperty]
    private HardwareDefinitionEditModel? _editModel;

    [ObservableProperty]
    private string _editPanelTitle = "Add Hardware Definition";

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    public HardwareDefinitionViewModel(
        HardwareDefinitionService definitionService,
        HardwareCatalogService catalogService)
    {
        _definitionService = definitionService
            ?? throw new ArgumentNullException(nameof(definitionService));
        _catalogService = catalogService
            ?? throw new ArgumentNullException(nameof(catalogService));
    }

    partial void OnSearchTextChanged(string value) => ApplyFilter();

    partial void OnHardwareDefinitionsChanged(
        ObservableCollection<HardwareDefinition> value) => ApplyFilter();

    private void ApplyFilter()
    {
        if (string.IsNullOrWhiteSpace(SearchText))
        {
            FilteredDefinitions =
                new ObservableCollection<HardwareDefinition>(HardwareDefinitions);
            return;
        }

        var filter = SearchText.Trim();
        var filtered = HardwareDefinitions
            .Where(definition =>
                definition.Name.Contains(filter, StringComparison.OrdinalIgnoreCase)
                || (definition.Alias?.Contains(filter, StringComparison.OrdinalIgnoreCase) ?? false)
                || (definition.Category?.Name.Contains(filter, StringComparison.OrdinalIgnoreCase) ?? false)
                || (definition.ControlProfile?.Name.Contains(filter, StringComparison.OrdinalIgnoreCase) ?? false))
            .ToList();
        FilteredDefinitions = new ObservableCollection<HardwareDefinition>(filtered);
    }

    partial void OnSelectedCategoryIdChanged(long? value)
    {
        AvailableControlProfiles = new ObservableCollection<HardwareControlProfile>(
            ControlProfiles.Where(profile => profile.CategoryId == value));

        if (SelectedControlProfileId.HasValue
            && !AvailableControlProfiles.Any(profile => profile.Id == SelectedControlProfileId.Value))
        {
            SelectedControlProfileId = null;
        }
    }

    partial void OnSelectedControlProfileIdChanged(long? value)
    {
        if (EditModel is not null)
        {
            EditModel.CategoryId = SelectedCategoryId;
            EditModel.ControlProfileId = value;
        }
    }

    [RelayCommand]
    private async Task LoadDefinitionsAsync()
    {
        IsLoading = true;
        StatusMessage = "Loading hardware definitions...";
        try
        {
            var categories = await _catalogService.GetCategoriesAsync();
            HardwareCategories = new ObservableCollection<HardwareCategory>(categories);

            var profiles = await _catalogService.GetProfilesAsync();
            ControlProfiles = new ObservableCollection<HardwareControlProfile>(profiles);
            OnSelectedCategoryIdChanged(SelectedCategoryId);

            var definitions = await _definitionService.GetAllAsync();
            HardwareDefinitions =
                new ObservableCollection<HardwareDefinition>(definitions);
            StatusMessage = $"Loaded {definitions.Count} hardware definitions";
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
    private void AddDefinition()
    {
        EditModel = new HardwareDefinitionEditModel();
        SelectedCategoryId = null;
        SelectedControlProfileId = null;
        AvailableControlProfiles = new ObservableCollection<HardwareControlProfile>();
        EditPanelTitle = "Add Hardware Definition";
        IsEditing = true;
        StatusMessage = "Adding hardware definition...";
    }

    [RelayCommand]
    private void EditDefinition(HardwareDefinition? definition)
    {
        if (definition is null)
        {
            return;
        }

        EditModel = new HardwareDefinitionEditModel
        {
            Id = definition.Id,
            Name = definition.Name,
            Alias = definition.Alias,
            Note = definition.Note,
            CategoryId = definition.CategoryId,
            ControlProfileId = definition.ControlProfileId,
            CustomSchemaJson = definition.CustomSchemaJson,
            DefaultParametersJson = definition.DefaultParametersJson,
            IsSystem = definition.IsSystem
        };
        SelectedCategoryId = definition.CategoryId;
        SelectedControlProfileId = definition.ControlProfileId;
        EditPanelTitle = $"Edit Hardware Definition (ID: {definition.Id})";
        IsEditing = true;
        StatusMessage = $"Editing {definition.Name}...";
    }

    [RelayCommand]
    private async Task SaveDefinitionAsync()
    {
        if (EditModel is null)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(EditModel.Name))
        {
            StatusMessage = "Name is required";
            return;
        }

        if (!SelectedCategoryId.HasValue)
        {
            StatusMessage = "Hardware category is required";
            return;
        }

        if (!SelectedControlProfileId.HasValue)
        {
            StatusMessage = "Control profile is required";
            return;
        }

        if (!IsValidJson(EditModel.CustomSchemaJson, "custom schema")
            || !IsValidJson(EditModel.DefaultParametersJson, "default parameters"))
        {
            return;
        }

        IsLoading = true;
        try
        {
            var category = HardwareCategories.FirstOrDefault(
                item => item.Id == SelectedCategoryId.Value);
            if (category is null)
            {
                StatusMessage = "The selected hardware category is unavailable";
                return;
            }

            var entity = new HardwareDefinition
            {
                Id = EditModel.Id,
                Name = EditModel.Name.Trim(),
                Alias = EditModel.Alias,
                Note = EditModel.Note,
                Type = category.Code,
                CategoryId = SelectedCategoryId,
                ControlProfileId = SelectedControlProfileId,
                CustomSchemaJson = NormalizeJson(EditModel.CustomSchemaJson),
                DefaultParametersJson = NormalizeJson(EditModel.DefaultParametersJson),
                IsSystem = EditModel.IsSystem
            };

            if (entity.Id == 0)
            {
                await _definitionService.CreateAsync(entity);
                StatusMessage = $"Hardware definition '{entity.Name}' created";
            }
            else
            {
                await _definitionService.UpdateAsync(entity);
                StatusMessage = $"Hardware definition '{entity.Name}' updated";
            }

            IsEditing = false;
            EditModel = null;
            await LoadDefinitionsAsync();
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
    private async Task DeleteDefinitionAsync(HardwareDefinition? definition)
    {
        if (definition is null)
        {
            return;
        }

        IsLoading = true;
        try
        {
            await _definitionService.DeleteAsync(definition.Id);
            StatusMessage = $"Hardware definition '{definition.Name}' deleted";
            await LoadDefinitionsAsync();
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

    private bool IsValidJson(string? json, string fieldName)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return true;
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            return true;
        }
        catch (JsonException)
        {
            StatusMessage = $"Invalid JSON in {fieldName}";
            return false;
        }
    }

    private static string? NormalizeJson(string? json) =>
        string.IsNullOrWhiteSpace(json) ? null : json.Trim();
}

/// <summary>
/// Hardware definition editing model.
/// </summary>
public partial class HardwareDefinitionEditModel : ObservableObject
{
    public long Id { get; set; }

    [ObservableProperty]
    private string _name = string.Empty;

    [ObservableProperty]
    private string? _alias;

    [ObservableProperty]
    private string? _note;

    [ObservableProperty]
    private long? _categoryId;

    [ObservableProperty]
    private long? _controlProfileId;

    [ObservableProperty]
    private string? _customSchemaJson;

    [ObservableProperty]
    private string? _defaultParametersJson;

    [ObservableProperty]
    private bool _isSystem;
}
