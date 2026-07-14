using System.Collections.ObjectModel;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HardwareModularWorkflow.Db.Entities;
using HardwareModularWorkflow.Db.Services;
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
    private string _editPanelTitle = "Add Module";

    [ObservableProperty]
    private string _statusMessage = string.Empty;

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
        StatusMessage = "Loading modules...";
        try
        {
            var list = await _moduleService.GetAllAsync();
            Modules = new ObservableCollection<ModuleEntity>(list);

            var instances = await _hardwareInstanceService.GetAllAsync();
            HardwareInstances = new ObservableCollection<HardwareInstance>(instances);

            StatusMessage = $"Loaded {list.Count} modules";
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
    private void AddModule()
    {
        EditModel = new ModuleEditModel();
        Steps.Clear();
        SelectedStep = null;
        EditPanelTitle = "Add Module";
        IsEditing = true;
        StatusMessage = "Adding new module...";
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
            }
        }

        SelectedStep = null;
        EditPanelTitle = $"Edit Module (ID: {module.Id})";
        IsEditing = true;
        StatusMessage = $"Editing {module.Name}...";
    }

    [RelayCommand]
    private async Task SaveModuleAsync()
    {
        if (EditModel is null) return;

        if (string.IsNullOrWhiteSpace(EditModel.Name))
        {
            StatusMessage = "Module name is required";
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
                StatusMessage = $"Module '{entity.Name}' created with {Steps.Count} steps";
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
                StatusMessage = $"Module '{entity.Name}' updated with {Steps.Count} steps";
            }

            IsEditing = false;
            EditModel = null;
            Steps.Clear();
            SelectedStep = null;
            await LoadModulesAsync();
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
        Steps.Clear();
        SelectedStep = null;
        StatusMessage = "Edit cancelled";
    }

    [RelayCommand]
    private async Task DeleteModuleAsync(ModuleEntity? module)
    {
        if (module is null) return;

        IsLoading = true;
        try
        {
            await _moduleService.DeleteAsync(module.Id);
            StatusMessage = $"Module '{module.Name}' deleted";
            await LoadModulesAsync();
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

    // --- 步骤编排 ---

    [RelayCommand]
    private void AddStep()
    {
        var step = new StepEditModel
        {
            Name = $"Step {Steps.Count + 1}",
            OrderIndex = Steps.Count,
            ExecutionMode = EditModel?.DefaultExecutionMode ?? "Sequential",
            IsAsync = true
        };
        Steps.Add(step);
        SelectedStep = step;
        StatusMessage = "Step added";
    }

    [RelayCommand]
    private void RemoveStep(StepEditModel? step)
    {
        if (step is null) return;
        Steps.Remove(step);
        RecalculateOrderIndices();
        SelectedStep = null;
        StatusMessage = "Step removed";
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
