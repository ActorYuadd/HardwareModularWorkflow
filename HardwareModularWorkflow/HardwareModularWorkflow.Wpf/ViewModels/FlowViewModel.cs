using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HardwareModularWorkflow.Db.Entities;
using HardwareModularWorkflow.Db.Services;
using Microsoft.EntityFrameworkCore;
using AppDbContext = HardwareModularWorkflow.Db.DbContext.HardwareModularWorkflowDbContext;

namespace HardwareModularWorkflow.Wpf.ViewModels;

/// <summary>
/// 工作流编辑器 ViewModel：工作流列表、模块/子流编排、排序、串/并行、循环检测
/// </summary>
public partial class FlowViewModel : ObservableObject
{
    private readonly FlowService _flowService;
    private readonly ModuleService _moduleService;
    private readonly AppDbContext _dbContext;

    [ObservableProperty]
    private ObservableCollection<FlowEntity> _flows = new();

    [ObservableProperty]
    private ObservableCollection<FlowEntity> _filteredFlows = new();

    [ObservableProperty]
    private ObservableCollection<ModuleEntity> _availableModules = new();

    [ObservableProperty]
    private ObservableCollection<FlowEntity> _availableSubFlows = new();

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private bool _isLoading = false;

    [ObservableProperty]
    private bool _isEditing = false;

    [ObservableProperty]
    private FlowEditModel? _editModel;

    [ObservableProperty]
    private ObservableCollection<FlowNodeEditModel> _flowNodes = new();

    [ObservableProperty]
    private FlowNodeEditModel? _selectedFlowNode;

    [ObservableProperty]
    private ModuleEntity? _selectedModuleToAdd;

    [ObservableProperty]
    private FlowEntity? _selectedSubFlowToAdd;

    [ObservableProperty]
    private string _editPanelTitle = "Add Workflow";

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    [ObservableProperty]
    private string _cycleWarning = string.Empty;

    public FlowViewModel(
        FlowService flowService,
        ModuleService moduleService,
        AppDbContext dbContext)
    {
        _flowService = flowService ?? throw new ArgumentNullException(nameof(flowService));
        _moduleService = moduleService ?? throw new ArgumentNullException(nameof(moduleService));
        _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
    }

    partial void OnSearchTextChanged(string value)
    {
        ApplyFilter();
    }

    partial void OnFlowsChanged(ObservableCollection<FlowEntity> value)
    {
        ApplyFilter();
    }

    private void ApplyFilter()
    {
        if (string.IsNullOrWhiteSpace(SearchText))
        {
            FilteredFlows = new ObservableCollection<FlowEntity>(Flows);
        }
        else
        {
            var filter = SearchText.Trim();
            var filtered = Flows
                .Where(f => f.Name.Contains(filter, StringComparison.OrdinalIgnoreCase)
                    || (f.Alias?.Contains(filter, StringComparison.OrdinalIgnoreCase) ?? false)
                    || (f.Tags?.Contains(filter, StringComparison.OrdinalIgnoreCase) ?? false))
                .ToList();
            FilteredFlows = new ObservableCollection<FlowEntity>(filtered);
        }
    }

    [RelayCommand]
    private async Task LoadFlowsAsync()
    {
        IsLoading = true;
        StatusMessage = "Loading workflows...";
        try
        {
            var list = await _flowService.GetAllAsync();
            Flows = new ObservableCollection<FlowEntity>(list);

            var modules = await _moduleService.GetAllAsync();
            AvailableModules = new ObservableCollection<ModuleEntity>(modules);

            var subFlows = await _flowService.GetReusableFlowsAsync();
            AvailableSubFlows = new ObservableCollection<FlowEntity>(subFlows);

            StatusMessage = $"Loaded {list.Count} workflows";
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
    private void AddFlow()
    {
        EditModel = new FlowEditModel();
        FlowNodes.Clear();
        SelectedFlowNode = null;
        SelectedModuleToAdd = null;
        SelectedSubFlowToAdd = null;
        EditPanelTitle = "Add Workflow";
        CycleWarning = string.Empty;
        IsEditing = true;
        StatusMessage = "Adding new workflow...";
    }

    [RelayCommand]
    private void EditFlow(FlowEntity? flow)
    {
        if (flow is null) return;

        EditModel = new FlowEditModel
        {
            Id = flow.Id,
            Name = flow.Name,
            Alias = flow.Alias,
            Note = flow.Note,
            Tags = flow.Tags,
            ExecutionMode = flow.ExecutionMode,
            CoreEvent = flow.CoreEvent,
            NotifyEvent = flow.NotifyEvent,
            TimeoutMs = flow.TimeoutMs,
            ContinueOnFailure = flow.ContinueOnFailure,
            IsReusable = flow.IsReusable
        };

        FlowNodes.Clear();
        if (flow.ModuleRelations != null)
        {
            foreach (var relation in flow.ModuleRelations.OrderBy(r => r.OrderIndex))
            {
                FlowNodes.Add(new FlowNodeEditModel
                {
                    Id = relation.Id,
                    OrderIndex = relation.OrderIndex,
                    Name = relation.Module?.Name ?? $"Module #{relation.ModuleId}",
                    NodeType = "Module",
                    ReferenceId = relation.ModuleId,
                    ExecutionMode = relation.ExecutionMode,
                    Condition = relation.Condition
                });
            }
        }
        if (flow.SubFlowReferences != null)
        {
            foreach (var reference in flow.SubFlowReferences.OrderBy(r => r.OrderIndex))
            {
                FlowNodes.Add(new FlowNodeEditModel
                {
                    Id = reference.Id,
                    OrderIndex = reference.OrderIndex,
                    Name = reference.SubFlow?.Name ?? $"Flow #{reference.SubFlowId}",
                    NodeType = "SubFlow",
                    ReferenceId = reference.SubFlowId,
                    ExecutionMode = reference.ExecutionMode,
                    Condition = reference.Condition
                });
            }
        }
        RecalculateOrderIndices();

        SelectedFlowNode = null;
        SelectedModuleToAdd = null;
        SelectedSubFlowToAdd = null;
        EditPanelTitle = $"Edit Workflow (ID: {flow.Id})";
        CycleWarning = string.Empty;
        IsEditing = true;
        StatusMessage = $"Editing {flow.Name}...";
    }

    [RelayCommand]
    private async Task SaveFlowAsync()
    {
        if (EditModel is null) return;

        if (string.IsNullOrWhiteSpace(EditModel.Name))
        {
            StatusMessage = "Workflow name is required";
            return;
        }

        // 循环检测
        if (EditModel.Id != 0)
        {
            var cycleNodes = FlowNodes.Where(n => n.NodeType == "SubFlow").ToList();
            foreach (var node in cycleNodes)
            {
                if (node.ReferenceId == EditModel.Id)
                {
                    StatusMessage = "Cannot reference itself as a sub-flow";
                    return;
                }
                var visited = new HashSet<long>();
                if (await HasCircularReferenceAsync(EditModel.Id, node.ReferenceId, visited))
                {
                    StatusMessage = $"Circular reference detected through sub-flow '{node.Name}'";
                    return;
                }
            }
        }

        IsLoading = true;
        try
        {
            if (EditModel.Id == 0)
            {
                // 创建新工作流
                var entity = new FlowEntity
                {
                    Name = EditModel.Name.Trim(),
                    Alias = EditModel.Alias,
                    Note = EditModel.Note,
                    Tags = EditModel.Tags,
                    ExecutionMode = EditModel.ExecutionMode,
                    CoreEvent = EditModel.CoreEvent,
                    NotifyEvent = EditModel.NotifyEvent,
                    TimeoutMs = EditModel.TimeoutMs,
                    ContinueOnFailure = EditModel.ContinueOnFailure,
                    IsReusable = EditModel.IsReusable
                };

                foreach (var node in FlowNodes.OrderBy(n => n.OrderIndex))
                {
                    if (node.NodeType == "Module")
                    {
                        entity.ModuleRelations.Add(new FlowModuleRelation
                        {
                            ModuleId = node.ReferenceId,
                            OrderIndex = node.OrderIndex,
                            ExecutionMode = node.ExecutionMode,
                            Condition = node.Condition
                        });
                    }
                    else if (node.NodeType == "SubFlow")
                    {
                        entity.SubFlowReferences.Add(new FlowSubFlowReference
                        {
                            SubFlowId = node.ReferenceId,
                            OrderIndex = node.OrderIndex,
                            ExecutionMode = node.ExecutionMode,
                            Condition = node.Condition
                        });
                    }
                }

                _dbContext.Flows.Add(entity);
                await _dbContext.SaveChangesAsync();
                StatusMessage = $"Workflow '{entity.Name}' created with {FlowNodes.Count} nodes";
            }
            else
            {
                // 编辑现有工作流
                var entity = await _dbContext.Flows
                    .Include(f => f.ModuleRelations)
                    .Include(f => f.SubFlowReferences)
                    .FirstAsync(f => f.Id == EditModel.Id);

                entity.Name = EditModel.Name.Trim();
                entity.Alias = EditModel.Alias;
                entity.Note = EditModel.Note;
                entity.Tags = EditModel.Tags;
                entity.ExecutionMode = EditModel.ExecutionMode;
                entity.CoreEvent = EditModel.CoreEvent;
                entity.NotifyEvent = EditModel.NotifyEvent;
                entity.TimeoutMs = EditModel.TimeoutMs;
                entity.ContinueOnFailure = EditModel.ContinueOnFailure;
                entity.IsReusable = EditModel.IsReusable;

                // 清除旧关联
                _dbContext.FlowModuleRelations.RemoveRange(entity.ModuleRelations);
                entity.ModuleRelations.Clear();
                _dbContext.FlowSubFlowReferences.RemoveRange(entity.SubFlowReferences);
                entity.SubFlowReferences.Clear();

                foreach (var node in FlowNodes.OrderBy(n => n.OrderIndex))
                {
                    if (node.NodeType == "Module")
                    {
                        entity.ModuleRelations.Add(new FlowModuleRelation
                        {
                            ModuleId = node.ReferenceId,
                            OrderIndex = node.OrderIndex,
                            ExecutionMode = node.ExecutionMode,
                            Condition = node.Condition
                        });
                    }
                    else if (node.NodeType == "SubFlow")
                    {
                        entity.SubFlowReferences.Add(new FlowSubFlowReference
                        {
                            SubFlowId = node.ReferenceId,
                            OrderIndex = node.OrderIndex,
                            ExecutionMode = node.ExecutionMode,
                            Condition = node.Condition
                        });
                    }
                }

                await _dbContext.SaveChangesAsync();
                StatusMessage = $"Workflow '{entity.Name}' updated with {FlowNodes.Count} nodes";
            }

            IsEditing = false;
            EditModel = null;
            FlowNodes.Clear();
            SelectedFlowNode = null;
            SelectedModuleToAdd = null;
            SelectedSubFlowToAdd = null;
            CycleWarning = string.Empty;
            await LoadFlowsAsync();
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
        FlowNodes.Clear();
        SelectedFlowNode = null;
        SelectedModuleToAdd = null;
        SelectedSubFlowToAdd = null;
        CycleWarning = string.Empty;
        StatusMessage = "Edit cancelled";
    }

    [RelayCommand]
    private async Task DeleteFlowAsync(FlowEntity? flow)
    {
        if (flow is null) return;

        IsLoading = true;
        try
        {
            await _flowService.DeleteAsync(flow.Id);
            StatusMessage = $"Workflow '{flow.Name}' deleted";
            await LoadFlowsAsync();
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

    // --- 节点编排 ---

    [RelayCommand]
    private void AddModuleNode()
    {
        if (SelectedModuleToAdd is null) return;
        var node = new FlowNodeEditModel
        {
            OrderIndex = FlowNodes.Count,
            Name = SelectedModuleToAdd.Name,
            NodeType = "Module",
            ReferenceId = SelectedModuleToAdd.Id,
            ExecutionMode = EditModel?.ExecutionMode
        };
        FlowNodes.Add(node);
        SelectedModuleToAdd = null;
        StatusMessage = $"Module '{node.Name}' added to workflow";
    }

    [RelayCommand]
    private void AddSubFlowNode()
    {
        if (SelectedSubFlowToAdd is null) return;
        if (EditModel?.Id != 0 && SelectedSubFlowToAdd.Id == EditModel?.Id)
        {
            StatusMessage = "Cannot reference the workflow itself";
            return;
        }
        var node = new FlowNodeEditModel
        {
            OrderIndex = FlowNodes.Count,
            Name = SelectedSubFlowToAdd.Name,
            NodeType = "SubFlow",
            ReferenceId = SelectedSubFlowToAdd.Id,
            ExecutionMode = EditModel?.ExecutionMode
        };
        FlowNodes.Add(node);
        SelectedSubFlowToAdd = null;
        StatusMessage = $"Sub-flow '{node.Name}' added to workflow";
    }

    [RelayCommand]
    private void RemoveNode(FlowNodeEditModel? node)
    {
        if (node is null) return;
        FlowNodes.Remove(node);
        RecalculateOrderIndices();
        SelectedFlowNode = null;
        StatusMessage = "Node removed";
    }

    [RelayCommand]
    private void MoveNodeUp(FlowNodeEditModel? node)
    {
        if (node is null) return;
        var index = FlowNodes.IndexOf(node);
        if (index <= 0) return;
        FlowNodes.Move(index, index - 1);
        RecalculateOrderIndices();
    }

    [RelayCommand]
    private void MoveNodeDown(FlowNodeEditModel? node)
    {
        if (node is null) return;
        var index = FlowNodes.IndexOf(node);
        if (index < 0 || index >= FlowNodes.Count - 1) return;
        FlowNodes.Move(index, index + 1);
        RecalculateOrderIndices();
    }

    private void RecalculateOrderIndices()
    {
        int order = 0;
        foreach (var node in FlowNodes)
        {
            node.OrderIndex = order++;
        }
    }

    // --- 循环检测 ---

    private async Task<bool> HasCircularReferenceAsync(long flowId, long subFlowId, HashSet<long> visited)
    {
        if (flowId == subFlowId) return true;
        if (visited.Contains(subFlowId)) return false;
        visited.Add(subFlowId);

        var subFlow = await _dbContext.Flows
            .Include(f => f.SubFlowReferences)
            .FirstOrDefaultAsync(f => f.Id == subFlowId);

        if (subFlow is null) return false;

        foreach (var reference in subFlow.SubFlowReferences)
        {
            if (await HasCircularReferenceAsync(flowId, reference.SubFlowId, visited))
                return true;
        }

        return false;
    }
}

/// <summary>
/// 工作流编辑模型
/// </summary>
public partial class FlowEditModel : ObservableObject
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
    private string _executionMode = "Sequential";

    [ObservableProperty]
    private string? _coreEvent;

    [ObservableProperty]
    private string? _notifyEvent;

    [ObservableProperty]
    private int? _timeoutMs;

    [ObservableProperty]
    private bool _continueOnFailure = false;

    [ObservableProperty]
    private bool _isReusable = true;
}

/// <summary>
/// 工作流节点编辑模型（统一表示模块或子流节点）
/// </summary>
public partial class FlowNodeEditModel : ObservableObject
{
    public long Id { get; set; }

    [ObservableProperty]
    private int _orderIndex;

    [ObservableProperty]
    private string _name = string.Empty;

    [ObservableProperty]
    private string _nodeType = "Module"; // "Module" or "SubFlow"

    [ObservableProperty]
    private long _referenceId;

    [ObservableProperty]
    private string? _executionMode;

    [ObservableProperty]
    private string? _condition;
}
