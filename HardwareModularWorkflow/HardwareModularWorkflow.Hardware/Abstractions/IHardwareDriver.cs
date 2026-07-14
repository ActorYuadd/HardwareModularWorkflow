using HardwareModularWorkflow.Hardware.Enums;
using HardwareModularWorkflow.Hardware.Results;

namespace HardwareModularWorkflow.Hardware.Abstractions;

/// <summary>
/// 硬件驱动接口：连接 Hardware 模型与底层 Controller 的桥梁
/// 每个硬件实例对应一个 IHardwareDriver，运行时由 Core 层注入具体实现
/// </summary>
public interface IHardwareDriver
{
    /// <summary>关联的硬件实例</summary>
    IHardware Hardware { get; }

    /// <summary>
    /// 执行硬件命令
    /// </summary>
    /// <param name="command">语义命令</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>命令执行结果</returns>
    Task<CommandResult> ExecuteAsync(IHardwareCommand command, CancellationToken ct = default);

    /// <summary>
    /// 读取硬件当前状态（快速操作，通常同步包装）
    /// </summary>
    Task<HardwareState> GetStateAsync(CancellationToken ct = default);

    /// <summary>
    /// 尝试中断当前正在执行的命令（取决于 Controller/硬件是否支持）
    /// </summary>
    Task<bool> TryStopAsync(CancellationToken ct = default);
}
