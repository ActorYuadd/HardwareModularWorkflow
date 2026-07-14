using HardwareModularWorkflow.Hardware.Enums;

namespace HardwareModularWorkflow.Hardware.Models;

/// <summary>
/// 制温硬件模型：加热器、加热板等
/// </summary>
public sealed class TemperatureHardware : HardwareBase
{
    public override HardwareType Type => HardwareType.Temperature;

    /// <summary>目标温度（℃）</summary>
    public double TargetTemperature
    {
        get => GetParameter<double>(nameof(TargetTemperature));
        set => SetParameter(nameof(TargetTemperature), value);
    }

    /// <summary>当前温度（℃）</summary>
    public double CurrentTemperature
    {
        get => GetParameter<double>(nameof(CurrentTemperature));
        set => SetParameter(nameof(CurrentTemperature), value);
    }

    /// <summary>加热功率（0-100% 或 0-最大瓦数）</summary>
    public double HeatingPower
    {
        get => GetParameter<double>(nameof(HeatingPower));
        set => SetParameter(nameof(HeatingPower), value);
    }

    /// <summary>温度控制模式：PID / 开关 / 自定义</summary>
    public TemperatureControlMode ControlMode
    {
        get => GetParameter<TemperatureControlMode>(nameof(ControlMode));
        set => SetParameter(nameof(ControlMode), value);
    }

    /// <summary>温度达到目标后的允许偏差（±℃）</summary>
    public double Tolerance
    {
        get => GetParameter<double>(nameof(Tolerance));
        set => SetParameter(nameof(Tolerance), value);
    }
}

/// <summary>
/// 温度控制模式
/// </summary>
public enum TemperatureControlMode
{
    /// <summary>PID 闭环控制</summary>
    Pid,
    /// <summary>开关式控制（高于目标停止，低于目标启动）</summary>
    OnOff,
    /// <summary>自定义控制模式（由 JSON Schema 定义）</summary>
    Custom
}
