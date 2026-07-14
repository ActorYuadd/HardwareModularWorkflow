namespace HardwareModularWorkflow.Hardware.Abstractions;

/// <summary>
/// 硬件语义命令：Hardware 层定义"做什么"，不感知具体控制器
/// </summary>
public interface IHardwareCommand
{
    /// <summary>命令名称（如 MoveTo、SetSpeed、Start）</summary>
    string CommandName { get; }

    /// <summary>命令参数</summary>
    Dictionary<string, object> Parameters { get; }

    /// <summary>是否异步执行（耗时操作标记为 true）</summary>
    bool IsAsync { get; }

    /// <summary>超时时间（null 表示使用默认超时）</summary>
    TimeSpan? Timeout { get; }

    /// <summary>期望返回的数据类型（用于 GetState 等读取命令）</summary>
    Type? ExpectedReturnType { get; }
}
