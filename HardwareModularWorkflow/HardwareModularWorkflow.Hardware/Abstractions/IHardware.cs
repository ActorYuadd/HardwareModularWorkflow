using HardwareModularWorkflow.Hardware.Enums;

namespace HardwareModularWorkflow.Hardware.Abstractions;

/// <summary>
/// 硬件基接口：所有硬件类型的通用契约
/// </summary>
public interface IHardware
{
    /// <summary>全局唯一标识</summary>
    long Id { get; set; }

    /// <summary>硬件名称</summary>
    string Name { get; set; }

    /// <summary>别名（界面显示用）</summary>
    string Alias { get; set; }

    /// <summary>备注说明</summary>
    string Note { get; set; }

    /// <summary>通讯端口/通道标识</summary>
    string Port { get; set; }

    /// <summary>设备在控制器上的地址</summary>
    string Address { get; set; }

    /// <summary>硬件类型</summary>
    HardwareType Type { get; }

    /// <summary>当前运行状态</summary>
    HardwareState State { get; set; }

    /// <summary>类型特定参数（如电机速度、目标温度等）</summary>
    Dictionary<string, object> Parameters { get; set; }

    /// <summary>绑定的控制器 ID（对应数据库 Controllers 表）</summary>
    long? ControllerId { get; set; }

    /// <summary>控制器类型（Plc / Can），运行时由 ControllerId 解析</summary>
    string? ControllerType { get; set; }
}
