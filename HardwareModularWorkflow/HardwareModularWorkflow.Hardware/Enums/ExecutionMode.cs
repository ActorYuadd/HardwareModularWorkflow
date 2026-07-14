namespace HardwareModularWorkflow.Hardware.Enums;

/// <summary>
/// 模块/工作流内的执行模式
/// </summary>
public enum ExecutionMode
{
    /// <summary>串行执行：等待前一个完成后再执行下一个</summary>
    Sequential,
    /// <summary>并行执行：同时启动，等待全部完成</summary>
    Parallel
}
