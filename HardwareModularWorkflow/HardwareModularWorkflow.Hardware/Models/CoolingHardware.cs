using HardwareModularWorkflow.Hardware.Enums;

namespace HardwareModularWorkflow.Hardware.Models;

/// <summary>
/// 制冷硬件模型：制冷机、冷却器等
/// </summary>
public sealed class CoolingHardware : HardwareBase
{
    public override HardwareType Type => HardwareType.Cooling;

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

    /// <summary>制冷功率（0-100% 或 0-最大瓦数）</summary>
    public double CoolingPower
    {
        get => GetParameter<double>(nameof(CoolingPower));
        set => SetParameter(nameof(CoolingPower), value);
    }

    /// <summary>温度控制模式</summary>
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
