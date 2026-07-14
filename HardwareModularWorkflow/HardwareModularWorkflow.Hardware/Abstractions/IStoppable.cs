namespace HardwareModularWorkflow.Hardware.Abstractions;

/// <summary>
/// 可中断接口：支持急停/中断命令的控制器或硬件驱动应实现此接口
/// </summary>
public interface IStoppable
{
    /// <summary>
    /// 尝试中断当前正在执行的操作
    /// </summary>
    /// <param name="ct">取消令牌</param>
    /// <returns>true 表示中断成功或已发出中断请求；false 表示不支持中断</returns>
    Task<bool> TryStopAsync(CancellationToken ct = default);
}
