using HardwareModularWorkflow.Hardware.Abstractions;
using HardwareModularWorkflow.Hardware.Enums;
using HardwareModularWorkflow.Hardware.Results;
using HardwareModularWorkflow.Workflow.Abstractions;
using HardwareModularWorkflow.Workflow.Events;
using HardwareModularWorkflow.Workflow.Engine;
using HardwareModularWorkflow.Workflow.Models;
using HardwareModularWorkflow.Workflow.Resources;
using HardwareModularWorkflow.Workflow.Scheduling;
using HardwareModularWorkflow.Core.Orchestration;
using HardwareModularWorkflow.Core.Events;
using HardwareModularWorkflow.Core.Services;
using HardwareModularWorkflow.Db.DbContext;
using HardwareModularWorkflow.Db.Entities;
using HardwareModularWorkflow.Db.Services;
using HardwareModularWorkflow.Controller.Loader;
using HardwareModularWorkflow.Controller.Factory;
using Microsoft.Extensions.DependencyInjection;

namespace HardwareModularWorkflow.Core.DependencyInjection;

/// <summary>
/// Core 层依赖注入服务注册
/// 协调 Hardware、Controller、Workflow、Db 各层的依赖注入
/// </summary>
public static class ServiceRegistration
{
    /// <summary>
    /// 注册所有 Core 层服务（应在 Wpf App.xaml.cs 中调用）
    /// </summary>
    public static IServiceCollection AddHardwareModularWorkflowCore(this IServiceCollection services, string? sqliteDbPath = null, string? sqlServerConnectionString = null)
    {
        // --- 1. 数据库上下文 ---
        if (!string.IsNullOrEmpty(sqlServerConnectionString))
        {
            services.AddScoped<HardwareModularWorkflowDbContext>(provider =>
                new HardwareModularWorkflowDbContext(
                    HardwareModularWorkflowDbContext.CreateSqlServerOptions(sqlServerConnectionString)));
        }
        else
        {
            services.AddScoped<HardwareModularWorkflowDbContext>(provider =>
                new HardwareModularWorkflowDbContext(
                    HardwareModularWorkflowDbContext.CreateSQLiteOptions(sqliteDbPath ?? "hardware_workflow.db")));
        }

        // --- 2. 数据库服务 ---
        services.AddScoped<ControllerService>();
        services.AddScoped<HardwareCatalogService>();
        services.AddScoped<HardwareDefinitionService>();
        services.AddScoped<HardwareInstanceService>();
        services.AddScoped<ModuleService>();
        services.AddScoped<FlowService>();
        services.AddScoped<ExecutionLogService>();
        services.AddScoped<WorkflowRunSnapshotService>();

        // --- 3. 控制器层 ---
        services.AddSingleton<VendorLibraryLoader>(provider =>
        {
            var loader = new VendorLibraryLoader(VendorLibraryLoader.GetDefaultVendorLibrariesPath());
            // 启动时异步扫描（不阻塞 DI 容器构建）
            _ = Task.Run(async () => await loader.ScanAndLoadAllAsync());
            return loader;
        });
        services.AddSingleton<ControllerFactory>(provider =>
        {
            var vendorLoader = provider.GetService<VendorLibraryLoader>();
            return new ControllerFactory(vendorLoader);
        });
        services.AddSingleton<Controller.Factory.ControllerFactory>();

        // --- 4. Core 层编排 ---
        services.AddSingleton<HardwareProfileDriverRegistry>();
        services.AddSingleton<HardwareDriverFactory>();
        services.AddSingleton<CoreStepExecutor>();
        services.AddSingleton<IStepExecutor>(provider => provider.GetRequiredService<CoreStepExecutor>());
        services.AddSingleton<IFlowResolver, FlowResolver>();
        services.AddSingleton<ISchedulingEventLog, InMemorySchedulingEventLog>();
        services.AddSingleton<IResourceReservationManager>(provider =>
            new ResourceReservationManager(
                eventLog: provider.GetRequiredService<ISchedulingEventLog>()));
        services.AddSingleton<WorkflowRuntimeService>();

        // --- 5. 事件总线 ---
        services.AddSingleton<IEventBus, EventBus>();

        // --- 6. 工作流调度器（注入 IStepExecutor 和 IFlowResolver 后创建）---
        services.AddSingleton<WorkflowScheduler>(provider =>
        {
            var stepExecutor = provider.GetRequiredService<IStepExecutor>();
            var flowResolver = provider.GetService<IFlowResolver>();
            var resourceReservationManager = provider.GetRequiredService<IResourceReservationManager>();
            var eventLog = provider.GetRequiredService<ISchedulingEventLog>();
            var scheduler = new WorkflowScheduler(
                stepExecutor,
                flowResolver,
                maxConcurrency: 50,
                channelCapacity: 1000,
                resourceReservationManager,
                eventLog);
            return scheduler;
        });

        return services;
    }

    /// <summary>
    /// 初始化数据库（创建表、种子数据）
    /// </summary>
    public static async Task InitializeDatabaseAsync(this IServiceProvider provider)
    {
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<HardwareModularWorkflowDbContext>();
        await dbContext.Database.EnsureCreatedAsync();
        await HardwareCatalogInitializer.InitializeAsync(dbContext);
    }
}
