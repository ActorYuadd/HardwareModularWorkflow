using HardwareModularWorkflow.Hardware.Enums;

namespace HardwareModularWorkflow.Hardware.Models;

/// <summary>
/// 电机硬件模型
/// 上位机区分带编码/不带编码：带编码时启用闭环反馈（位置确认、回零检测），无编码时只做开环下发
/// </summary>
public sealed class MotorHardware : HardwareBase
{
    public override HardwareType Type => HardwareType.Motor;

    /// <summary>
    /// 是否带编码器
    /// true: 上位机读取编码器反馈做闭环监控（位置确认、回零校准、完成检测）
    /// false: 只做开环命令下发，跳过反馈等待
    /// </summary>
    public bool HasEncoder
    {
        get => GetParameter<bool>(nameof(HasEncoder));
        set => SetParameter(nameof(HasEncoder), value);
    }

    /// <summary>当前编码器值（带编码器时有效）</summary>
    public long EncoderValue
    {
        get => GetParameter<long>(nameof(EncoderValue));
        set => SetParameter(nameof(EncoderValue), value);
    }

    /// <summary>当前位置</summary>
    public double Position
    {
        get => GetParameter<double>(nameof(Position));
        set => SetParameter(nameof(Position), value);
    }

    /// <summary>目标速度</summary>
    public double Speed
    {
        get => GetParameter<double>(nameof(Speed));
        set => SetParameter(nameof(Speed), value);
    }

    /// <summary>扭力/扭矩限制</summary>
    public double Torque
    {
        get => GetParameter<double>(nameof(Torque));
        set => SetParameter(nameof(Torque), value);
    }

    /// <summary>加速度</summary>
    public double Acceleration
    {
        get => GetParameter<double>(nameof(Acceleration));
        set => SetParameter(nameof(Acceleration), value);
    }

    /// <summary>
    /// 运动方式：点位运动 / 速度模式 / 力矩模式
    /// 由 Workflow 层配置，映射到具体命令
    /// </summary>
    public MotorMotionMode MotionMode
    {
        get => GetParameter<MotorMotionMode>(nameof(MotionMode));
        set => SetParameter(nameof(MotionMode), value);
    }
}

/// <summary>
/// 电机运动模式
/// </summary>
public enum MotorMotionMode
{
    /// <summary>点位运动：移动到指定位置后停止</summary>
    Position,
    /// <summary>速度模式：以设定速度持续运行</summary>
    Velocity,
    /// <summary>力矩模式：以设定力矩运行</summary>
    Torque
}
