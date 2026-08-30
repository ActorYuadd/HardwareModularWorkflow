﻿﻿using System.Collections.ObjectModel;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HardwareModularWorkflow.Db.Entities;
using HardwareModularWorkflow.Db.Services;
using HardwareModularWorkflow.Lang;
using HardwareModularWorkflow.Workflow.Engine;
using HardwareModularWorkflow.Workflow.Models;
using HardwareModularWorkflow.Workflow.Resources;
using HardwareModularWorkflow.Workflow.Scheduling;
using HardwareModularWorkflow.Wpf.Controls;
using Microsoft.EntityFrameworkCore;
using Microsoft.Win32;
using AppDbContext = HardwareModularWorkflow.Db.DbContext.HardwareModularWorkflowDbContext;

namespace HardwareModularWorkflow.Wpf.ViewModels;

/// <summary>
/// 宸ヤ綔娴佺紪杈戝櫒 ViewModel锛氬伐浣滄祦鍒楄〃銆佹ā鍧?瀛愭祦缂栨帓銆佹帓搴忋€佷覆/骞惰銆佸惊鐜娴?/// </summary>
public partial class FlowViewModel : ObservableObject
{
    private readonly FlowService _flowService;
    private readonly ModuleService _moduleService;
    private readonly AppDbContext _dbContext;
    private readonly IResourceReservationManager? _resourceReservationManager;
    private readonly WorkflowScheduler? _workflowScheduler;
    private readonly ISchedulingEventLog? _schedulingEventLog;
    private readonly HardwareInstanceService? _hardwareInstanceService;

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
    private ObservableCollection<FlowParameterEditModel> _inputParameters = new();

    [ObservableProperty]
    private ObservableCollection<FlowParameterEditModel> _outputParameters = new();

    [ObservableProperty]
    private ModuleEntity? _selectedModuleToAdd;

    [ObservableProperty]
    private FlowEntity? _selectedSubFlowToAdd;

    [ObservableProperty]
    private ObservableCollection<HardwareInstance> _hardwareInstances = new();

    [ObservableProperty]
    private HardwareInstance? _selectedFlowResourceHardware;

    [ObservableProperty]
    private ObservableCollection<FlowResourceReservationEditModel> _flowResourceReservations = new();

    [ObservableProperty]
    private ObservableCollection<GraphNodeEditModel> _graphNodes = new();

    [ObservableProperty]
    private ObservableCollection<GraphEdgeEditModel> _graphEdges = new();

    [ObservableProperty]
    private GraphNodeEditModel? _selectedGraphNode;

    [ObservableProperty]
    private GraphNodeEditModel? _selectedGraphEdgeFrom;

    [ObservableProperty]
    private GraphNodeEditModel? _selectedGraphEdgeTo;

    [ObservableProperty]
    private string _graphRouteKey = "Success";

    [ObservableProperty]
    private string _editPanelTitle = "Add Workflow";

    [ObservableProperty]
    private string _statusMessage = "Ready";

    [ObservableProperty]
    private string _cycleWarning = string.Empty;

    [ObservableProperty]
    private ObservableCollection<ResourceReservationDisplayModel> _resourceReservations = new();

    [ObservableProperty]
    private ResourceReservationDisplayModel? _selectedResourceReservation;

    [ObservableProperty]
    private int _waitingReservationPriority;

    [ObservableProperty]
    private string _schedulerMetrics = string.Empty;

    [ObservableProperty]
    private string _resourceQueueMetrics = string.Empty;

    [ObservableProperty]
    private ObservableCollection<SchedulingEventDisplayModel> _schedulingEvents = new();

    [ObservableProperty]
    private ObservableCollection<WorkflowTabItem> _openTabs = new();

    [ObservableProperty]
    private WorkflowTabItem? _selectedTab;

    [ObservableProperty]
    private bool _isConnectionMode;

    [ObservableProperty]
    private ObservableCollection<Guid> _executedNodeIds = new();

    [ObservableProperty]
    private ObservableCollection<string> _executedEdgeKeys = new();

    public FlowViewModel(
        FlowService flowService,
        ModuleService moduleService,
        AppDbContext dbContext,
        IResourceReservationManager? resourceReservationManager = null,
        WorkflowScheduler? workflowScheduler = null,
        ISchedulingEventLog? schedulingEventLog = null,
        HardwareInstanceService? hardwareInstanceService = null)
    {
        _flowService = flowService ?? throw new ArgumentNullException(nameof(flowService));
        _moduleService = moduleService ?? throw new ArgumentNullException(nameof(moduleService));
        _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
        _resourceReservationManager = resourceReservationManager;
        _workflowScheduler = workflowScheduler;
        _schedulingEventLog = schedulingEventLog;
        _hardwareInstanceService = hardwareInstanceService;
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
        StatusMessage = LangKeys.Message_LoadingWorkflows;
        try
        {
            var list = await _flowService.GetAllAsync();
            Flows = new ObservableCollection<FlowEntity>(list);

            var modules = await _moduleService.GetAllAsync();
            AvailableModules = new ObservableCollection<ModuleEntity>(modules);

            var subFlows = await _flowService.GetReusableFlowsAsync();
            AvailableSubFlows = new ObservableCollection<FlowEntity>(subFlows);

            if (_hardwareInstanceService is not null)
                HardwareInstances = new ObservableCollection<HardwareInstance>(await _hardwareInstanceService.GetAllAsync());
            RefreshResourceReservations();

            StatusMessage = string.Format(LangKeys.Message_LoadedWorkflows, list.Count);
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
    private void AddFlow()
    {
        EditModel = new FlowEditModel();
        FlowNodes.Clear();
        SelectedFlowNode = null;
        SelectedModuleToAdd = null;
        SelectedSubFlowToAdd = null;
        EditPanelTitle = LangKeys.Title_AddWorkflow;
        CycleWarning = string.Empty;
        IsEditing = true;
        StatusMessage = LangKeys.Message_AddingNewWorkflow;
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
                    Name = relation.Module?.Name ?? string.Format(LangKeys.Format_ModuleReference, relation.ModuleId),
                    NodeType = LangKeys.Label_Module,
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
                    Name = reference.SubFlow?.Name ?? string.Format(LangKeys.Format_FlowReference, reference.SubFlowId),
                    NodeType = LangKeys.Label_SubFlow,
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
        EditPanelTitle = string.Format(LangKeys.Title_EditWorkflow, flow.Id);
        CycleWarning = string.Empty;
        IsEditing = true;
        StatusMessage = string.Format(LangKeys.Message_EditingName, flow.Name);
    }

    [RelayCommand]
    private async Task SaveFlowAsync()
    {
        if (EditModel is null) return;

        if (string.IsNullOrWhiteSpace(EditModel.Name))
        {
            StatusMessage = LangKeys.Validation_WorkflowNameRequired;
            return;
        }

        // 寰幆妫€娴?        if (EditModel.Id != 0)
        {
            var cycleNodes = FlowNodes.Where(n => n.NodeType == "SubFlow").ToList();
            foreach (var node in cycleNodes)
            {
                if (node.ReferenceId == EditModel.Id)
                {
                    StatusMessage = LangKeys.Validation_CannotReferenceSelfAsSubFlow;
                    return;
                }
                var visited = new HashSet<long>();
                if (await HasCircularReferenceAsync(EditModel.Id, node.ReferenceId, visited))
                {
                    StatusMessage = string.Format(LangKeys.Validation_CircularReferenceDetected, node.Name);
                    return;
                }
            }
        }

        IsLoading = true;
        try
        {
            if (EditModel.Id == 0)
            {
                // 鍒涘缓鏂板伐浣滄祦
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
                StatusMessage = string.Format(LangKeys.Message_WorkflowCreatedWithNodes, entity.Name, FlowNodes.Count);
            }
            else
            {
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

                // 娓呴櫎鏃у叧鑱?                _dbContext.FlowModuleRelations.RemoveRange(entity.ModuleRelations);
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
                StatusMessage = string.Format(LangKeys.Message_WorkflowUpdatedWithNodes, entity.Name, FlowNodes.Count);
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
        FlowNodes.Clear();
        SelectedFlowNode = null;
        SelectedModuleToAdd = null;
        SelectedSubFlowToAdd = null;
        CycleWarning = string.Empty;
        StatusMessage = LangKeys.Message_EditCancelled;
    }

    [RelayCommand]
    private async Task DeleteFlowAsync(FlowEntity? flow)
    {
        if (flow is null) return;

        IsLoading = true;
        try
        {
            await _flowService.DeleteAsync(flow.Id);
            StatusMessage = string.Format(LangKeys.Message_WorkflowDeleted, flow.Name);
            await LoadFlowsAsync();
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

    // --- 鑺傜偣缂栨帓 ---

    [RelayCommand]
    private void AddModuleNode()
    {
        if (SelectedModuleToAdd is null) return;
        var node = new FlowNodeEditModel
        {
            OrderIndex = FlowNodes.Count,
            Name = SelectedModuleToAdd.Name,
            NodeType = LangKeys.Label_Module,
            ReferenceId = SelectedModuleToAdd.Id,
            ExecutionMode = EditModel?.ExecutionMode
        };
        FlowNodes.Add(node);
        SelectedModuleToAdd = null;
        StatusMessage = string.Format(LangKeys.Message_ModuleAddedToWorkflow, node.Name);
    }

    [RelayCommand]
    private void AddSubFlowNode()
    {
        if (SelectedSubFlowToAdd is null) return;
        if (EditModel?.Id != 0 && SelectedSubFlowToAdd.Id == EditModel?.Id)
        {
            StatusMessage = LangKeys.Message_CannotReferenceWorkflowItself;
            return;
        }
        var node = new FlowNodeEditModel
        {
            OrderIndex = FlowNodes.Count,
            Name = SelectedSubFlowToAdd.Name,
            NodeType = LangKeys.Label_SubFlow,
            ReferenceId = SelectedSubFlowToAdd.Id,
            ExecutionMode = EditModel?.ExecutionMode
        };
        FlowNodes.Add(node);
        SelectedSubFlowToAdd = null;
        StatusMessage = string.Format(LangKeys.Message_SubFlowAddedToWorkflow, node.Name);
    }

    [RelayCommand]
    private void RemoveNode(FlowNodeEditModel? node)
    {
        if (node is null) return;
        FlowNodes.Remove(node);
        RecalculateOrderIndices();
        SelectedFlowNode = null;
        StatusMessage = LangKeys.Message_NodeRemoved;
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

    // --- 寰幆妫€娴?---

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

    [RelayCommand]
    private void RefreshResourceReservations()
    {
        if (_workflowScheduler is null) return;
        var s = _workflowScheduler.GetSnapshot();
        SchedulerMetrics = $"并发 {s.RunningWorkItems}/{s.MaxConcurrency}，队列 {s.QueuedWorkItems}，平均等待 {s.AverageQueueWait.TotalMilliseconds:F0} ms";
        if (_resourceReservationManager is not null)
        {
            var m = _resourceReservationManager.GetMetrics();
            ResourceQueueMetrics = $"资源等待 {m.WaitingReservations}/{m.QueueCapacity}，持有 {m.GrantedReservations}，继承 {m.InheritedPriorityReservations}，最长等待 {m.OldestWaitingDuration.TotalSeconds:F1}s";
            ResourceReservations = new ObservableCollection<ResourceReservationDisplayModel>(
                _resourceReservationManager.GetSnapshot().OrderBy(x => x.IsGranted).ThenByDescending(x => x.EffectivePriority)
                .Select(x => new ResourceReservationDisplayModel
                {
                    WorkflowRunId = x.WorkflowRunId, NodeRunId = x.NodeRunId, ReservationId = x.ReservationId,
                    Status = x.IsGranted ? "持有中" : "等待中", IsGranted = x.IsGranted,
                    Resources = string.Join(", ", x.Requirements.Select(r => r.ResourceId)),
                    Waiting = x.IsGranted ? "-" : x.WaitingDuration.ToString(@"mm\:ss"),
                    Deadline = x.DeadlineUtc?.ToLocalTime().ToString("HH:mm:ss") ?? "-",
                    Priority = $"{x.BasePriority} +{x.AgingPriorityBoost} +{x.InheritedPriorityBoost} -> {x.EffectivePriority}",
                    BlockedBy = x.BlockingReservationIds.Count == 0 ? "-" : string.Join(", ", x.BlockingReservationIds.Select(id => id.ToString("N")[..8]))
                }));
        }
        if (_schedulingEventLog is not null)
        {
            SchedulingEvents = new ObservableCollection<SchedulingEventDisplayModel>(
                _schedulingEventLog.GetRecentEvents(100).Select(e => new SchedulingEventDisplayModel
                {
                    Time = e.TimestampUtc.ToLocalTime().ToString("HH:mm:ss.fff"), Kind = e.Kind.ToString(),
                    Source = e.Source, WorkflowRun = e.WorkflowRunId is Guid g ? g.ToString("N")[..8] : "-", Message = e.Message
                }));
        }
    }

    [RelayCommand] private void UpdateWaitingReservationPriority()
    {
        if (SelectedResourceReservation is null || SelectedResourceReservation.IsGranted || _resourceReservationManager is null) return;
        _resourceReservationManager.TryUpdateWaitingPriority(SelectedResourceReservation.WorkflowRunId, SelectedResourceReservation.NodeRunId, WaitingReservationPriority);
        RefreshResourceReservations();
    }
    [RelayCommand] private void CancelWaitingReservation()
    {
        if (SelectedResourceReservation is null || SelectedResourceReservation.IsGranted || _resourceReservationManager is null) return;
        _resourceReservationManager.TryCancelWaitingReservation(SelectedResourceReservation.ReservationId);
        SelectedResourceReservation = null; RefreshResourceReservations();
    }

    // --- Tab management ---
    [RelayCommand] private void OpenTab(FlowEntity? f) { if (f is null) return; if (OpenTabs.Any(t => t.FlowId == f.Id)) { SelectedTab = OpenTabs.First(t => t.FlowId == f.Id); return; } var tab = new WorkflowTabItem { TabId = Guid.NewGuid(), FlowId = f.Id, Title = f.Name, EditModel = new FlowEditModel() }; OpenTabs.Add(tab); SelectedTab = tab; LoadTabContent(tab, f); }
    [RelayCommand] private void CloseTab(WorkflowTabItem? t) { if (t is null) return; OpenTabs.Remove(t); if (SelectedTab == t) SelectedTab = OpenTabs.LastOrDefault(); }
    [RelayCommand] private void AddNewTab() { var tab = new WorkflowTabItem { TabId = Guid.NewGuid(), Title = "New Workflow", EditModel = new FlowEditModel() }; OpenTabs.Add(tab); SelectedTab = tab; IsEditing = true; }

    partial void OnSelectedTabChanged(WorkflowTabItem? value)
    {
        if (value is null) return;
        EditModel = value.EditModel;
        GraphNodes = value.Nodes;
        GraphEdges = new ObservableCollection<GraphEdgeEditModel>((value.Edges ?? new()).Select(e => new GraphEdgeEditModel { FromNodeId = e.FromNodeId, ToNodeId = e.ToNodeId, RouteKey = e.RouteKey, IsDefault = e.IsDefault }));
        IsEditing = true;
    }

    private void LoadTabContent(WorkflowTabItem tab, FlowEntity flow)
    {
        tab.Title = flow.Name;
        tab.EditModel = new FlowEditModel { Id = flow.Id, Name = flow.Name, Alias = flow.Alias, Note = flow.Note, Tags = flow.Tags, ExecutionMode = flow.ExecutionMode, TimeoutMs = flow.TimeoutMs, ResourceWaitTimeoutMs = flow.ResourceWaitTimeoutMs > 0 ? flow.ResourceWaitTimeoutMs : 30000, MaxGraphNodeVisits = flow.MaxGraphNodeVisits > 0 ? flow.MaxGraphNodeVisits : 1000, DefinitionVersion = flow.DefinitionVersion, RecoveryPolicy = flow.RecoveryPolicy ?? "NotRecoverable", ContinueOnFailure = flow.ContinueOnFailure, IsReusable = flow.IsReusable };
        tab.Nodes.Clear(); tab.Edges?.Clear();
        foreach (var n in flow.GraphNodes.OrderBy(n => n.Id))
            tab.Nodes.Add(new GraphNodeEditModel { NodeId = n.NodeId, Name = n.Name, NodeType = n.NodeType, ModuleId = n.ModuleId, SubFlowId = n.SubFlowId, RouteKeyVariable = n.RouteKeyVariable, JoinMode = n.JoinMode ?? "WaitAll", MaxVisits = n.MaxVisits, InvocationPolicy = n.SubFlowInvocationPolicy ?? "Reentrant", X = n.X, Y = n.Y });
        var byDbId = new Dictionary<long, GraphNodeEditModel>();
        var dbNodes = flow.GraphNodes.ToList();
        for (int i = 0; i < dbNodes.Count && i < tab.Nodes.Count; i++) byDbId[dbNodes[i].Id] = tab.Nodes[i];
        foreach (var e in flow.GraphEdges)
        {
            if (byDbId.TryGetValue(e.FromNodeId, out var fn) && byDbId.TryGetValue(e.ToNodeId, out var tn) && tab.Edges is not null)
            {
                var re = new GraphEdgeRenderModel { FromNodeId = fn.NodeId, ToNodeId = tn.NodeId, RouteKey = e.RouteKey ?? "Success", IsDefault = e.IsDefault };
                re.UpdateLine(fn.X + fn.Width / 2, fn.Y + fn.Height, tn.X + tn.Width / 2, tn.Y);
                tab.Edges.Add(re);
            }
        }
    }

    [RelayCommand] private async Task SaveTabAsync()
    {
        if (SelectedTab?.EditModel is null || string.IsNullOrWhiteSpace(SelectedTab.EditModel.Name)) { StatusMessage = "Name required"; return; }
        var m = SelectedTab.EditModel; IsLoading = true;
        try
        {
            FlowEntity entity;
            if (m.Id == 0) { entity = new FlowEntity { Name = m.Name.Trim(), Alias = m.Alias, Note = m.Note, Tags = m.Tags, ExecutionMode = m.ExecutionMode, TimeoutMs = m.TimeoutMs, ResourceWaitTimeoutMs = m.ResourceWaitTimeoutMs, MaxGraphNodeVisits = m.MaxGraphNodeVisits, DefinitionVersion = 1, RecoveryPolicy = m.RecoveryPolicy, ContinueOnFailure = m.ContinueOnFailure, IsReusable = m.IsReusable }; _dbContext.Flows.Add(entity); await _dbContext.SaveChangesAsync(); SelectedTab.FlowId = entity.Id; }
            else { entity = await _dbContext.Flows.Include(f => f.GraphNodes).Include(f => f.GraphEdges).FirstAsync(f => f.Id == m.Id); entity.Name = m.Name.Trim(); entity.Alias = m.Alias; entity.Note = m.Note; entity.Tags = m.Tags; entity.ExecutionMode = m.ExecutionMode; entity.TimeoutMs = m.TimeoutMs; entity.ResourceWaitTimeoutMs = m.ResourceWaitTimeoutMs; entity.MaxGraphNodeVisits = m.MaxGraphNodeVisits; entity.DefinitionVersion = Math.Max(1, entity.DefinitionVersion + 1); entity.RecoveryPolicy = m.RecoveryPolicy; entity.ContinueOnFailure = m.ContinueOnFailure; entity.IsReusable = m.IsReusable; _dbContext.FlowGraphEdges.RemoveRange(entity.GraphEdges); _dbContext.FlowGraphNodes.RemoveRange(entity.GraphNodes); entity.GraphEdges.Clear(); entity.GraphNodes.Clear(); }
            foreach (var n in SelectedTab.Nodes)
                entity.GraphNodes.Add(new FlowGraphNode { NodeId = n.NodeId, NodeType = n.NodeType, Name = n.Name, ModuleId = n.ModuleId, SubFlowId = n.SubFlowId, RouteKeyVariable = n.RouteKeyVariable, JoinMode = n.JoinMode, MaxVisits = n.MaxVisits, SubFlowInvocationPolicy = n.InvocationPolicy, X = n.X, Y = n.Y });
            await _dbContext.SaveChangesAsync();
            var persisted = entity.GraphNodes.ToDictionary(n => n.NodeId, n => n.Id);
            foreach (var e in SelectedTab.Edges ?? new())
                if (persisted.TryGetValue(e.FromNodeId, out var fid) && persisted.TryGetValue(e.ToNodeId, out var tid))
                    entity.GraphEdges.Add(new FlowGraphEdge { FromNodeId = fid, ToNodeId = tid, RouteKey = e.RouteKey, IsDefault = e.IsDefault });
            await _dbContext.SaveChangesAsync();
            SelectedTab.Title = entity.Name; SelectedTab.IsDirty = false; SelectedTab.NotifyHeaderChanged();
            StatusMessage = $"Saved: {entity.Name}"; await LoadFlowsAsync();
        }
        catch (Exception ex) { StatusMessage = $"Save failed: {ex.Message}"; }
        finally { IsLoading = false; }
    }

    // --- Graph node commands ---
    private void AddNodeToTab(string type, string name, long? modId = null, long? subId = null, string joinMode = "WaitAll")
    {
        if (SelectedTab is null) { AddNewTab(); if (SelectedTab is null) return; }
        var n = new GraphNodeEditModel { NodeType = type, Name = name, ModuleId = modId, SubFlowId = subId, JoinMode = joinMode, InvocationPolicy = "Reentrant", X = 50 + SelectedTab.Nodes.Count * 200, Y = 50 + (SelectedTab.Nodes.Count % 3) * 100 };
        SelectedTab.Nodes.Add(n); SelectedTab.NotifyDirty();
    }
    [RelayCommand] private void AddGraphStart() => AddNodeToTab("Start", "Start");
    [RelayCommand] private void AddGraphEnd() => AddNodeToTab("End", "End");
    [RelayCommand] private void AddGraphSwitch() => AddNodeToTab("Switch", "Switch");
    [RelayCommand] private void AddGraphFork() => AddNodeToTab("Fork", "Fork");
    [RelayCommand] private void AddGraphJoin() => AddNodeToTab("Join", "Join (WaitAll)");
    [RelayCommand] private void AddGraphWaitAnyJoin() => AddNodeToTab("Join", "Join (WaitAny)", joinMode: "WaitAny");
    [RelayCommand] private void AddGraphModule() { if (SelectedModuleToAdd is not null) AddNodeToTab("Module", SelectedModuleToAdd.Name, modId: SelectedModuleToAdd.Id); }
    [RelayCommand] private void AddGraphSubFlow() { if (SelectedSubFlowToAdd is not null) AddNodeToTab("SubFlow", SelectedSubFlowToAdd.Name, subId: SelectedSubFlowToAdd.Id); }
    [RelayCommand] private void RemoveGraphNode(GraphNodeEditModel? n)
    {
        if (n is null || SelectedTab is null) return;
        var es = (SelectedTab.Edges ?? new()).Where(e => e.FromNodeId == n.NodeId || e.ToNodeId == n.NodeId).ToList();
        foreach (var e in es) SelectedTab.Edges?.Remove(e);
        SelectedTab.Nodes.Remove(n); SelectedTab.NotifyDirty();
    }
    [RelayCommand] private void AddEdgeToTab(object? c)
    {
        if (c is ValueTuple<Guid, Guid, string> conn && SelectedTab is not null)
        {
            var fn = SelectedTab.Nodes.FirstOrDefault(n => n.NodeId == conn.Item1);
            var tn = SelectedTab.Nodes.FirstOrDefault(n => n.NodeId == conn.Item2);
            if (fn is null || tn is null || conn.Item1 == conn.Item2) return;
            if ((SelectedTab.Edges ?? new()).Any(e => e.FromNodeId == conn.Item1 && e.ToNodeId == conn.Item2 && e.RouteKey == conn.Item3)) return;
            var re = new GraphEdgeRenderModel { FromNodeId = conn.Item1, ToNodeId = conn.Item2, RouteKey = conn.Item3 };
            re.UpdateLine(fn.X + fn.Width / 2, fn.Y + fn.Height, tn.X + tn.Width / 2, tn.Y);
            SelectedTab.Edges?.Add(re); SelectedTab.NotifyDirty();
        }
    }
    [RelayCommand] private void RemoveEdgeFromTab(object? e) { if (e is GraphEdgeRenderModel re && SelectedTab is not null) { SelectedTab.Edges?.Remove(re); SelectedTab.NotifyDirty(); } }
    [RelayCommand] private void ToggleConnectionMode() => IsConnectionMode = !IsConnectionMode;
    [RelayCommand] private void AutoLayoutNodes()
    {
        if (SelectedTab is null) return;
        var inDeg = new Dictionary<Guid, int>(); foreach (var n in SelectedTab.Nodes) inDeg[n.NodeId] = 0;
        foreach (var e in SelectedTab.Edges ?? new()) if (inDeg.ContainsKey(e.ToNodeId)) inDeg[e.ToNodeId]++;
        var layers = new Dictionary<Guid, int>(); var q = new Queue<Guid>();
        foreach (var kv in inDeg) if (kv.Value == 0) { q.Enqueue(kv.Key); layers[kv.Key] = 0; }
        while (q.Count > 0) { var cur = q.Dequeue(); var layer = layers.GetValueOrDefault(cur, 0); foreach (var e in (SelectedTab.Edges ?? new()).Where(e => e.FromNodeId == cur)) { layers[e.ToNodeId] = Math.Max(layers.GetValueOrDefault(e.ToNodeId, 0), layer + 1); q.Enqueue(e.ToNodeId); } }
        var pos = new Dictionary<int, int>();
        foreach (var n in SelectedTab.Nodes) { var l = layers.GetValueOrDefault(n.NodeId, 0); n.X = 60 + l * 200; var idx = pos.GetValueOrDefault(l, 0); n.Y = 40 + idx * 100; pos[l] = idx + 1; }
        foreach (var e in SelectedTab.Edges ?? new()) { var fn = SelectedTab.Nodes.FirstOrDefault(n => n.NodeId == e.FromNodeId); var tn = SelectedTab.Nodes.FirstOrDefault(n => n.NodeId == e.ToNodeId); if (fn is not null && tn is not null) e.UpdateLine(fn.X + fn.Width / 2, fn.Y + fn.Height, tn.X + tn.Width / 2, tn.Y); }
    }
    [RelayCommand] private void ClearExecutionHighlights() { ExecutedNodeIds.Clear(); ExecutedEdgeKeys.Clear(); foreach (var t in OpenTabs) { foreach (var n in t.Nodes) n.IsExecuted = false; foreach (var e in t.Edges ?? new()) e.IsExecuted = false; } }

    // --- Parameters ---
    [RelayCommand] private void AddInputParameter() => InputParameters.Add(new FlowParameterEditModel());
    [RelayCommand] private void AddOutputParameter() => OutputParameters.Add(new FlowParameterEditModel { IsRequired = false });
    [RelayCommand] private void RemoveInputParameter(FlowParameterEditModel? p) { if (p is not null) InputParameters.Remove(p); }
    [RelayCommand] private void RemoveOutputParameter(FlowParameterEditModel? p) { if (p is not null) OutputParameters.Remove(p); }

    // --- Flow resource ---
    [RelayCommand] private void AddFlowResourceReservation() { if (SelectedFlowResourceHardware is not null && !FlowResourceReservations.Any(r => r.HardwareInstanceId == SelectedFlowResourceHardware.Id)) FlowResourceReservations.Add(new FlowResourceReservationEditModel { HardwareInstanceId = SelectedFlowResourceHardware.Id, Name = SelectedFlowResourceHardware.Name }); SelectedFlowResourceHardware = null; }
    [RelayCommand] private void RemoveFlowResourceReservation(FlowResourceReservationEditModel? r) { if (r is not null) FlowResourceReservations.Remove(r); }

    // --- Export / Import ---
    [RelayCommand] private async Task ExportFlowAsync(FlowEntity? f)
    {
        if (f is null) return;
        var full = await _dbContext.Flows.Include(x => x.GraphNodes).Include(x => x.GraphEdges).Include(x => x.Parameters).Include(x => x.ResourceReservations).AsNoTracking().FirstOrDefaultAsync(x => x.Id == f.Id);
        if (full is null) return;
        var json = JsonSerializer.Serialize(full, new JsonSerializerOptions { WriteIndented = true, ReferenceHandler = ReferenceHandler.IgnoreCycles });
        var path = Path.Combine(Environment.CurrentDirectory, SanitizeFileName(full.Name) + "_workflow.json");
        await File.WriteAllTextAsync(path, json); StatusMessage = $"Exported to {path}";
    }
    [RelayCommand] private async Task ExportAllFlowsAsync()
    {
        var all = await _dbContext.Flows.AsNoTracking().ToListAsync(); if (all.Count == 0) return;
        var path = Path.Combine(Environment.CurrentDirectory, $"all_workflows_{DateTime.Now:yyyyMMdd_HHmmss}.json");
        var json = JsonSerializer.Serialize(all, new JsonSerializerOptions { WriteIndented = true, ReferenceHandler = ReferenceHandler.IgnoreCycles });
        await File.WriteAllTextAsync(path, json); StatusMessage = $"Exported {all.Count} workflows to {path}";
    }
    [RelayCommand] private async Task ImportFlowAsync()
    {
        var dlg = new OpenFileDialog { Filter = "JSON|*.json", Title = "Import Workflow" };
        if (dlg.ShowDialog() != true) return;
        var json = await File.ReadAllTextAsync(dlg.FileName);
        var opts = new JsonSerializerOptions { ReferenceHandler = ReferenceHandler.IgnoreCycles };
        try
        {
            var flows = JsonSerializer.Deserialize<List<FlowEntity>>(json, opts);
            if (flows is { Count: > 0 }) { foreach (var f in flows) { f.Id = 0; f.GraphNodes.ToList().ForEach(n => n.Id = 0); f.GraphEdges.ToList().ForEach(e => e.Id = 0); _dbContext.Flows.Add(f); } await _dbContext.SaveChangesAsync(); StatusMessage = $"Imported {flows.Count} workflows."; await LoadFlowsAsync(); return; }
        }
        catch { }
        try
        {
            var single = JsonSerializer.Deserialize<FlowEntity>(json, opts);
            if (single is not null) { single.Id = 0; single.GraphNodes.ToList().ForEach(n => n.Id = 0); single.GraphEdges.ToList().ForEach(e => e.Id = 0); _dbContext.Flows.Add(single); await _dbContext.SaveChangesAsync(); StatusMessage = $"Imported: {single.Name}"; await LoadFlowsAsync(); }
        }
        catch (Exception ex) { StatusMessage = $"Import failed: {ex.Message}"; }
    }

    // --- Helpers ---
    private static string SanitizeFileName(string name) => string.Join("_", name.Split(Path.GetInvalidFileNameChars()));
    private static List<FlowSubFlowParameterBinding> ToParameterBindings(FlowNodeEditModel node) => new();
}

/// <summary>
/// Workflow Edit Model
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
    private int _resourceWaitTimeoutMs = 30000;

    [ObservableProperty]
    private int _maxGraphNodeVisits = 1000;

    [ObservableProperty]
    private int _definitionVersion = 1;

    [ObservableProperty]
    private string _recoveryPolicy = "NotRecoverable";

    [ObservableProperty]
    private bool _continueOnFailure = false;

    [ObservableProperty]
    private bool _isReusable = true;
}
public partial class FlowNodeEditModel : ObservableObject
{
    public long Id { get; set; }
    [ObservableProperty] private int _orderIndex;
    [ObservableProperty] private string _name = string.Empty;
    [ObservableProperty] private string _nodeType = "Module";
    [ObservableProperty] private long _referenceId;
    [ObservableProperty] private string? _executionMode;
    [ObservableProperty] private string? _condition;
    [ObservableProperty] private string? _inputBindings;
    [ObservableProperty] private string? _outputBindings;
    [ObservableProperty] private string _invocationPolicy = "Reentrant";
}

public partial class FlowParameterEditModel : ObservableObject
{
    [ObservableProperty] private string _name = string.Empty;
    [ObservableProperty] private string _parameterType = "String";
    [ObservableProperty] private bool _isRequired;
    [ObservableProperty] private string? _defaultValueJson;
    [ObservableProperty] private string? _description;
}

public sealed class ResourceReservationDisplayModel
{
    public Guid ReservationId { get; init; }
    public Guid WorkflowRunId { get; init; }
    public Guid NodeRunId { get; init; }
    public string Status { get; init; } = string.Empty;
    public bool IsGranted { get; init; }
    public string Resources { get; init; } = string.Empty;
    public string Waiting { get; init; } = string.Empty;
    public string Deadline { get; init; } = string.Empty;
    public string Priority { get; init; } = string.Empty;
    public string BlockedBy { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
}

public sealed class SchedulingEventDisplayModel
{
    public string Time { get; init; } = string.Empty;
    public string Kind { get; init; } = string.Empty;
    public string Source { get; init; } = string.Empty;
    public string WorkflowRun { get; init; } = string.Empty;
    public string Message { get; init; } = string.Empty;
}

public partial class FlowResourceReservationEditModel : ObservableObject
{
    public long HardwareInstanceId { get; set; }
    [ObservableProperty] private string _name = string.Empty;
    [ObservableProperty] private string _accessMode = "Exclusive";
    [ObservableProperty] private int _priority;
}

public partial class GraphNodeEditModel : ObservableObject
{
    public Guid NodeId { get; set; } = Guid.NewGuid();
    [ObservableProperty] private string _name = string.Empty;
    [ObservableProperty] private string _nodeType = "Module";
    [ObservableProperty] private long? _moduleId;
    [ObservableProperty] private long? _subFlowId;
    [ObservableProperty] private string? _routeKeyVariable;
    [ObservableProperty] private string _joinMode = "WaitAll";
    [ObservableProperty] private int? _maxVisits;
    [ObservableProperty] private string _invocationPolicy = "Reentrant";
    [ObservableProperty] private string? _inputBindings;
    [ObservableProperty] private string? _outputBindings;
    [ObservableProperty] private double _x;
    [ObservableProperty] private double _y;
    [ObservableProperty] private double _width = 120;
    [ObservableProperty] private double _height = 44;
    [ObservableProperty] private bool _isExecuted;
}

public partial class GraphEdgeEditModel : ObservableObject
{
    public Guid FromNodeId { get; set; }
    public Guid ToNodeId { get; set; }
    [ObservableProperty] private string? _routeKey;
    [ObservableProperty] private bool _isDefault;
    [ObservableProperty] private int _priority;
}

public class WorkflowTabItem : ObservableObject
{
    public Guid TabId { get; set; } = Guid.NewGuid();
    public long FlowId { get; set; }
    private string _title = string.Empty;
    public string Title { get => _title; set => SetProperty(ref _title, value); }
    public ObservableCollection<GraphNodeEditModel> Nodes { get; set; } = new();
    public ObservableCollection<GraphEdgeRenderModel>? Edges { get; set; } = new();
    public FlowEditModel? EditModel { get; set; }
    public bool IsDirty { get; set; }
    public string DisplayHeader => $"{(IsDirty ? "*" : "")}{Title}{(FlowId > 0 ? "" : " [unsaved]")}";
    public void NotifyDirty() { IsDirty = true; NotifyHeaderChanged(); }
    public void NotifyHeaderChanged() => OnPropertyChanged(new System.ComponentModel.PropertyChangedEventArgs(nameof(DisplayHeader)));
}