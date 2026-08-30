using Microsoft.EntityFrameworkCore;
using HardwareModularWorkflow.Db.Entities;
using HardwareModularWorkflow.Db.DbContext;

namespace HardwareModularWorkflow.Db.Services;

/// <summary>
/// 控制器配置服务：增删改查控制器配置
/// </summary>
public class ControllerService
{
    private readonly HardwareModularWorkflowDbContext _context;

    public ControllerService(HardwareModularWorkflowDbContext context)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
    }

    public async Task<List<ControllerEntity>> GetAllAsync(CancellationToken ct = default)
    {
        return await _context.Controllers
            .AsNoTracking()
            .OrderBy(c => c.Id)
            .ToListAsync(ct);
    }

    public async Task<ControllerEntity?> GetByIdAsync(long id, CancellationToken ct = default)
    {
        return await _context.Controllers
            .AsNoTracking()
            .Include(c => c.HardwareInstances)
            .FirstOrDefaultAsync(c => c.Id == id, ct);
    }

    public async Task<ControllerEntity> CreateAsync(ControllerEntity entity, CancellationToken ct = default)
    {
        entity.CreatedAt = DateTime.UtcNow;
        entity.UpdatedAt = DateTime.UtcNow;
        _context.Controllers.Add(entity);
        await _context.SaveChangesAsync(ct);
        return entity;
    }

    public async Task<ControllerEntity> UpdateAsync(ControllerEntity entity, CancellationToken ct = default)
    {
        entity.UpdatedAt = DateTime.UtcNow;
        _context.Controllers.Update(entity);
        await _context.SaveChangesAsync(ct);
        return entity;
    }

    public async Task DeleteAsync(long id, CancellationToken ct = default)
    {
        var entity = await _context.Controllers.FindAsync(new object[] { id }, ct);
        if (entity is not null)
        {
            _context.Controllers.Remove(entity);
            await _context.SaveChangesAsync(ct);
        }
    }

    public async Task<ControllerEntity?> GetDefaultAsync(string controllerType, CancellationToken ct = default)
    {
        return await _context.Controllers
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.ControllerType == controllerType && c.IsDefault && c.IsEnabled, ct);
    }
}

/// <summary>
/// 硬件定义服务：管理硬件模板（电机、制温、制冷、自定义）
/// </summary>
public class HardwareDefinitionService
{
    private readonly HardwareModularWorkflowDbContext _context;

    public HardwareDefinitionService(HardwareModularWorkflowDbContext context)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
    }

    public async Task<List<HardwareDefinition>> GetAllAsync(CancellationToken ct = default)
    {
        return await _context.HardwareDefinitions
            .AsNoTracking()
            .Include(h => h.Category)
            .Include(h => h.ControlProfile)
            .OrderBy(h => h.Category!.Name).ThenBy(h => h.Name)
            .ToListAsync(ct);
    }

    public async Task<HardwareDefinition?> GetByIdAsync(long id, CancellationToken ct = default)
    {
        return await _context.HardwareDefinitions
            .AsNoTracking()
            .Include(h => h.HardwareInstances)
            .Include(h => h.Category)
            .Include(h => h.ControlProfile)
            .FirstOrDefaultAsync(h => h.Id == id, ct);
    }

    public async Task<HardwareDefinition> CreateAsync(HardwareDefinition entity, CancellationToken ct = default)
    {
        await ApplyCategoryAndProfileAsync(entity, ct);
        entity.CreatedAt = DateTime.UtcNow;
        entity.UpdatedAt = DateTime.UtcNow;
        _context.HardwareDefinitions.Add(entity);
        await _context.SaveChangesAsync(ct);
        return entity;
    }

    public async Task<HardwareDefinition> UpdateAsync(HardwareDefinition entity, CancellationToken ct = default)
    {
        await ApplyCategoryAndProfileAsync(entity, ct);
        entity.UpdatedAt = DateTime.UtcNow;
        _context.HardwareDefinitions.Update(entity);
        await _context.SaveChangesAsync(ct);
        return entity;
    }

    public async Task DeleteAsync(long id, CancellationToken ct = default)
    {
        var entity = await _context.HardwareDefinitions.FindAsync(new object[] { id }, ct);
        if (entity is null)
        {
            return;
        }

        if (entity.IsSystem)
        {
            throw new InvalidOperationException("System hardware definitions cannot be deleted.");
        }

        var isInUse = await _context.HardwareInstances
            .AnyAsync(h => h.DefinitionId == id, ct);
        if (isInUse)
        {
            throw new InvalidOperationException(
                "This hardware definition is in use and cannot be deleted.");
        }

        _context.HardwareDefinitions.Remove(entity);
        await _context.SaveChangesAsync(ct);
    }

    public async Task<List<HardwareDefinition>> GetByTypeAsync(string type, CancellationToken ct = default)
    {
        return await _context.HardwareDefinitions
            .AsNoTracking()
            .Where(h => h.Type == type)
            .ToListAsync(ct);
    }

    private async Task ApplyCategoryAndProfileAsync(
        HardwareDefinition entity,
        CancellationToken ct)
    {
        if (!entity.CategoryId.HasValue || !entity.ControlProfileId.HasValue)
        {
            throw new InvalidOperationException(
                "A hardware category and control profile are required.");
        }

        var profile = await _context.HardwareControlProfiles
            .Include(item => item.Category)
            .FirstOrDefaultAsync(
                item => item.Id == entity.ControlProfileId.Value,
                ct);
        if (profile is null || profile.CategoryId != entity.CategoryId.Value)
        {
            throw new InvalidOperationException(
                "The selected control profile does not belong to the hardware category.");
        }

        // Keep the first-phase runtime compatible until driver resolution uses profiles.
        entity.Type = profile.Category.Code;
    }
}

/// <summary>
/// Hardware catalog service: exposes hardware categories and their control profiles.
/// </summary>
public class HardwareCatalogService
{
    private readonly HardwareModularWorkflowDbContext _context;

    public HardwareCatalogService(HardwareModularWorkflowDbContext context)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
    }

    public async Task<List<HardwareCategory>> GetCategoriesAsync(
        CancellationToken ct = default) =>
        await _context.HardwareCategories
            .AsNoTracking()
            .OrderBy(category => category.Name)
            .ToListAsync(ct);

    public async Task<List<HardwareControlProfile>> GetProfilesAsync(
        CancellationToken ct = default) =>
        await _context.HardwareControlProfiles
            .AsNoTracking()
            .Include(profile => profile.Category)
            .OrderBy(profile => profile.Category.Name)
            .ThenBy(profile => profile.Name)
            .ToListAsync(ct);
}

/// <summary>
/// 硬件实例服务：管理具体硬件设备配置
/// </summary>
public class HardwareInstanceService
{
    private readonly HardwareModularWorkflowDbContext _context;

    public HardwareInstanceService(HardwareModularWorkflowDbContext context)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
    }

    public async Task<List<HardwareInstance>> GetAllAsync(CancellationToken ct = default)
    {
        return await _context.HardwareInstances
            .AsNoTracking()
            .Include(h => h.Definition)
                .ThenInclude(d => d.Category)
            .Include(h => h.Definition)
                .ThenInclude(d => d.ControlProfile)
            .Include(h => h.Controller)
            .OrderBy(h => h.Name)
            .ToListAsync(ct);
    }

    public async Task<HardwareInstance?> GetByIdAsync(long id, CancellationToken ct = default)
    {
        return await _context.HardwareInstances
            .AsNoTracking()
            .Include(h => h.Definition)
                .ThenInclude(d => d.Category)
            .Include(h => h.Definition)
                .ThenInclude(d => d.ControlProfile)
            .Include(h => h.Controller)
            .FirstOrDefaultAsync(h => h.Id == id, ct);
    }

    public async Task<List<HardwareInstance>> GetByControllerIdAsync(long controllerId, CancellationToken ct = default)
    {
        return await _context.HardwareInstances
            .AsNoTracking()
            .Include(h => h.Definition)
                .ThenInclude(d => d.Category)
            .Include(h => h.Definition)
                .ThenInclude(d => d.ControlProfile)
            .Where(h => h.ControllerId == controllerId)
            .ToListAsync(ct);
    }

    public async Task<HardwareInstance> CreateAsync(HardwareInstance entity, CancellationToken ct = default)
    {
        entity.CreatedAt = DateTime.UtcNow;
        entity.UpdatedAt = DateTime.UtcNow;
        _context.HardwareInstances.Add(entity);
        await _context.SaveChangesAsync(ct);
        return entity;
    }

    public async Task<HardwareInstance> UpdateAsync(HardwareInstance entity, CancellationToken ct = default)
    {
        entity.UpdatedAt = DateTime.UtcNow;
        _context.HardwareInstances.Update(entity);
        await _context.SaveChangesAsync(ct);
        return entity;
    }

    public async Task DeleteAsync(long id, CancellationToken ct = default)
    {
        var entity = await _context.HardwareInstances.FindAsync(new object[] { id }, ct);
        if (entity is not null)
        {
            _context.HardwareInstances.Remove(entity);
            await _context.SaveChangesAsync(ct);
        }
    }
}

/// <summary>
/// 模块服务：管理模块定义和步骤
/// </summary>
public class ModuleService
{
    private readonly HardwareModularWorkflowDbContext _context;

    public ModuleService(HardwareModularWorkflowDbContext context)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
    }

    public async Task<List<ModuleEntity>> GetAllAsync(CancellationToken ct = default)
    {
        return await _context.Modules
            .AsNoTracking()
            .Include(m => m.Steps)
                .ThenInclude(s => s.HardwareInstance)
                    .ThenInclude(h => h!.Definition)
            .Include(m => m.ResourceReservations)
                .ThenInclude(reservation => reservation.HardwareInstance)
            .OrderBy(m => m.Name)
            .ToListAsync(ct);
    }

    public async Task<ModuleEntity?> GetByIdAsync(long id, CancellationToken ct = default)
    {
        return await _context.Modules
            .AsNoTracking()
            .Include(m => m.Steps)
                .ThenInclude(s => s.HardwareInstance)
                    .ThenInclude(h => h!.Definition)
            .Include(m => m.ResourceReservations)
                .ThenInclude(reservation => reservation.HardwareInstance)
            .FirstOrDefaultAsync(m => m.Id == id, ct);
    }

    public async Task<ModuleEntity> CreateAsync(ModuleEntity entity, CancellationToken ct = default)
    {
        entity.CreatedAt = DateTime.UtcNow;
        entity.UpdatedAt = DateTime.UtcNow;
        _context.Modules.Add(entity);
        await _context.SaveChangesAsync(ct);
        return entity;
    }

    public async Task<ModuleEntity> UpdateAsync(ModuleEntity entity, CancellationToken ct = default)
    {
        entity.UpdatedAt = DateTime.UtcNow;
        _context.Modules.Update(entity);
        await _context.SaveChangesAsync(ct);
        return entity;
    }

    public async Task DeleteAsync(long id, CancellationToken ct = default)
    {
        var entity = await _context.Modules.FindAsync(new object[] { id }, ct);
        if (entity is not null)
        {
            _context.Modules.Remove(entity);
            await _context.SaveChangesAsync(ct);
        }
    }
}

/// <summary>
/// 流服务：管理流定义、模块关系和子流引用
/// </summary>
public class FlowService
{
    private readonly HardwareModularWorkflowDbContext _context;

    public FlowService(HardwareModularWorkflowDbContext context)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
    }

    public async Task<List<FlowEntity>> GetAllAsync(CancellationToken ct = default)
    {
        return await _context.Flows
            .AsNoTracking()
            .Include(f => f.ModuleRelations)
                .ThenInclude(r => r.Module)
            .Include(f => f.SubFlowReferences)
                .ThenInclude(r => r.SubFlow)
            .Include(f => f.SubFlowReferences)
                .ThenInclude(r => r.ParameterBindings)
            .Include(f => f.ResourceReservations)
                .ThenInclude(r => r.HardwareInstance)
            .Include(f => f.Parameters)
            .Include(f => f.GraphNodes)
                .ThenInclude(node => node.ParameterBindings)
            .Include(f => f.GraphEdges)
            .OrderBy(f => f.Name)
            .ToListAsync(ct);
    }

    public async Task<FlowEntity?> GetByIdAsync(long id, CancellationToken ct = default)
    {
        return await _context.Flows
            .AsNoTracking()
            .Include(f => f.ModuleRelations)
                .ThenInclude(r => r.Module)
                    .ThenInclude(m => m!.Steps)
                        .ThenInclude(s => s.HardwareInstance)
                            .ThenInclude(h => h!.Definition)
            .Include(f => f.ModuleRelations)
                .ThenInclude(r => r.Module)
                    .ThenInclude(m => m!.ResourceReservations)
                        .ThenInclude(reservation => reservation.HardwareInstance)
            .Include(f => f.GraphNodes)
                .ThenInclude(node => node.Module)
                    .ThenInclude(module => module!.Steps)
                        .ThenInclude(step => step.HardwareInstance)
                            .ThenInclude(hardware => hardware!.Definition)
            .Include(f => f.GraphNodes)
                .ThenInclude(node => node.Module)
                    .ThenInclude(module => module!.ResourceReservations)
                        .ThenInclude(reservation => reservation.HardwareInstance)
            .Include(f => f.GraphNodes)
                .ThenInclude(node => node.SubFlow)
            .Include(f => f.GraphNodes)
                .ThenInclude(node => node.ParameterBindings)
            .Include(f => f.GraphEdges)
            .Include(f => f.ResourceReservations)
                .ThenInclude(r => r.HardwareInstance)
            .Include(f => f.Parameters)
            .Include(f => f.SubFlowReferences)
                .ThenInclude(r => r.SubFlow)
            .Include(f => f.SubFlowReferences)
                .ThenInclude(r => r.ParameterBindings)
            .FirstOrDefaultAsync(f => f.Id == id, ct);
    }

    public async Task<FlowEntity> CreateAsync(FlowEntity entity, CancellationToken ct = default)
    {
        entity.CreatedAt = DateTime.UtcNow;
        entity.UpdatedAt = DateTime.UtcNow;
        _context.Flows.Add(entity);
        await _context.SaveChangesAsync(ct);
        return entity;
    }

    public async Task<FlowEntity> UpdateAsync(FlowEntity entity, CancellationToken ct = default)
    {
        entity.UpdatedAt = DateTime.UtcNow;
        _context.Flows.Update(entity);
        await _context.SaveChangesAsync(ct);
        return entity;
    }

    public async Task DeleteAsync(long id, CancellationToken ct = default)
    {
        var entity = await _context.Flows.FindAsync(new object[] { id }, ct);
        if (entity is not null)
        {
            _context.Flows.Remove(entity);
            await _context.SaveChangesAsync(ct);
        }
    }

    public async Task<List<FlowEntity>> GetReusableFlowsAsync(CancellationToken ct = default)
    {
        return await _context.Flows
            .AsNoTracking()
            .Where(f => f.IsReusable)
            .OrderBy(f => f.Name)
            .ToListAsync(ct);
    }
}

/// <summary>
/// 执行日志服务：记录和查询执行日志
/// </summary>
public class ExecutionLogService
{
    private readonly HardwareModularWorkflowDbContext _context;

    public ExecutionLogService(HardwareModularWorkflowDbContext context)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
    }

    public async Task<ExecutionLog> CreateAsync(ExecutionLog entity, CancellationToken ct = default)
    {
        _context.ExecutionLogs.Add(entity);
        await _context.SaveChangesAsync(ct);
        return entity;
    }

    public async Task CreateBatchAsync(IEnumerable<ExecutionLog> entities, CancellationToken ct = default)
    {
        _context.ExecutionLogs.AddRange(entities);
        await _context.SaveChangesAsync(ct);
    }

    public async Task<List<ExecutionLog>> GetByExecutionIdAsync(Guid executionId, CancellationToken ct = default)
    {
        return await _context.ExecutionLogs
            .AsNoTracking()
            .Where(e => e.ExecutionId == executionId)
            .OrderBy(e => e.ExecutedAt)
            .ToListAsync(ct);
    }

    public async Task<List<ExecutionLog>> GetBySessionIdAsync(Guid sessionId, CancellationToken ct = default)
    {
        return await _context.ExecutionLogs
            .AsNoTracking()
            .Where(e => e.ExecutionId == sessionId)
            .OrderBy(e => e.ExecutedAt)
            .ToListAsync(ct);
    }

    public async Task<List<ExecutionLog>> GetByHardwareInstanceAsync(long hardwareInstanceId, int limit = 100, CancellationToken ct = default)
    {
        return await _context.ExecutionLogs
            .AsNoTracking()
            .Where(e => e.HardwareInstanceId == hardwareInstanceId)
            .OrderByDescending(e => e.ExecutedAt)
            .Take(limit)
            .ToListAsync(ct);
    }

    public async Task<ExecutionSession> CreateSessionAsync(ExecutionSession session, CancellationToken ct = default)
    {
        _context.ExecutionSessions.Add(session);
        await _context.SaveChangesAsync(ct);
        return session;
    }

    public async Task<ExecutionSession?> GetSessionByIdAsync(Guid id, CancellationToken ct = default)
    {
        return await _context.ExecutionSessions
            .AsNoTracking()
            .Include(s => s.Logs)
            .FirstOrDefaultAsync(s => s.Id == id, ct);
    }

    public async Task<List<ExecutionSession>> GetRecentSessionsAsync(int limit = 50, CancellationToken ct = default)
    {
        return await _context.ExecutionSessions
            .AsNoTracking()
            .OrderByDescending(s => s.StartedAt)
            .Take(limit)
            .ToListAsync(ct);
    }

    public async Task CompleteSessionAsync(Guid sessionId, bool isSuccess, long? totalDurationMs, CancellationToken ct = default)
    {
        var session = await _context.ExecutionSessions.FindAsync(new object[] { sessionId }, ct);
        if (session is not null)
        {
            session.IsSuccess = isSuccess;
            session.TotalDurationMs = totalDurationMs;
            session.CompletedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync(ct);
        }
    }
}
