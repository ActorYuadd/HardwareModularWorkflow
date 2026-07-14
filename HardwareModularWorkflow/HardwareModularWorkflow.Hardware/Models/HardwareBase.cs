using HardwareModularWorkflow.Hardware.Enums;

namespace HardwareModularWorkflow.Hardware.Models;

/// <summary>
/// 硬件基类：所有具体硬件类型的抽象基类
/// 实现 IHardware 接口，提供通用属性
/// </summary>
public abstract class HardwareBase : Abstractions.IHardware
{
    public long Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Alias { get; set; } = string.Empty;
    public string Note { get; set; } = string.Empty;
    public string Port { get; set; } = string.Empty;
    public string Address { get; set; } = string.Empty;
    public abstract HardwareType Type { get; }
    public HardwareState State { get; set; } = HardwareState.Idle;
    public Dictionary<string, object> Parameters { get; set; } = new();
    public long? ControllerId { get; set; }
    public string? ControllerType { get; set; }
    public string CategoryCode { get; set; } = string.Empty;
    public string DriverKey { get; set; } = string.Empty;

    /// <summary>
    /// 获取类型特定的参数值
    /// </summary>
    public T? GetParameter<T>(string key)
    {
        if (Parameters.TryGetValue(key, out var value) && value is T typedValue)
            return typedValue;
        return default;
    }

    /// <summary>
    /// 设置类型特定的参数值
    /// </summary>
    public void SetParameter<T>(string key, T value)
    {
        Parameters[key] = value!;
    }
}
