using System.Runtime.InteropServices;
using HardwareModularWorkflow.Hardware.Abstractions;
using HardwareModularWorkflow.Hardware.Enums;
using HardwareModularWorkflow.Hardware.Results;
using HardwareModularWorkflow.Controller.Common;

namespace HardwareModularWorkflow.Controller.Plc;

/// <summary>
/// 雷赛（LeadSys）MC500 系列 PLC 控制器 — 模拟实现
/// 无硬件环境下返回模拟结果，用于测试和演示
/// 
/// 实际部署时：根据 EPAN 头文件启用 LeadSysEpanNative 中的 P/Invoke，
/// 将本类中模拟逻辑替换为真实 DLL 调用。
/// </summary>
public sealed class LeadSysPlcController : IPlcController, IStoppable
{
    private readonly LeadSysPlcControllerConfig _config;
    private IntPtr _handle = IntPtr.Zero;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private bool _isDisposed;

    // 模拟寄存器存储：key = "RegisterType:Offset"
    private readonly Dictionary<string, int> _simulatedRegisters = new();
    private readonly Random _random = new();

    public string ControllerType => "Plc";
    public string VendorName => "LeadSys";
    public bool IsConnected => _handle != IntPtr.Zero;

    public LeadSysPlcController(LeadSysPlcControllerConfig config)
    {
        _config = config ?? throw new ArgumentNullException(nameof(config));
    }

    public async Task ConnectAsync(CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct);
        try
        {
            if (IsConnected) return;

            // 模拟连接：延迟 100-300ms，模拟真实连接耗时
            await Task.Delay(_random.Next(100, 300), ct);

            // 验证配置
            if (string.IsNullOrEmpty(_config.IpAddress) && string.IsNullOrEmpty(_config.ComPort))
            {
                throw new InvalidOperationException("Either IpAddress or ComPort must be configured for PLC connection.");
            }

            _handle = new IntPtr(1); // 模拟连接句柄
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task DisconnectAsync(CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct);
        try
        {
            if (IsConnected)
            {
                await Task.Delay(50, ct); // 模拟断开耗时
                _handle = IntPtr.Zero;
            }
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<byte[]> SendCommandAsync(byte[] data, CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct);
        try
        {
            EnsureConnected();

            if (data.Length < 2)
                throw new ArgumentException("PLC command data must be at least 2 bytes (function code + parameters)");

            // 模拟命令执行延迟
            await Task.Delay(_random.Next(20, 80), ct);

            // 解析命令名（由 CoreHardwareDriver.SerializeCommand 序列化）
            var commandName = ExtractCommandName(data);

            // 根据命令名返回模拟响应
            return commandName?.ToUpperInvariant() switch
            {
                "START" => SimulateResponse("OK", new { Status = 1, Position = 0 }),
                "STOP" => SimulateResponse("OK", new { Status = 0 }),
                "RESET" => SimulateResponse("OK", new { Status = 0, ErrorCode = 0 }),
                "GETSTATE" => SimulateResponse("OK", new { State = (int)HardwareState.Idle, Position = _random.Next(0, 10000) }),
                "MOTORMOVETO" => SimulateResponse("OK", new { Status = 1, TargetPosition = GetParameter(data, "position") ?? 0 }),
                "MOTORHOME" => SimulateResponse("OK", new { Status = 1, Position = 0 }),
                "SETTEMPERATURE" => SimulateResponse("OK", new { Temperature = GetParameter(data, "temperature") ?? 25 }),
                "SETPARAMETER" => SimulateResponse("OK", new { Updated = true }),
                "CUSTOM" => SimulateResponse("OK", new { Result = "custom_command_executed" }),
                _ => SimulateResponse("OK", new { Unknown = true, Echo = commandName })
            };
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<T> ReadRegisterAsync<T>(string address, CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct);
        try
        {
            EnsureConnected();

            // 处理空地址（如 GetStateAsync 传入空地址）
            if (string.IsNullOrWhiteSpace(address))
            {
                await Task.Delay(_random.Next(10, 30), ct);
                return (T)(object)ConvertValue<T>((int)HardwareState.Idle);
            }

            var (registerType, registerOffset) = ParseAddress(address);
            var key = $"{registerType}:{registerOffset}";

            // 模拟读取延迟
            await Task.Delay(_random.Next(10, 30), ct);

            // 如果寄存器不存在，初始化一个模拟值
            if (!_simulatedRegisters.TryGetValue(key, out var value))
            {
                value = registerType switch
                {
                    0x01 => _random.Next(0, 32767),     // D 数据寄存器
                    0x02 => _random.Next(0, 2),          // M 辅助继电器
                    0x03 => _random.Next(0, 2),          // X 输入
                    0x04 => _random.Next(0, 2),          // Y 输出
                    0x05 => _random.Next(0, 1000),      // R 保持寄存器
                    0x06 => _random.Next(0, 2),          // S 状态继电器
                    0x07 => _random.Next(0, 10000),     // T 定时器
                    0x08 => _random.Next(0, 10000),     // C 计数器
                    _ => 0
                };
                _simulatedRegisters[key] = value;
            }

            return (T)(object)ConvertValue<T>(value);
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task WriteRegisterAsync<T>(string address, T value, CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct);
        try
        {
            EnsureConnected();

            var (registerType, registerOffset) = ParseAddress(address);
            var key = $"{registerType}:{registerOffset}";

            // 模拟写入延迟
            await Task.Delay(_random.Next(10, 30), ct);

            _simulatedRegisters[key] = ConvertValue(value);
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<bool> TryStopAsync(CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct);
        try
        {
            if (!IsConnected) return true;
            await Task.Delay(50, ct);
            return true;
        }
        catch
        {
            return false;
        }
        finally
        {
            _lock.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_isDisposed) return;
        _isDisposed = true;
        await DisconnectAsync();
        _lock.Dispose();
    }

    private void EnsureConnected()
    {
        if (!IsConnected)
            throw new InvalidOperationException("PLC controller is not connected. Call ConnectAsync first.");
    }

    /// <summary>
    /// 从序列化命令中提取命令名
    /// </summary>
    private static string? ExtractCommandName(byte[] data)
    {
        if (data.Length < 2) return null;
        var nameLength = data[0];
        if (data.Length < 1 + nameLength) return null;
        return System.Text.Encoding.UTF8.GetString(data, 1, nameLength);
    }

    /// <summary>
    /// 从序列化命令中提取参数值
    /// </summary>
    private static int? GetParameter(byte[] data, string key)
    {
        try
        {
            using var ms = new System.IO.MemoryStream(data);
            using var reader = new System.IO.BinaryReader(ms);

            var nameLen = reader.ReadByte();
            reader.ReadBytes(nameLen); // skip name

            var paramCount = reader.ReadByte();
            for (int i = 0; i < paramCount; i++)
            {
                var keyLen = reader.ReadByte();
                var keyBytes = reader.ReadBytes(keyLen);
                var paramKey = System.Text.Encoding.UTF8.GetString(keyBytes);

                var valueLen = reader.ReadInt32();
                var valueBytes = reader.ReadBytes(valueLen);
                var valueJson = System.Text.Encoding.UTF8.GetString(valueBytes);

                if (paramKey.Equals(key, StringComparison.OrdinalIgnoreCase))
                {
                    if (int.TryParse(valueJson, out var intValue)) return intValue;
                    if (double.TryParse(valueJson, out var doubleValue)) return (int)doubleValue;
                }
            }
        }
        catch { }
        return null;
    }

    private static byte[] SimulateResponse(string status, object data)
    {
        var json = System.Text.Json.JsonSerializer.Serialize(new { Status = status, Data = data });
        return System.Text.Encoding.UTF8.GetBytes(json);
    }

    /// <summary>
    /// 解析 PLC 地址字符串
    /// </summary>
    private static (byte RegisterType, int Offset) ParseAddress(string address)
    {
        if (string.IsNullOrWhiteSpace(address))
            throw new ArgumentException("Address cannot be null or empty");

        address = address.Trim().ToUpperInvariant();

        char typeChar = address[0];
        if (!int.TryParse(address[1..], out var offset))
            throw new ArgumentException($"Invalid PLC address format: {address}");

        byte registerType = typeChar switch
        {
            'D' => 0x01,
            'M' => 0x02,
            'X' => 0x03,
            'Y' => 0x04,
            'R' => 0x05,
            'S' => 0x06,
            'T' => 0x07,
            'C' => 0x08,
            _ => throw new ArgumentException($"Unknown register type: {typeChar}. Supported: D, M, X, Y, R, S, T, C")
        };

        return (registerType, offset);
    }

    private static int ConvertValue<T>(T value)
    {
        return value switch
        {
            int i => i,
            uint ui => (int)ui,
            short s => s,
            ushort us => us,
            byte b => b,
            sbyte sb => sb,
            bool bl => bl ? 1 : 0,
            float f => (int)f,
            double d => (int)d,
            long l => (int)l,
            ulong ul => (int)ul,
            _ => throw new NotSupportedException($"Type {typeof(T).Name} is not supported for PLC register write")
        };
    }

    private static object ConvertValue<T>(int value)
    {
        var targetType = Nullable.GetUnderlyingType(typeof(T)) ?? typeof(T);
        return targetType switch
        {
            var t when t == typeof(int) => value,
            var t when t == typeof(uint) => (uint)value,
            var t when t == typeof(short) => (short)value,
            var t when t == typeof(ushort) => (ushort)value,
            var t when t == typeof(byte) => (byte)value,
            var t when t == typeof(sbyte) => (sbyte)value,
            var t when t == typeof(bool) => value != 0,
            var t when t == typeof(float) => (float)value,
            var t when t == typeof(double) => (double)value,
            var t when t == typeof(long) => (long)value,
            var t when t == typeof(ulong) => (ulong)value,
            _ => value
        };
    }
}

/// <summary>
/// 雷赛 PLC 控制器配置
/// </summary>
public sealed class LeadSysPlcControllerConfig
{
    public string? IpAddress { get; set; }
    public int Port { get; set; } = 502;
    public string? ComPort { get; set; }
    public int BaudRate { get; set; } = 115200;
    public byte StationId { get; set; } = 1;
    public int ConnectTimeoutMs { get; set; } = 5000;
    public int ReadWriteTimeoutMs { get; set; } = 3000;
}

/// <summary>
/// 雷赛 EPAN DLL P/Invoke 声明（保留供实际部署时启用）
/// </summary>
internal static class LeadSysEpanNative
{
    private const string DllName = "EPAN.dll";
    // P/Invoke 声明已注释，实际部署时根据头文件启用
}
