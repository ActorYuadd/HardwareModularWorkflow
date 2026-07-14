namespace HardwareModularWorkflow.Hardware.Enums;

/// <summary>
/// 命令执行结果状态
/// </summary>
public enum CommandStatus
{
    /// <summary>成功完成</summary>
    Success,
    /// <summary>执行失败</summary>
    Failed,
    /// <summary>已取消</summary>
    Cancelled,
    /// <summary>超时</summary>
    Timeout,
    /// <summary>硬件离线，命令未执行</summary>
    Offline,
    /// <summary>控制器不支持该命令</summary>
    NotSupported
}
