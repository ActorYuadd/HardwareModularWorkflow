using Microsoft.EntityFrameworkCore;
using HardwareModularWorkflow.Db.Entities;

namespace HardwareModularWorkflow.Db.DbContext;

/// <summary>
/// EF Core DbContext：硬件模块化工作流数据库上下文
/// 支持 SQLite（本地）和 SQL Server（云端/服务器）
/// </summary>
public class HardwareModularWorkflowDbContext : Microsoft.EntityFrameworkCore.DbContext
{
    // --- DbSets ---
    public DbSet<ControllerEntity> Controllers { get; set; } = null!;
    public DbSet<HardwareDefinition> HardwareDefinitions { get; set; } = null!;
    public DbSet<HardwareInstance> HardwareInstances { get; set; } = null!;
    public DbSet<ModuleEntity> Modules { get; set; } = null!;
    public DbSet<ModuleStepEntity> ModuleSteps { get; set; } = null!;
    public DbSet<FlowEntity> Flows { get; set; } = null!;
    public DbSet<FlowModuleRelation> FlowModuleRelations { get; set; } = null!;
    public DbSet<FlowSubFlowReference> FlowSubFlowReferences { get; set; } = null!;
    public DbSet<ExecutionLog> ExecutionLogs { get; set; } = null!;
    public DbSet<ExecutionSession> ExecutionSessions { get; set; } = null!;

    public HardwareModularWorkflowDbContext(DbContextOptions<HardwareModularWorkflowDbContext> options) : base(options)
    {
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // --- ControllerEntity ---
        modelBuilder.Entity<ControllerEntity>(entity =>
        {
            entity.ToTable("Controllers");
            entity.HasIndex(e => e.ControllerType).HasDatabaseName("IX_Controllers_Type");
            entity.HasIndex(e => e.VendorName).HasDatabaseName("IX_Controllers_Vendor");
            entity.HasIndex(e => e.IsEnabled).HasDatabaseName("IX_Controllers_Enabled");
            entity.HasIndex(e => e.IsDefault).HasDatabaseName("IX_Controllers_Default");
        });

        // --- HardwareDefinition ---
        modelBuilder.Entity<HardwareDefinition>(entity =>
        {
            entity.ToTable("HardwareDefinitions");
            entity.HasIndex(e => e.Type).HasDatabaseName("IX_HardwareDefinitions_Type");
            entity.HasIndex(e => e.IsSystem).HasDatabaseName("IX_HardwareDefinitions_System");
        });

        // --- HardwareInstance ---
        modelBuilder.Entity<HardwareInstance>(entity =>
        {
            entity.ToTable("HardwareInstances");
            entity.HasIndex(e => e.DefinitionId).HasDatabaseName("IX_HardwareInstances_DefinitionId");
            entity.HasIndex(e => e.ControllerId).HasDatabaseName("IX_HardwareInstances_ControllerId");
            entity.HasIndex(e => e.ControllerType).HasDatabaseName("IX_HardwareInstances_ControllerType");
            entity.HasIndex(e => e.IsEnabled).HasDatabaseName("IX_HardwareInstances_Enabled");
            entity.HasIndex(e => e.Name).HasDatabaseName("IX_HardwareInstances_Name");

            entity.HasOne(e => e.Controller)
                  .WithMany(c => c.HardwareInstances)
                  .HasForeignKey(e => e.ControllerId)
                  .OnDelete(DeleteBehavior.SetNull);
        });

        // --- ModuleEntity ---
        modelBuilder.Entity<ModuleEntity>(entity =>
        {
            entity.ToTable("Modules");
            entity.HasIndex(e => e.Name).HasDatabaseName("IX_Modules_Name");
            entity.HasIndex(e => e.DefaultExecutionMode).HasDatabaseName("IX_Modules_ExecutionMode");
        });

        // --- ModuleStepEntity ---
        modelBuilder.Entity<ModuleStepEntity>(entity =>
        {
            entity.ToTable("ModuleSteps");
            entity.HasIndex(e => e.ModuleId).HasDatabaseName("IX_ModuleSteps_ModuleId");
            entity.HasIndex(e => e.HardwareInstanceId).HasDatabaseName("IX_ModuleSteps_HardwareInstanceId");
            entity.HasIndex(e => e.OrderIndex).HasDatabaseName("IX_ModuleSteps_OrderIndex");
        });

        // --- FlowEntity ---
        modelBuilder.Entity<FlowEntity>(entity =>
        {
            entity.ToTable("Flows");
            entity.HasIndex(e => e.Name).HasDatabaseName("IX_Flows_Name");
            entity.HasIndex(e => e.ExecutionMode).HasDatabaseName("IX_Flows_ExecutionMode");
            entity.HasIndex(e => e.IsReusable).HasDatabaseName("IX_Flows_Reusable");
        });

        // --- FlowModuleRelation ---
        modelBuilder.Entity<FlowModuleRelation>(entity =>
        {
            entity.ToTable("FlowModuleRelations");
            entity.HasIndex(e => new { e.FlowId, e.OrderIndex }).HasDatabaseName("IX_FlowModuleRelations_FlowId_Order");
            entity.HasIndex(e => e.ModuleId).HasDatabaseName("IX_FlowModuleRelations_ModuleId");
        });

        // --- FlowSubFlowReference ---
        modelBuilder.Entity<FlowSubFlowReference>(entity =>
        {
            entity.ToTable("FlowSubFlowReferences");
            entity.HasIndex(e => e.ParentFlowId).HasDatabaseName("IX_FlowSubFlowReferences_ParentFlowId");
            entity.HasIndex(e => e.SubFlowId).HasDatabaseName("IX_FlowSubFlowReferences_SubFlowId");
            // 防止自引用（A → A）的复合唯一索引
            entity.HasIndex(e => new { e.ParentFlowId, e.SubFlowId }).IsUnique().HasDatabaseName("IX_FlowSubFlowReferences_Parent_Sub");

            entity.HasOne(e => e.ParentFlow)
                  .WithMany(f => f.SubFlowReferences)
                  .HasForeignKey(e => e.ParentFlowId)
                  .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.SubFlow)
                  .WithMany(f => f.ParentFlowReferences)
                  .HasForeignKey(e => e.SubFlowId)
                  .OnDelete(DeleteBehavior.Restrict);
        });

        // --- ExecutionLog ---
        modelBuilder.Entity<ExecutionLog>(entity =>
        {
            entity.ToTable("ExecutionLogs");
            entity.HasIndex(e => e.ExecutionId).HasDatabaseName("IX_ExecutionLogs_ExecutionId");
            entity.HasIndex(e => e.LogType).HasDatabaseName("IX_ExecutionLogs_LogType");
            entity.HasIndex(e => e.FlowId).HasDatabaseName("IX_ExecutionLogs_FlowId");
            entity.HasIndex(e => e.ModuleId).HasDatabaseName("IX_ExecutionLogs_ModuleId");
            entity.HasIndex(e => e.HardwareInstanceId).HasDatabaseName("IX_ExecutionLogs_HardwareInstanceId");
            entity.HasIndex(e => e.ExecutedAt).HasDatabaseName("IX_ExecutionLogs_ExecutedAt");
            entity.HasIndex(e => e.IsSuccess).HasDatabaseName("IX_ExecutionLogs_IsSuccess");
            entity.HasIndex(e => e.ExecutionId).HasDatabaseName("IX_ExecutionLogs_SessionId");
        });

        // --- ExecutionSession ---
        modelBuilder.Entity<ExecutionSession>(entity =>
        {
            entity.ToTable("ExecutionSessions");
            entity.HasIndex(e => e.StartedAt).HasDatabaseName("IX_ExecutionSessions_StartedAt");
            entity.HasIndex(e => e.IsSuccess).HasDatabaseName("IX_ExecutionSessions_IsSuccess");
            entity.HasIndex(e => e.IsCancelled).HasDatabaseName("IX_ExecutionSessions_IsCancelled");
        });
    }

    /// <summary>
    /// 使用 SQLite 配置（本地开发/测试）
    /// </summary>
    public static DbContextOptions<HardwareModularWorkflowDbContext> CreateSQLiteOptions(string dbPath = "hardware_workflow.db")
    {
        var builder = new DbContextOptionsBuilder<HardwareModularWorkflowDbContext>();
        builder.UseSqlite($"Data Source={dbPath}");
        return builder.Options;
    }

    /// <summary>
    /// 使用 SQL Server 配置（生产环境）
    /// </summary>
    public static DbContextOptions<HardwareModularWorkflowDbContext> CreateSqlServerOptions(string connectionString)
    {
        var builder = new DbContextOptionsBuilder<HardwareModularWorkflowDbContext>();
        builder.UseSqlServer(connectionString);
        return builder.Options;
    }
}
