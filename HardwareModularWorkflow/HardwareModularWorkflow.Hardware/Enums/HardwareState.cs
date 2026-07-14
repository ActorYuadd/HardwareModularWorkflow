namespace HardwareModularWorkflow.Hardware.Enums;

/// <summary>
/// 硬件当前运行状态
/// </summary>
public enum HardwareState
{
    /// <summary>空闲，等待命令</summary>
    Idle,
    /// <summary>正在执行命令</summary>
    Running,
    /// <summary>发生错误</summary>
    Error,
    /// <summary>控制器离线或通讯中断</summary>
    Offline,
    /// <summary>正在初始化/连接中</summary>
    Initializing
}
