using HardwareModularWorkflow.Hardware.Abstractions;
using HardwareModularWorkflow.Hardware.Enums;

namespace HardwareModularWorkflow.Hardware.Models;

/// <summary>
/// 预定义硬件命令：封装常用的硬件语义命令
/// 作为 IHardwareCommand 的具体实现，供 Workflow 层直接使用
/// </summary>
public sealed class HardwareCommand : IHardwareCommand
{
    public required string CommandName { get; init; }
    public Dictionary<string, object> Parameters { get; init; } = new();
    public bool IsAsync { get; init; } = true;
    public TimeSpan? Timeout { get; init; }
    public Type? ExpectedReturnType { get; init; }

    // --- 常用命令工厂方法 ---

    /// <summary>启动命令</summary>
    public static HardwareCommand Start(TimeSpan? timeout = null) =>
        new() { CommandName = "Start", IsAsync = true, Timeout = timeout };

    /// <summary>停止命令</summary>
    public static HardwareCommand Stop(TimeSpan? timeout = null) =>
        new() { CommandName = "Stop", IsAsync = true, Timeout = timeout };

    /// <summary>复位命令</summary>
    public static HardwareCommand Reset(TimeSpan? timeout = null) =>
        new() { CommandName = "Reset", IsAsync = true, Timeout = timeout };

    /// <summary>获取状态命令（快速，标记为非异步）</summary>
    public static HardwareCommand GetState() =>
        new() { CommandName = "GetState", IsAsync = false, ExpectedReturnType = typeof(HardwareState) };

    /// <summary>设置参数命令</summary>
    public static HardwareCommand SetParameter(string key, object value, TimeSpan? timeout = null) =>
        new() { CommandName = "SetParameter", Parameters = new() { ["Key"] = key, ["Value"] = value }, IsAsync = true, Timeout = timeout };

    /// <summary>电机移动到指定位置</summary>
    public static HardwareCommand MotorMoveTo(double position, double speed, TimeSpan? timeout = null) =>
        new()
        {
            CommandName = "MoveTo",
            Parameters = new() { ["Position"] = position, ["Speed"] = speed },
            IsAsync = true,
            Timeout = timeout
        };

    /// <summary>电机回零命令</summary>
    public static HardwareCommand MotorHome(TimeSpan? timeout = null) =>
        new() { CommandName = "Home", IsAsync = true, Timeout = timeout };

    /// <summary>设置温度命令</summary>
    public static HardwareCommand SetTemperature(double targetTemperature, TimeSpan? timeout = null) =>
        new()
        {
            CommandName = "SetTemperature",
            Parameters = new() { ["TargetTemperature"] = targetTemperature },
            IsAsync = true,
            Timeout = timeout
        };

    /// <summary>执行自定义命令（自定义硬件用）</summary>
    public static HardwareCommand Custom(string name, Dictionary<string, object> parameters, TimeSpan? timeout = null) =>
        new() { CommandName = name, Parameters = parameters, IsAsync = true, Timeout = timeout };
}
