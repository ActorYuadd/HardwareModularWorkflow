using HardwareModularWorkflow.Hardware.Abstractions;
using HardwareModularWorkflow.Hardware.Results;

namespace HardwareModularWorkflow.Controller.Adapter;

/// <summary>
/// 控制器适配器接口
/// 
/// 设计意图：
/// - 将厂商特定的 DLL 调用封装为统一的 IController 接口
/// - 底层通过 IVendorLibraryDriver（反射/动态加载）调用厂商 API
/// - 上层（Core 层）无需关心具体厂商，只与 IController 交互
/// 
/// 与 IController 的关系：
/// - IController 是 Hardware 层的抽象（发送命令、读写寄存器）
/// - IControllerAdapter 是 Controller 层的实现（具体调用厂商 DLL）
/// </summary>
public interface IControllerAdapter : IController, IStoppable
{
    /// <summary>适配器使用的厂商 ID</summary>
    string VendorId { get; }

    /// <summary>控制器配置参数（从数据库加载）</summary>
    Dictionary<string, object> Parameters { get; }

    /// <summary>初始化适配器（传入配置和厂商驱动）</summary>
    void Initialize(Dictionary<string, object> parameters);
}

/// <summary>
/// PLC 控制器适配器基类
/// 子类只需实现具体厂商的 Connect/Disconnect/SendCommand/ReadRegister/WriteRegister
/// </summary>
public abstract class PlcControllerAdapterBase : IControllerAdapter
{
    protected readonly SemaphoreSlim _lock = new(1, 1);
    protected bool _isDisposed;

    public abstract string ControllerType { get; }
    public abstract string VendorName { get; }
    public abstract string VendorId { get; }
    public abstract bool IsConnected { get; }

    public Dictionary<string, object> Parameters { get; protected set; } = new();

    public void Initialize(Dictionary<string, object> parameters)
    {
        Parameters = new Dictionary<string, object>(parameters, StringComparer.OrdinalIgnoreCase);
    }

    public abstract Task ConnectAsync(CancellationToken ct = default);
    public abstract Task DisconnectAsync(CancellationToken ct = default);
    public abstract Task<byte[]> SendCommandAsync(byte[] data, CancellationToken ct = default);
    public abstract Task<T> ReadRegisterAsync<T>(string address, CancellationToken ct = default);
    public abstract Task WriteRegisterAsync<T>(string address, T value, CancellationToken ct = default);
    public abstract Task<bool> TryStopAsync(CancellationToken ct = default);

    public virtual async ValueTask DisposeAsync()
    {
        if (_isDisposed) return;
        _isDisposed = true;
        await DisconnectAsync();
        _lock.Dispose();
    }

    protected T GetParameter<T>(string key, T defaultValue = default!)
    {
        if (Parameters.TryGetValue(key, out var value) && value is T typedValue)
            return typedValue;
        return defaultValue;
    }

    protected void EnsureConnected()
    {
        if (!IsConnected)
            throw new InvalidOperationException($"{VendorName} PLC controller is not connected. Call ConnectAsync first.");
    }
}

/// <summary>
/// CAN 控制器适配器基类
/// </summary>
public abstract class CanControllerAdapterBase : IControllerAdapter
{
    protected readonly SemaphoreSlim _lock = new(1, 1);
    protected bool _isDisposed;

    public abstract string ControllerType { get; }
    public abstract string VendorName { get; }
    public abstract string VendorId { get; }
    public abstract bool IsConnected { get; }

    public Dictionary<string, object> Parameters { get; protected set; } = new();

    public void Initialize(Dictionary<string, object> parameters)
    {
        Parameters = new Dictionary<string, object>(parameters, StringComparer.OrdinalIgnoreCase);
    }

    public abstract Task ConnectAsync(CancellationToken ct = default);
    public abstract Task DisconnectAsync(CancellationToken ct = default);
    public abstract Task<byte[]> SendCommandAsync(byte[] data, CancellationToken ct = default);
    public abstract Task<T> ReadRegisterAsync<T>(string address, CancellationToken ct = default);
    public abstract Task WriteRegisterAsync<T>(string address, T value, CancellationToken ct = default);
    public abstract Task<bool> TryStopAsync(CancellationToken ct = default);

    public virtual async ValueTask DisposeAsync()
    {
        if (_isDisposed) return;
        _isDisposed = true;
        await DisconnectAsync();
        _lock.Dispose();
    }

    protected T GetParameter<T>(string key, T defaultValue = default!)
    {
        if (Parameters.TryGetValue(key, out var value) && value is T typedValue)
            return typedValue;
        return defaultValue;
    }

    protected void EnsureConnected()
    {
        if (!IsConnected)
            throw new InvalidOperationException($"{VendorName} CAN controller is not connected. Call ConnectAsync first.");
    }
}
