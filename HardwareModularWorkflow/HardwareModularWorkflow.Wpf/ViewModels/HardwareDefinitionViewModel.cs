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

    [ObservableProperty]
    private ObservableCollection<HardwareDefinition> _hardwareDefinitions = new();

    [ObservableProperty]
    private ObservableCollection<HardwareDefinition> _filteredDefinitions = new();

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

    public HardwareDefinitionViewModel(HardwareDefinitionService definitionService)
    {
        _definitionService = definitionService
            ?? throw new ArgumentNullException(nameof(definitionService));
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
                || definition.Type.Contains(filter, StringComparison.OrdinalIgnoreCase))
            .ToList();
        FilteredDefinitions = new ObservableCollection<HardwareDefinition>(filtered);
    }

    [RelayCommand]
    private async Task LoadDefinitionsAsync()
    {
        IsLoading = true;
        StatusMessage = "Loading hardware definitions...";
        try
        {
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
            Type = definition.Type,
            CustomSchemaJson = definition.CustomSchemaJson,
            DefaultParametersJson = definition.DefaultParametersJson,
            IsSystem = definition.IsSystem
        };
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

        if (string.IsNullOrWhiteSpace(EditModel.Type))
        {
            StatusMessage = "Hardware type is required";
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
            var entity = new HardwareDefinition
            {
                Id = EditModel.Id,
                Name = EditModel.Name.Trim(),
                Alias = EditModel.Alias,
                Note = EditModel.Note,
                Type = EditModel.Type.Trim(),
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
    private string _type = "Motor";

    [ObservableProperty]
    private string? _customSchemaJson;

    [ObservableProperty]
    private string? _defaultParametersJson;

    [ObservableProperty]
    private bool _isSystem;
}
