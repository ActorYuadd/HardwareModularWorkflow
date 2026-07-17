using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using HardwareModularWorkflow.Core.DependencyInjection;
using HardwareModularWorkflow.Core.Services;
using HardwareModularWorkflow.Wpf.ViewModels;
using HardwareModularWorkflow.Wpf.Views;

namespace HardwareModularWorkflow.Wpf;

/// <summary>
/// WPF 应用程序入口
/// 配置依赖注入容器、初始化数据库、启动主窗口
/// </summary>
public partial class App : Application
{
    private ServiceProvider? _serviceProvider;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // 1. 配置 DI 容器
        var services = new ServiceCollection();
        ConfigureServices(services);
        _serviceProvider = services.BuildServiceProvider();

        // 2. 初始化数据库
        await _serviceProvider.InitializeDatabaseAsync();

        // 3. 启动工作流运行时服务
        var runtimeService = _serviceProvider.GetRequiredService<WorkflowRuntimeService>();
        runtimeService.Start();

        // 3.5. 加载用户设置（语言、主题等）
        var settingsViewModel = _serviceProvider.GetRequiredService<SettingsViewModel>();
        await settingsViewModel.LoadSettingsCommand.ExecuteAsync(null);

        // 4. 启动主窗口
        var mainWindow = _serviceProvider.GetRequiredService<MainWindow>();
        mainWindow.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        var serviceProvider = Interlocked.Exchange(ref _serviceProvider, null);
        if (serviceProvider is not null)
        {
            serviceProvider.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }

        base.OnExit(e);
    }

    /// <summary>
    /// 配置服务注册
    /// </summary>
    private static void ConfigureServices(IServiceCollection services)
    {
        // Core 层所有服务
        services.AddHardwareModularWorkflowCore(
            sqliteDbPath: "hardware_workflow.db");

        // WPF ViewModel
        services.AddSingleton<DashboardViewModel>();
        services.AddSingleton<HardwareViewModel>();
        services.AddSingleton<HardwareDefinitionViewModel>();
        services.AddSingleton<ControllerViewModel>();
        services.AddSingleton<ModuleViewModel>();
        services.AddSingleton<FlowViewModel>();
        services.AddSingleton<MonitorViewModel>();
        services.AddSingleton<ExecutionLogViewModel>();
        services.AddSingleton<SettingsViewModel>();
        services.AddSingleton<MainWindowViewModel>();
        services.AddSingleton<MainWindow>(provider =>
        {
            var viewModel = provider.GetRequiredService<MainWindowViewModel>();
            return new MainWindow(viewModel);
        });
    }

    /// <summary>
    /// 全局获取服务（供 XAML/CodeBehind 使用）
    /// </summary>
    public static T GetService<T>() where T : class
    {
        if (Current is not App app || app._serviceProvider is null)
            throw new InvalidOperationException("Service provider is not initialized");
        return app._serviceProvider.GetRequiredService<T>();
    }
}
