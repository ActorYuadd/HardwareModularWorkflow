using HardwareModularWorkflow.Hardware.Abstractions;
using HardwareModularWorkflow.Hardware.Enums;
using HardwareModularWorkflow.Hardware.Models;
using HardwareModularWorkflow.Hardware.Results;
using HardwareModularWorkflow.Controller.Factory;
using HardwareModularWorkflow.Db.DbContext;
using HardwareModularWorkflow.Db.Entities;
using HardwareModularWorkflow.Db.Services;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace HardwareModularWorkflow.Core.Orchestration;

/// <summary>
/// 硬件驱动工厂：根据硬件实例创建对应的 IHardwareDriver
/// 运行时由 CoreStepExecutor 调用，注入具体的 Controller 实现
/// </summary>
public sealed class HardwareDriverFactory
{
    private readonly ControllerFactory _controllerFactory;
    private readonly HardwareInstanceService _hardwareInstanceService;
    private readonly ControllerService _controllerService;
    private readonly HardwareProfileDriverRegistry _profileDriverRegistry;
    private readonly Dictionary<long, IHardwareDriver> _driverCache = new();
    private readonly SemaphoreSlim _cacheLock = new(1, 1);

    public HardwareDriverFactory(
        ControllerFactory controllerFactory,
        HardwareInstanceService hardwareInstanceService,
        ControllerService controllerService,
        HardwareProfileDriverRegistry profileDriverRegistry)
    {
        _controllerFactory = controllerFactory ?? throw new ArgumentNullException(nameof(controllerFactory));
        _hardwareInstanceService = hardwareInstanceService ?? throw new ArgumentNullException(nameof(hardwareInstanceService));
        _controllerService = controllerService ?? throw new ArgumentNullException(nameof(controllerService));
        _profileDriverRegistry = profileDriverRegistry
            ?? throw new ArgumentNullException(nameof(profileDriverRegistry));
    }

    /// <summary>
    /// 获取或创建硬件驱动
    /// </summary>
    public async Task<IHardwareDriver> GetOrCreateDriverAsync(long hardwareInstanceId, CancellationToken ct = default)
    {
        await _cacheLock.WaitAsync(ct);
        try
        {
            if (_driverCache.TryGetValue(hardwareInstanceId, out var cachedDriver))
                return cachedDriver;

            // 1. 从数据库加载硬件实例（包含 Definition 和 Controller）
            var instance = await _hardwareInstanceService.GetByIdAsync(hardwareInstanceId, ct);
            if (instance is null)
                throw new InvalidOperationException($"Hardware instance {hardwareInstanceId} not found in database.");
            var profile = instance.Definition.ControlProfile
                ?? throw new InvalidOperationException(
                    $"Hardware definition '{instance.Definition.Name}' has no control profile.");

            // 2. 创建 IHardware 模型
            var hardware = CreateHardwareModel(instance);

            // 3. 创建控制器（如果绑定了 ControllerId）
            IController? controller = null;
            if (instance.ControllerId.HasValue)
            {
                var controllerConfig = await GetControllerConfigAsync(instance.ControllerId.Value, ct);
                controller = await _controllerFactory.GetOrCreateAsync(controllerConfig, ct);
            }

            // 4. The profile selects the installed driver and validates controller compatibility.
            var driver = _profileDriverRegistry.CreateDriver(hardware, profile, controller);
            _driverCache[hardwareInstanceId] = driver;
            return driver;
        }
        finally
        {
            _cacheLock.Release();
        }
    }

    /// <summary>
    /// 移除指定硬件驱动缓存
    /// </summary>
    public async Task RemoveDriverAsync(long hardwareInstanceId, CancellationToken ct = default)
    {
        await _cacheLock.WaitAsync(ct);
        try
        {
            if (_driverCache.Remove(hardwareInstanceId, out var driver))
            {
                // 尝试停止驱动
                if (driver is IAsyncDisposable disposable)
                {
                    await disposable.DisposeAsync();
                }
            }
        }
        finally
        {
            _cacheLock.Release();
        }
    }

    /// <summary>
    /// 获取指定硬件驱动的实时状态
    /// </summary>
    public async Task<HardwareState> GetDriverStateAsync(long hardwareInstanceId, CancellationToken ct = default)
    {
        await _cacheLock.WaitAsync(ct);
        try
        {
            if (_driverCache.TryGetValue(hardwareInstanceId, out var driver))
            {
                try
                {
                    return await driver.GetStateAsync(ct);
                }
                catch
                {
                    return HardwareState.Offline;
                }
            }
            return HardwareState.Offline;
        }
        finally
        {
            _cacheLock.Release();
        }
    }

    /// <summary>
    /// 获取所有已缓存硬件驱动的实时状态
    /// </summary>
    public async Task<Dictionary<long, HardwareState>> GetAllDriverStatesAsync(CancellationToken ct = default)
    {
        await _cacheLock.WaitAsync(ct);
        try
        {
            var result = new Dictionary<long, HardwareState>(_driverCache.Count);
            foreach (var (id, driver) in _driverCache)
            {
                try
                {
                    result[id] = await driver.GetStateAsync(ct);
                }
                catch
                {
                    result[id] = HardwareState.Offline;
                }
            }
            return result;
        }
        finally
        {
            _cacheLock.Release();
        }
    }

    /// <summary>
    /// 当前已缓存的驱动数量
    /// </summary>
    public int CachedDriverCount
    {
        get
        {
            if (_cacheLock.Wait(0))
            {
                try
                {
                    return _driverCache.Count;
                }
                finally
                {
                    _cacheLock.Release();
                }
            }
            return -1; // 获取锁失败
        }
    }

    /// <summary>
    /// 清空所有驱动缓存
    /// </summary>
    public async Task ClearAsync(CancellationToken ct = default)
    {
        await _cacheLock.WaitAsync(ct);
        try
        {
            foreach (var (_, driver) in _driverCache)
            {
                if (driver is IAsyncDisposable disposable)
                {
                    await disposable.DisposeAsync();
                }
            }
            _driverCache.Clear();
        }
        finally
        {
            _cacheLock.Release();
        }
    }

    /// <summary>
    /// 从数据库实例创建 Hardware 模型（公共静态方法，供 FlowResolver 等复用）
    /// </summary>
    public static IHardware CreateHardwareModel(HardwareInstance instance)
    {
        var parameters = string.IsNullOrEmpty(instance.ParametersJson)
            ? new Dictionary<string, object>()
            : JsonSerializer.Deserialize<Dictionary<string, object>>(instance.ParametersJson)
              ?? new Dictionary<string, object>();

        var categoryCode = instance.Definition?.Category?.Code
            ?? instance.Definition?.Type
            ?? "Custom";
        var driverKey = instance.Definition?.ControlProfile?.DriverKey ?? string.Empty;

        // Category controls the compatibility model. Profile controls execution through the registry.
        HardwareBase hardware = categoryCode.ToUpperInvariant() switch
        {
            "MOTOR" => new MotorHardware(),
            "TEMPERATURE" => new TemperatureHardware(),
            "COOLING" => new CoolingHardware(),
            "CUSTOM" => new CustomHardware(),
            _ => new CustomHardware()
        };

        hardware.Id = instance.Id;
        hardware.Name = instance.Name;
        hardware.Alias = instance.Alias ?? string.Empty;
        hardware.Note = instance.Note ?? string.Empty;
        hardware.Port = instance.Port ?? string.Empty;
        hardware.Address = instance.Address ?? string.Empty;
        hardware.Parameters = parameters;
        hardware.ControllerId = instance.ControllerId;
        hardware.ControllerType = instance.ControllerType;
        hardware.CategoryCode = categoryCode;
        hardware.DriverKey = driverKey;

        return hardware;
    }

    /// <summary>
    /// 从数据库加载控制器配置
    /// </summary>
    private async Task<ControllerFactoryConfig> GetControllerConfigAsync(long controllerId, CancellationToken ct)
    {
        var entity = await _controllerService.GetByIdAsync(controllerId, ct);
        if (entity is null)
            throw new InvalidOperationException($"Controller {controllerId} not found in database.");

        var parameters = string.IsNullOrEmpty(entity.ConnectionConfigJson)
            ? new Dictionary<string, object>()
            : JsonSerializer.Deserialize<Dictionary<string, object>>(entity.ConnectionConfigJson)
              ?? new Dictionary<string, object>();

        return new ControllerFactoryConfig
        {
            ControllerId = entity.Id,
            ControllerType = entity.ControllerType,
            VendorName = entity.VendorName,
            Parameters = parameters
        };
    }
}

/// <summary>
/// Core 层硬件驱动实现：连接 Hardware 模型和 Controller
/// </summary>
public sealed class CoreHardwareDriver : IHardwareDriver, IAsyncDisposable
{
    private readonly IHardware _hardware;
    private readonly IController? _controller;

    public CoreHardwareDriver(IHardware hardware, IController? controller)
    {
        _hardware = hardware ?? throw new ArgumentNullException(nameof(hardware));
        _controller = controller;
    }

    public IHardware Hardware => _hardware;

    public async Task<CommandResult> ExecuteAsync(IHardwareCommand command, CancellationToken ct = default)
    {
        if (_controller is null)
            return CommandResult.Offline(TimeSpan.Zero);

        var start = DateTime.UtcNow;

        try
        {
            // 将语义命令翻译为控制器指令（Controller 层负责具体翻译）
            var commandData = SerializeCommand(command);
            var response = await _controller.SendCommandAsync(commandData, ct);

            var duration = DateTime.UtcNow - start;
            return CommandResult.Success(duration, response);
        }
        catch (OperationCanceledException)
        {
            return CommandResult.Cancelled(DateTime.UtcNow - start);
        }
        catch (Exception ex)
        {
            return CommandResult.Failed(DateTime.UtcNow - start, "DRIVER_ERROR", ex.Message);
        }
    }

    public async Task<HardwareState> GetStateAsync(CancellationToken ct = default)
    {
        if (_controller is null)
            return HardwareState.Offline;

        try
        {
            // 读取硬件状态寄存器
            var stateValue = await _controller.ReadRegisterAsync<int>(_hardware.Address, ct);
            return Enum.IsDefined(typeof(HardwareState), stateValue)
                ? (HardwareState)stateValue
                : HardwareState.Idle;
        }
        catch
        {
            return HardwareState.Offline;
        }
    }

    public async Task<bool> TryStopAsync(CancellationToken ct = default)
    {
        if (_controller is IStoppable stoppable)
        {
            return await stoppable.TryStopAsync(ct);
        }
        return false;
    }

    public async ValueTask DisposeAsync()
    {
        // 不释放控制器（由 ControllerFactory 统一管理）
        await Task.CompletedTask;
    }

    /// <summary>
    /// 将语义命令序列化为字节数组，供 Controller 层翻译
    /// 格式：[CommandNameLength:1][CommandName:N][ParamCount:1][[KeyLength:1][Key:N][ValueLength:4][Value:N]...]
    /// </summary>
    private static byte[] SerializeCommand(IHardwareCommand command)
    {
        using var ms = new System.IO.MemoryStream();
        using var writer = new System.IO.BinaryWriter(ms);

        // 写入命令名
        var nameBytes = System.Text.Encoding.UTF8.GetBytes(command.CommandName);
        writer.Write((byte)nameBytes.Length);
        writer.Write(nameBytes);

        // 写入参数
        var paramCount = command.Parameters?.Count ?? 0;
        writer.Write((byte)paramCount);

        if (command.Parameters is not null)
        {
            foreach (var (key, value) in command.Parameters)
            {
                var keyBytes = System.Text.Encoding.UTF8.GetBytes(key);
                writer.Write((byte)keyBytes.Length);
                writer.Write(keyBytes);

                var valueJson = JsonSerializer.Serialize(value);
                var valueBytes = System.Text.Encoding.UTF8.GetBytes(valueJson);
                writer.Write(valueBytes.Length);
                writer.Write(valueBytes);
            }
        }

        writer.Flush();
        return ms.ToArray();
    }
}
