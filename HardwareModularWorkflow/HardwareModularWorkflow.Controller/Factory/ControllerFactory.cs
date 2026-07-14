using HardwareModularWorkflow.Hardware.Abstractions;
using HardwareModularWorkflow.Controller.Loader;
using HardwareModularWorkflow.Controller.Adapter;
using HardwareModularWorkflow.Controller.Plc;
using HardwareModularWorkflow.Controller.Can;

namespace HardwareModularWorkflow.Controller.Factory;

/// <summary>
/// 控制器工厂：根据配置创建具体的控制器实例
/// 
/// 重构后支持两种模式：
/// 1. 反射模式（新）：通过 VendorLibraryLoader 动态加载厂商 DLL，支持任意厂商
/// 2. 内联模式（旧）：直接创建硬编码的控制器实现（如 LeadSysPlcController / ZlgCanController）
/// 
/// 默认优先使用反射模式，若厂商库不可用则自动回退到模拟实现。
/// </summary>
public sealed class ControllerFactory : IDisposable
{
    private readonly VendorLibraryLoader? _vendorLoader;
    private readonly Dictionary<long, IController> _cachedControllers = new();
    private readonly SemaphoreSlim _cacheLock = new(1, 1);
    private bool _isDisposed;

    /// <summary>
    /// 创建控制器工厂（反射模式）
    /// </summary>
    /// <param name="vendorLoader">厂商库加载器，若提供则启用反射模式</param>
    public ControllerFactory(VendorLibraryLoader? vendorLoader = null)
    {
        _vendorLoader = vendorLoader;
    }

    /// <summary>
    /// 创建或获取已缓存的控制器
    /// </summary>
    public async Task<IController> GetOrCreateAsync(ControllerFactoryConfig config, CancellationToken ct = default)
    {
        await _cacheLock.WaitAsync(ct);
        try
        {
            if (_cachedControllers.TryGetValue(config.ControllerId, out var cached))
                return cached;

            IController controller = CreateController(config);
            await controller.ConnectAsync(ct);
            _cachedControllers[config.ControllerId] = controller;
            return controller;
        }
        finally
        {
            _cacheLock.Release();
        }
    }

    /// <summary>
    /// 关闭并移除指定控制器
    /// </summary>
    public async Task RemoveAsync(long controllerId, CancellationToken ct = default)
    {
        await _cacheLock.WaitAsync(ct);
        try
        {
            if (_cachedControllers.Remove(controllerId, out var controller))
            {
                await controller.DisposeAsync();
            }
        }
        finally
        {
            _cacheLock.Release();
        }
    }

    /// <summary>
    /// 关闭所有控制器并清空缓存
    /// </summary>
    public async Task ClearAsync(CancellationToken ct = default)
    {
        await _cacheLock.WaitAsync(ct);
        try
        {
            foreach (var (_, controller) in _cachedControllers)
            {
                await controller.DisposeAsync();
            }
            _cachedControllers.Clear();
        }
        finally
        {
            _cacheLock.Release();
        }
    }

    /// <summary>
    /// 创建控制器（优先反射模式，回退到模拟模式）
    /// </summary>
    private IController CreateController(ControllerFactoryConfig config)
    {
        var controllerType = config.ControllerType.ToLowerInvariant();
        var vendorName = config.VendorName;

        if (string.IsNullOrEmpty(vendorName))
        {
            // 未指定厂商：回退到默认模拟实现
            return controllerType switch
            {
                "plc" => CreateDefaultPlcController(config),
                "can" => CreateDefaultCanController(config),
                _ => throw new ArgumentException($"Unsupported controller type: {config.ControllerType}")
            };
        }

        // 尝试反射模式：查找 VendorLibraryLoader 中是否加载了该厂商
        if (_vendorLoader != null && _vendorLoader.IsVendorAvailable(vendorName))
        {
            var driver = _vendorLoader.GetDriver(vendorName);
            return controllerType switch
            {
                "plc" => CreateReflectivePlcController(vendorName, driver, config),
                "can" => CreateReflectiveCanController(vendorName, driver, config),
                _ => throw new ArgumentException($"Unsupported controller type: {config.ControllerType}")
            };
        }

        // 厂商未在 VendorLibraryLoader 中加载：回退到模拟模式
        return controllerType switch
        {
            "plc" => CreateDefaultPlcController(config),
            "can" => CreateDefaultCanController(config),
            _ => throw new ArgumentException($"Unsupported controller type: {config.ControllerType}")
        };
    }

    #region Reflective Controllers (New)

    private static ReflectivePlcController CreateReflectivePlcController(string vendorId, IVendorLibraryDriver? driver, ControllerFactoryConfig config)
    {
        var controller = new ReflectivePlcController(vendorId, driver);
        controller.Initialize(new Dictionary<string, object>(config.Parameters, StringComparer.OrdinalIgnoreCase));
        return controller;
    }

    private static ReflectiveCanController CreateReflectiveCanController(string vendorId, IVendorLibraryDriver? driver, ControllerFactoryConfig config)
    {
        var controller = new ReflectiveCanController(vendorId, driver);
        controller.Initialize(new Dictionary<string, object>(config.Parameters, StringComparer.OrdinalIgnoreCase));
        return controller;
    }

    #endregion

    #region Default Fallback Controllers (Legacy Simulation)

    private static IPlcController CreateDefaultPlcController(ControllerFactoryConfig config)
    {
        var plcConfig = new LeadSysPlcControllerConfig
        {
            IpAddress = config.GetParameter<string>("IpAddress"),
            Port = config.GetParameter<int>("Port", 502),
            ComPort = config.GetParameter<string>("ComPort"),
            BaudRate = config.GetParameter<int>("BaudRate", 115200),
            StationId = config.GetParameter<byte>("StationId", 1),
            ConnectTimeoutMs = config.GetParameter<int>("ConnectTimeoutMs", 5000),
            ReadWriteTimeoutMs = config.GetParameter<int>("ReadWriteTimeoutMs", 3000)
        };
        return new LeadSysPlcController(plcConfig);
    }

    private static ICanController CreateDefaultCanController(ControllerFactoryConfig config)
    {
        var canConfig = new ZlgCanControllerConfig
        {
            DeviceType = config.GetParameter<uint>("DeviceType", ZlgCanNative.ZCAN_USBCAN_2E_U),
            DeviceIndex = config.GetParameter<uint>("DeviceIndex", 0),
            ChannelIndex = config.GetParameter<uint>("ChannelIndex", 0),
            CanFd = config.GetParameter<bool>("CanFd", false),
            CanFdBrs = config.GetParameter<bool>("CanFdBrs", false),
            ReadTimeoutMs = config.GetParameter<int>("ReadTimeoutMs", 1000),
            DeviceName = config.GetParameter<string>("DeviceName")
        };
        return new ZlgCanController(canConfig);
    }

    #endregion

    public void Dispose()
    {
        if (_isDisposed) return;
        _isDisposed = true;
        _cacheLock.Wait();
        try
        {
            foreach (var (_, controller) in _cachedControllers)
            {
                try { controller.DisposeAsync().AsTask().Wait(); } catch { }
            }
            _cachedControllers.Clear();
        }
        finally
        {
            _cacheLock.Release();
            _cacheLock.Dispose();
        }
    }
}

/// <summary>
/// 控制器工厂配置：从数据库加载的控制器配置信息
/// </summary>
public sealed class ControllerFactoryConfig
{
    /// <summary>控制器唯一 ID（数据库主键）</summary>
    public long ControllerId { get; set; }

    /// <summary>控制器类型："Plc" 或 "Can"</summary>
    public required string ControllerType { get; set; }

    /// <summary>厂商名称（如 LeadSys / ZLG / Siemens / Beckhoff）</summary>
    public string VendorName { get; set; } = string.Empty;

    /// <summary>扩展参数字典（存储连接参数、波特率、IP 等）</summary>
    public Dictionary<string, object> Parameters { get; set; } = new();

    public T GetParameter<T>(string key, T defaultValue = default!)
    {
        if (Parameters.TryGetValue(key, out var value) && value is T typedValue)
            return typedValue;
        return defaultValue;
    }
}
