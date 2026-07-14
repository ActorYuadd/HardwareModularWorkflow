namespace HardwareModularWorkflow.Hardware.Abstractions;

/// <summary>
/// 通讯控制器基接口：Hardware 层定义抽象契约，Controller 层实现具体协议
/// 只覆盖基础操作：连接、断开、发送命令、读写寄存器
/// </summary>
public interface IController : IAsyncDisposable
{
    /// <summary>控制器类型："Plc" 或 "Can"</summary>
    string ControllerType { get; }

    /// <summary>厂商名称（如 "Siemens"、"Beckhoff"、"Peak" 等）</summary>
    string VendorName { get; }

    /// <summary>是否已连接</summary>
    bool IsConnected { get; }

    /// <summary>连接控制器</summary>
    Task ConnectAsync(CancellationToken ct = default);

    /// <summary>断开控制器</summary>
    Task DisconnectAsync(CancellationToken ct = default);

    /// <summary>
    /// 发送原始命令数据并返回响应
    /// </summary>
    /// <param name="data">原始字节数据</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>控制器返回的原始字节数据</returns>
    Task<byte[]> SendCommandAsync(byte[] data, CancellationToken ct = default);

    /// <summary>
    /// 读取寄存器/变量值
    /// </summary>
    /// <typeparam name="T">期望返回类型</typeparam>
    /// <param name="address">寄存器地址或变量名</param>
    /// <param name="ct">取消令牌</param>
    Task<T> ReadRegisterAsync<T>(string address, CancellationToken ct = default);

    /// <summary>
    /// 写入寄存器/变量值
    /// </summary>
    /// <typeparam name="T">写入值类型</typeparam>
    /// <param name="address">寄存器地址或变量名</param>
    /// <param name="value">写入值</param>
    /// <param name="ct">取消令牌</param>
    Task WriteRegisterAsync<T>(string address, T value, CancellationToken ct = default);
}
