using System.Collections.ObjectModel;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HardwareModularWorkflow.Db.Entities;
using HardwareModularWorkflow.Db.Services;
using HardwareModularWorkflow.Lang;
using Microsoft.EntityFrameworkCore;
using AppDbContext = HardwareModularWorkflow.Db.DbContext.HardwareModularWorkflowDbContext;

namespace HardwareModularWorkflow.Wpf.ViewModels;

/// <summary>
/// 模块编辑器 ViewModel：模块列表、添加/编辑、步骤编排（硬件选择+命令+参数）、排序、串/并行设置
/// </summary>
public partial class ModuleViewModel : ObservableObject
{
    private readonly ModuleService _moduleService;
    private readonly HardwareInstanceService _hardwareInstanceService;
    private readonly AppDbContext _dbContext;

    [ObservableProperty]
    private ObservableCollection<ModuleEntity> _modules = new();

    [ObservableProperty]
    private ObservableCollection<ModuleEntity> _filteredModules = new();

    [ObservableProperty]
    private ObservableCollection<HardwareInstance> _hardwareInstances = new();

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private bool _isLoading = false;

    [ObservableProperty]
    private bool _isEditing = false;

    [ObservableProperty]
    private ModuleEditModel? _editModel;

    [ObservableProperty]
    private ObservableCollection<StepEditModel> _steps = new();

    [ObservableProperty]
    private StepEditModel? _selectedStep;

    [ObservableProperty]
    private ObservableCollection<HardwareCommandDefinition> _availableCommands = new();

    [ObservableProperty]
    private string _editPanelTitle = LangKeys.Title_AddModule;

    [ObservableProperty]
    private string _statusMessage = LangKeys.Status_Ready;

    public ModuleViewModel(
        ModuleService moduleService,
        HardwareInstanceService hardwareInstanceService,
        AppDbContext dbContext)
    {
        _moduleService = moduleService ?? throw new ArgumentNullException(nameof(moduleService));
        _hardwareInstanceService = hardwareInstanceService ?? throw new ArgumentNullException(nameof(hardwareInstanceService));
        _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
    }

    partial void OnSearchTextChanged(string value)
    {
        ApplyFilter();
    }

    partial void OnModulesChanged(ObservableCollection<ModuleEntity> value)
    {
        ApplyFilter();
    }

    partial void OnSelectedStepChanged(StepEditModel? value)
    {
        RefreshAvailableCommands();
    }

    private void ApplyFilter()
    {
        if (string.IsNullOrWhiteSpace(SearchText))
        {
            FilteredModules = new ObservableCollection<ModuleEntity>(Modules);
        }
        else
        {
            var filter = SearchText.Trim();
            var filtered = Modules
                .Where(m => m.Name.Contains(filter, StringComparison.OrdinalIgnoreCase)
                    || (m.Alias?.Contains(filter, StringComparison.OrdinalIgnoreCase) ?? false)
                    || (m.Tags?.Contains(filter, StringComparison.OrdinalIgnoreCase) ?? false))
                .ToList();
            FilteredModules = new ObservableCollection<ModuleEntity>(filtered);
        }
    }

    [RelayCommand]
    private async Task LoadModulesAsync()
    {
        IsLoading = true;
        StatusMessage = LangKeys.Message_LoadingModules;
        try
        {
            var list = await _moduleService.GetAllAsync();
            Modules = new ObservableCollection<ModuleEntity>(list);

            var instances = await _hardwareInstanceService.GetAllAsync();
            HardwareInstances = new ObservableCollection<HardwareInstance>(instances);

            StatusMessage = string.Format(LangKeys.Message_LoadedModules, list.Count);
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
    private void AddModule()
    {
        EditModel = new ModuleEditModel();
        Steps.Clear();
        SelectedStep = null;
        EditPanelTitle = LangKeys.Title_AddModule;
        IsEditing = true;
        StatusMessage = LangKeys.Message_AddingNewModule;
    }

    [RelayCommand]
    private void EditModule(ModuleEntity? module)
    {
        if (module is null) return;

        EditModel = new ModuleEditModel
        {
            Id = module.Id,
            Name = module.Name,
            Alias = module.Alias,
            Note = module.Note,
            Tags = module.Tags,
            DefaultExecutionMode = module.DefaultExecutionMode,
            CoreEvent = module.CoreEvent,
            NotifyEvent = module.NotifyEvent,
            TimeoutMs = module.TimeoutMs,
            ContinueOnFailure = module.ContinueOnFailure
        };

        Steps.Clear();
        if (module.Steps != null)
        {
            foreach (var step in module.Steps.OrderBy(s => s.OrderIndex))
            {
                Steps.Add(new StepEditModel
                {
                    Id = step.Id,
                    Name = step.Name,
                    OrderIndex = step.OrderIndex,
                    ExecutionMode = step.ExecutionMode,
                    HardwareInstanceId = step.HardwareInstanceId,
                    CommandName = step.CommandName,
                    CommandParametersJson = step.CommandParametersJson,
                    IsAsync = step.IsAsync,
                    TimeoutMs = step.TimeoutMs,
                    ContinueOnFailure = step.ContinueOnFailure
                });
                ConfigureStep(Steps[^1]);
            }
        }

        SelectedStep = null;
        EditPanelTitle = string.Format(LangKeys.Title_EditModule, module.Id);
        IsEditing = true;
        StatusMessage = string.Format(LangKeys.Message_EditingName, module.Name);
    }

    [RelayCommand]
    private async Task SaveModuleAsync()
    {
        if (EditModel is null) return;

        if (string.IsNullOrWhiteSpace(EditModel.Name))
        {
            StatusMessage = LangKeys.Validation_ModuleNameRequired;
            return;
        }

        if (!ValidateSteps())
        {
            return;
        }

        IsLoading = true;
        try
        {
            if (EditModel.Id == 0)
            {
                // 创建新模块
                var entity = new ModuleEntity
                {
                    Name = EditModel.Name.Trim(),
                    Alias = EditModel.Alias,
                    Note = EditModel.Note,
                    Tags = EditModel.Tags,
                    DefaultExecutionMode = EditModel.DefaultExecutionMode,
                    CoreEvent = EditModel.CoreEvent,
                    NotifyEvent = EditModel.NotifyEvent,
                    TimeoutMs = EditModel.TimeoutMs,
                    ContinueOnFailure = EditModel.ContinueOnFailure
                };

                int order = 0;
                foreach (var step in Steps)
                {
                    entity.Steps.Add(new ModuleStepEntity
                    {
                        Name = step.Name,
                        OrderIndex = order++,
                        ExecutionMode = step.ExecutionMode,
                        HardwareInstanceId = step.HardwareInstanceId,
                        CommandName = step.CommandName,
                        CommandParametersJson = step.CommandParametersJson,
                        IsAsync = step.IsAsync,
                        TimeoutMs = step.TimeoutMs,
                        ContinueOnFailure = step.ContinueOnFailure
                    });
                }

                _dbContext.Modules.Add(entity);
                await _dbContext.SaveChangesAsync();
                StatusMessage = string.Format(LangKeys.Message_ModuleCreatedWithSteps, entity.Name, Steps.Count);
            }
            else
            {
                // 编辑现有模块 - 使用被追踪的实体
                var entity = await _dbContext.Modules
                    .Include(m => m.Steps)
                    .FirstAsync(m => m.Id == EditModel.Id);

                entity.Name = EditModel.Name.Trim();
                entity.Alias = EditModel.Alias;
                entity.Note = EditModel.Note;
                entity.Tags = EditModel.Tags;
                entity.DefaultExecutionMode = EditModel.DefaultExecutionMode;
                entity.CoreEvent = EditModel.CoreEvent;
                entity.NotifyEvent = EditModel.NotifyEvent;
                entity.TimeoutMs = EditModel.TimeoutMs;
                entity.ContinueOnFailure = EditModel.ContinueOnFailure;

                // 清除现有步骤并重新添加
                _dbContext.ModuleSteps.RemoveRange(entity.Steps);
                entity.Steps.Clear();

                int order = 0;
                foreach (var step in Steps)
                {
                    entity.Steps.Add(new ModuleStepEntity
                    {
                        Name = step.Name,
                        OrderIndex = order++,
                        ExecutionMode = step.ExecutionMode,
                        HardwareInstanceId = step.HardwareInstanceId,
                        CommandName = step.CommandName,
                        CommandParametersJson = step.CommandParametersJson,
                        IsAsync = step.IsAsync,
                        TimeoutMs = step.TimeoutMs,
                        ContinueOnFailure = step.ContinueOnFailure
                    });
                }

                await _dbContext.SaveChangesAsync();
                StatusMessage = string.Format(LangKeys.Message_ModuleUpdatedWithSteps, entity.Name, Steps.Count);
            }

            IsEditing = false;
            EditModel = null;
            Steps.Clear();
            SelectedStep = null;
            await LoadModulesAsync();
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
        Steps.Clear();
        SelectedStep = null;
        StatusMessage = LangKeys.Message_EditCancelled;
    }

    [RelayCommand]
    private async Task DeleteModuleAsync(ModuleEntity? module)
    {
        if (module is null) return;

        IsLoading = true;
        try
        {
            await _moduleService.DeleteAsync(module.Id);
            StatusMessage = string.Format(LangKeys.Message_ModuleDeleted, module.Name);
            await LoadModulesAsync();
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

    // --- 步骤编排 ---

    [RelayCommand]
    private void AddStep()
    {
        var step = new StepEditModel
        {
            Name = string.Format(LangKeys.Format_StepName, Steps.Count + 1),
            OrderIndex = Steps.Count,
            ExecutionMode = EditModel?.DefaultExecutionMode ?? "Sequential",
            IsAsync = true
        };
        ConfigureStep(step);
        Steps.Add(step);
        SelectedStep = step;
        StatusMessage = LangKeys.Message_StepAdded;
    }

    [RelayCommand]
    private void RemoveStep(StepEditModel? step)
    {
        if (step is null) return;
        Steps.Remove(step);
        RecalculateOrderIndices();
        SelectedStep = null;
        StatusMessage = LangKeys.Message_StepRemoved;
    }

    [RelayCommand]
    private void MoveStepUp(StepEditModel? step)
    {
        if (step is null) return;
        var index = Steps.IndexOf(step);
        if (index <= 0) return;
        Steps.Move(index, index - 1);
        RecalculateOrderIndices();
    }

    [RelayCommand]
    private void MoveStepDown(StepEditModel? step)
    {
        if (step is null) return;
        var index = Steps.IndexOf(step);
        if (index < 0 || index >= Steps.Count - 1) return;
        Steps.Move(index, index + 1);
        RecalculateOrderIndices();
    }

    private void RecalculateOrderIndices()
    {
        int order = 0;
        foreach (var step in Steps)
        {
            step.OrderIndex = order++;
        }
    }

    private void ConfigureStep(StepEditModel step)
    {
        step.PropertyChanged += (_, eventArgs) =>
        {
            if (eventArgs.PropertyName == nameof(StepEditModel.HardwareInstanceId)
                && ReferenceEquals(step, SelectedStep))
            {
                RefreshAvailableCommands();
            }
        };
    }

    private void RefreshAvailableCommands()
    {
        if (SelectedStep is null)
        {
            AvailableCommands = new ObservableCollection<HardwareCommandDefinition>();
            return;
        }

        var profile = HardwareInstances
            .FirstOrDefault(instance => instance.Id == SelectedStep.HardwareInstanceId)
            ?.Definition.ControlProfile;
        if (profile is null)
        {
            AvailableCommands = new ObservableCollection<HardwareCommandDefinition>();
            return;
        }

        AvailableCommands = new ObservableCollection<HardwareCommandDefinition>(
            HardwareCommandCatalog.Parse(profile.CommandDefinitionsJson));
        var selectedCommand = AvailableCommands.FirstOrDefault(command =>
            string.Equals(command.Name, SelectedStep.CommandName, StringComparison.OrdinalIgnoreCase));
        if (selectedCommand is null && !string.IsNullOrWhiteSpace(SelectedStep.CommandName))
        {
            SelectedStep.CommandName = string.Empty;
            SelectedStep.CommandParametersJson = null;
            StatusMessage = LangKeys.Message_HardwareProfileDoesNotSupportCommand;
        }
        else if (selectedCommand is not null)
        {
            SelectedStep.IsAsync = selectedCommand.IsAsync;
        }
    }

    private bool ValidateSteps()
    {
        foreach (var step in Steps)
        {
            var profile = HardwareInstances
                .FirstOrDefault(instance => instance.Id == step.HardwareInstanceId)
                ?.Definition.ControlProfile;
            if (profile is null)
            {
                StatusMessage = string.Format(LangKeys.Validation_StepRequiresHardwareInstanceWithProfile, step.Name);
                return false;
            }

            Dictionary<string, object>? parameters = null;
            if (!string.IsNullOrWhiteSpace(step.CommandParametersJson))
            {
                try
                {
                    parameters = JsonSerializer.Deserialize<Dictionary<string, object>>(
                        step.CommandParametersJson);
                }
                catch (JsonException)
                {
                    StatusMessage = string.Format(LangKeys.Validation_StepInvalidCommandParametersJson, step.Name);
                    return false;
                }
            }

            try
            {
                HardwareCommandCatalog.Validate(profile, step.CommandName, parameters);
            }
            catch (InvalidOperationException ex)
            {
                StatusMessage = string.Format(LangKeys.Format_StepValidationError, step.Name, ex.Message);
                return false;
            }
        }

        return true;
    }
}

/// <summary>
/// 模块编辑模型
/// </summary>
public partial class ModuleEditModel : ObservableObject
{
    public long Id { get; set; }

    [ObservableProperty]
    private string _name = string.Empty;

    [ObservableProperty]
    private string? _alias;

    [ObservableProperty]
    private string? _note;

    [ObservableProperty]
    private string? _tags;

    [ObservableProperty]
    private string _defaultExecutionMode = "Sequential";

    [ObservableProperty]
    private string? _coreEvent;

    [ObservableProperty]
    private string? _notifyEvent;

    [ObservableProperty]
    private int? _timeoutMs;

    [ObservableProperty]
    private bool _continueOnFailure = false;
}

/// <summary>
/// 步骤编辑模型（用于模块内步骤编排）
/// </summary>
public partial class StepEditModel : ObservableObject
{
    public long Id { get; set; }

    [ObservableProperty]
    private string _name = string.Empty;

    [ObservableProperty]
    private int _orderIndex;

    [ObservableProperty]
    private string _executionMode = "Sequential";

    [ObservableProperty]
    private long _hardwareInstanceId;

    [ObservableProperty]
    private string _commandName = string.Empty;

    [ObservableProperty]
    private string? _commandParametersJson;

    [ObservableProperty]
    private bool _isAsync = true;

    [ObservableProperty]
    private int? _timeoutMs;

    [ObservableProperty]
    private bool _continueOnFailure = false;
}
