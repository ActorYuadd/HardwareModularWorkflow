using HardwareModularWorkflow.Hardware.Enums;

namespace HardwareModularWorkflow.Hardware.Results;

/// <summary>
/// 命令执行结果：Controller 层执行后返回的统一结果格式
/// </summary>
public sealed class CommandResult
{
    /// <summary>执行状态</summary>
    public required CommandStatus Status { get; init; }

    /// <summary>是否成功</summary>
    public bool IsSuccess => Status == CommandStatus.Success;

    /// <summary>命令执行耗时</summary>
    public TimeSpan Duration { get; init; }

    /// <summary>返回数据（读取命令或自定义命令时使用）</summary>
    public object? Data { get; init; }

    /// <summary>错误码（失败时有效）</summary>
    public string? ErrorCode { get; init; }

    /// <summary>错误信息（失败时有效）</summary>
    public string? ErrorMessage { get; init; }

    /// <summary>原始响应数据（调试/日志用）</summary>
    public byte[]? RawResponse { get; init; }

    // --- 工厂方法 ---

    public static CommandResult Success(TimeSpan duration, object? data = null) =>
        new() { Status = CommandStatus.Success, Duration = duration, Data = data };

    public static CommandResult Failed(TimeSpan duration, string errorCode, string errorMessage) =>
        new() { Status = CommandStatus.Failed, Duration = duration, ErrorCode = errorCode, ErrorMessage = errorMessage };

    public static CommandResult Cancelled(TimeSpan duration) =>
        new() { Status = CommandStatus.Cancelled, Duration = duration };

    public static CommandResult Timeout(TimeSpan duration) =>
        new() { Status = CommandStatus.Timeout, Duration = duration };

    public static CommandResult Offline(TimeSpan duration) =>
        new() { Status = CommandStatus.Offline, Duration = duration };

    public static CommandResult NotSupported(TimeSpan duration, string commandName) =>
        new() { Status = CommandStatus.NotSupported, Duration = duration, ErrorMessage = $"Command '{commandName}' is not supported by this controller." };
}
