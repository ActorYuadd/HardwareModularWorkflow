using System.Buffers.Binary;
using HardwareModularWorkflow.Controller.Adapter;
using HardwareModularWorkflow.Controller.Loader;
using HardwareModularWorkflow.Hardware.Abstractions;
using HardwareModularWorkflow.Hardware.Enums;
using HardwareModularWorkflow.Hardware.Results;
using HardwareModularWorkflow.Controller.Common;

namespace HardwareModularWorkflow.Controller.Plc;

/// <summary>
/// 基于反射动态加载的通用 PLC 控制器
/// 
/// 设计：
/// 1. 通过 VendorLibraryLoader 获取对应厂商的 IVendorLibraryDriver
/// 2. 调用 manifest.json 中声明的 API（如 OpenDevice / ReadRegister / WriteRegister）
/// 3. 如果厂商库不可用，自动回退到模拟实现（与现有 LeadSysPlcController 行为一致）
/// 
/// 优势：
/// - 无需硬编码 P/Invoke 声明
/// - 同一套代码可支持多个厂商的 PLC
/// - 厂商库缺失时自动降级为模拟模式
/// </summary>
public sealed class ReflectivePlcController : PlcControllerAdapterBase, IPlcController
{
    private readonly IVendorLibraryDriver? _driver;
    private readonly bool _useSimulation;
    private nint _deviceHandle;
    private readonly Random _random = new();
    private readonly Dictionary<string, int> _simulatedRegisters = new();

    public override string ControllerType => "Plc";
    public override string VendorName { get; }
    public override string VendorId { get; }
    public override bool IsConnected => _deviceHandle != nint.Zero;

    public ReflectivePlcController(string vendorId, IVendorLibraryDriver? driver = null)
    {
        VendorId = vendorId;
        VendorName = driver?.VendorId ?? vendorId;
        _driver = driver;
        _useSimulation = driver == null || !driver.HasApi("OpenDevice");
    }

    public override async Task ConnectAsync(CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct);
        try
        {
            if (IsConnected) return;

            if (_useSimulation)
            {
                // 模拟模式：延迟 + 验证参数
                await Task.Delay(_random.Next(100, 300), ct);
                var ip = GetParameter<string>("IpAddress");
                var comPort = GetParameter<string>("ComPort");
                if (string.IsNullOrEmpty(ip) && string.IsNullOrEmpty(comPort))
                    throw new InvalidOperationException("Either IpAddress or ComPort must be configured.");
                _deviceHandle = new nint(1);
                return;
            }

            // 真实模式：通过反射调用厂商 DLL
            var ipAddress = GetParameter<string>("IpAddress", "127.0.0.1");
            var port = GetParameter<int>("Port", 502);
            var timeoutMs = GetParameter<int>("ConnectTimeoutMs", 5000);

            _deviceHandle = _driver!.Invoke<nint>("OpenDevice", ipAddress, port, timeoutMs);
            if (_deviceHandle == nint.Zero)
                throw new ControllerException($"Failed to open {VendorName} PLC device.");
        }
        finally
        {
            _lock.Release();
        }
    }

    public override async Task DisconnectAsync(CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct);
        try
        {
            if (!IsConnected) return;

            if (!_useSimulation && _driver != null)
            {
                _driver.Invoke<int>("CloseDevice", _deviceHandle);
            }
            else
            {
                await Task.Delay(50, ct);
            }
            _deviceHandle = nint.Zero;
        }
        finally
        {
            _lock.Release();
        }
    }

    public override async Task<byte[]> SendCommandAsync(byte[] data, CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct);
        try
        {
            EnsureConnected();

            if (data.Length < 2)
                throw new ArgumentException("PLC command data must be at least 2 bytes.");

            if (_useSimulation)
            {
                await Task.Delay(_random.Next(20, 80), ct);
                var commandName = ExtractCommandName(data);
                return SimulateCommandResponse(commandName, data);
            }

            // 真实模式
            var result = _driver!.Invoke<int>("SendCommand", _deviceHandle, data, data.Length);
            if (result < 0)
                throw new ControllerException($"{VendorName} PLC SendCommand failed with code {result}.");

            return Array.Empty<byte>(); // 实际实现中应从驱动获取响应
        }
        finally
        {
            _lock.Release();
        }
    }

    public override async Task<T> ReadRegisterAsync<T>(string address, CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct);
        try
        {
            EnsureConnected();

            if (string.IsNullOrWhiteSpace(address))
            {
                if (_useSimulation)
                {
                    await Task.Delay(_random.Next(10, 30), ct);
                    return ConvertTo<T>((int)HardwareState.Idle);
                }
            }

            var (regType, offset) = ParseAddress(address);

            if (_useSimulation)
            {
                await Task.Delay(_random.Next(10, 30), ct);
                var key = $"{regType}:{offset}";
                if (!_simulatedRegisters.TryGetValue(key, out var value))
                {
                    value = SimulateRegisterValue(regType);
                    _simulatedRegisters[key] = value;
                }
                return ConvertTo<T>(value);
            }

            // 真实模式：调用 ReadRegister API
            // 注意：这里需要根据 manifest 中 API 签名调整参数传递方式
            // 对于 out/ref 参数，需要使用特定的调用模式
            var count = 1;
            var result = _driver!.Invoke<int>("ReadRegister", _deviceHandle, (byte)regType, offset, count);
            return ConvertTo<T>(result);
        }
        finally
        {
            _lock.Release();
        }
    }

    public override async Task WriteRegisterAsync<T>(string address, T value, CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct);
        try
        {
            EnsureConnected();
            var (regType, offset) = ParseAddress(address);
            var intValue = ConvertToInt(value);

            if (_useSimulation)
            {
                await Task.Delay(_random.Next(10, 30), ct);
                _simulatedRegisters[$"{regType}:{offset}"] = intValue;
                return;
            }

            var result = _driver!.Invoke<int>("WriteRegister", _deviceHandle, (byte)regType, offset, intValue);
            if (result < 0)
                throw new ControllerException($"{VendorName} PLC WriteRegister failed with code {result}.");
        }
        finally
        {
            _lock.Release();
        }
    }

    public override async Task<bool> TryStopAsync(CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct);
        try
        {
            if (!IsConnected) return true;
            await DisconnectAsync(ct);
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

    #region Simulation Helpers

    private static string? ExtractCommandName(byte[] data)
    {
        if (data.Length < 2) return null;
        var nameLength = data[0];
        if (data.Length < 1 + nameLength) return null;
        return System.Text.Encoding.UTF8.GetString(data, 1, nameLength);
    }

    private byte[] SimulateCommandResponse(string? commandName, byte[] data)
    {
        return commandName?.ToUpperInvariant() switch
        {
            "START" => SimulateJsonResponse(new { Status = 1, Position = 0 }),
            "STOP" => SimulateJsonResponse(new { Status = 0 }),
            "RESET" => SimulateJsonResponse(new { Status = 0, ErrorCode = 0 }),
            "GETSTATE" => SimulateJsonResponse(new { State = (int)HardwareState.Idle, Position = _random.Next(0, 10000) }),
            "MOTORMOVETO" => SimulateJsonResponse(new { Status = 1, TargetPosition = GetParameterFromData(data, "position") ?? 0 }),
            "MOTORHOME" => SimulateJsonResponse(new { Status = 1, Position = 0 }),
            "SETTEMPERATURE" => SimulateJsonResponse(new { Temperature = GetParameterFromData(data, "temperature") ?? 25 }),
            "SETPARAMETER" => SimulateJsonResponse(new { Updated = true }),
            "CUSTOM" => SimulateJsonResponse(new { Result = "custom_command_executed" }),
            _ => SimulateJsonResponse(new { Unknown = true, Echo = commandName })
        };
    }

    private static byte[] SimulateJsonResponse(object data)
    {
        var json = System.Text.Json.JsonSerializer.Serialize(new { Status = "OK", Data = data });
        return System.Text.Encoding.UTF8.GetBytes(json);
    }

    private static int? GetParameterFromData(byte[] data, string key)
    {
        try
        {
            using var ms = new System.IO.MemoryStream(data);
            using var reader = new System.IO.BinaryReader(ms);
            var nameLen = reader.ReadByte();
            reader.ReadBytes(nameLen);
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

    private static int SimulateRegisterValue(byte regType)
    {
        var random = new Random();
        return regType switch
        {
            0x01 => random.Next(0, 32767),   // D
            0x02 => random.Next(0, 2),        // M
            0x03 => random.Next(0, 2),        // X
            0x04 => random.Next(0, 2),        // Y
            0x05 => random.Next(0, 1000),     // R
            0x06 => random.Next(0, 2),        // S
            0x07 => random.Next(0, 10000),    // T
            0x08 => random.Next(0, 10000),    // C
            _ => 0
        };
    }

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
            _ => throw new ArgumentException($"Unknown register type: {typeChar}")
        };

        return (registerType, offset);
    }

    private static int ConvertToInt<T>(T value)
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
            _ => throw new NotSupportedException($"Type {typeof(T).Name} is not supported")
        };
    }

    private static T ConvertTo<T>(int value)
    {
        var targetType = Nullable.GetUnderlyingType(typeof(T)) ?? typeof(T);
        object result = targetType switch
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
        return (T)result;
    }

    #endregion
}
