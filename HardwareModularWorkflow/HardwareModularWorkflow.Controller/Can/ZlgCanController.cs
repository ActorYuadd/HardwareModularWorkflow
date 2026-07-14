using System.Runtime.InteropServices;
using HardwareModularWorkflow.Hardware.Abstractions;
using HardwareModularWorkflow.Hardware.Enums;
using HardwareModularWorkflow.Hardware.Results;
using HardwareModularWorkflow.Controller.Common;
using HardwareModularWorkflow.Controller.Can;

namespace HardwareModularWorkflow.Controller.Can;

/// <summary>
/// 周立功 ZLG CAN 控制器 — 模拟实现
/// 无硬件环境下返回模拟结果，用于测试和演示
/// 
/// 实际部署时：将 ZlgCanNative P/Invoke 调用恢复为真实 DLL 调用。
/// </summary>
public sealed class ZlgCanController : ICanController, IStoppable
{
    private readonly ZlgCanControllerConfig _config;
    private bool _isConnected;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private bool _isDisposed;
    private readonly Random _random = new();

    // 模拟接收队列：key = CAN ID，value = 模拟 payload
    private readonly Dictionary<uint, Queue<byte[]>> _simulatedRxQueue = new();

    public string ControllerType => "Can";
    public string VendorName => "ZLG";
    public bool IsConnected => _isConnected;
    public uint ChannelIndex => _config.ChannelIndex;

    public ZlgCanController(ZlgCanControllerConfig config)
    {
        _config = config ?? throw new ArgumentNullException(nameof(config));
    }

    public async Task ConnectAsync(CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct);
        try
        {
            if (_isConnected) return;

            // 模拟设备初始化延迟
            await Task.Delay(_random.Next(200, 500), ct);
            _isConnected = true;
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
            if (_isConnected)
            {
                await Task.Delay(50, ct);
                _isConnected = false;
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

            if (data.Length < 5)
                throw new ArgumentException("CAN command data must be at least 5 bytes (4-byte ID + 1-byte DLC + payload)");

            var canId = BitConverter.ToUInt32(data, 0);
            var dlc = data[4];
            var payload = data.Length > 5 ? data[5..] : Array.Empty<byte>();

            // 模拟发送延迟
            await Task.Delay(_random.Next(10, 40), ct);

            // 根据 payload 生成模拟响应并存入接收队列
            SimulateResponse(canId, payload);

            return Array.Empty<byte>();
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
                return GenerateDefaultValue<T>();
            }

            var parts = address.Split(':');
            if (!uint.TryParse(parts[0], out var canId))
                throw new ArgumentException($"Invalid CAN address format: {address}. Expected 'CANID' or 'CANID:SubIndex'");

            // 构造读取请求并发送
            byte[] requestData = new byte[5 + 4];
            BitConverter.GetBytes(canId).CopyTo(requestData, 0);
            requestData[4] = 4;
            if (parts.Length > 1 && byte.TryParse(parts[1], out var subIndex))
                requestData[5] = subIndex;

            await SendCommandAsync(requestData, ct);

            // 模拟等待响应
            await Task.Delay(_random.Next(50, 150), ct);

            // 从模拟接收队列取响应
            if (_simulatedRxQueue.TryGetValue(canId, out var queue) && queue.Count > 0)
            {
                var payload = queue.Dequeue();
                return ConvertPayload<T>(payload);
            }

            // 队列为空，生成默认模拟值
            return GenerateDefaultValue<T>();
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

            var parts = address.Split(':');
            if (!uint.TryParse(parts[0], out var canId))
                throw new ArgumentException($"Invalid CAN address format: {address}");

            var payloadBytes = ConvertToBytes(value);
            byte dlc = (byte)payloadBytes.Length;

            byte[] commandData = new byte[5 + dlc];
            BitConverter.GetBytes(canId).CopyTo(commandData, 0);
            commandData[4] = dlc;
            payloadBytes.CopyTo(commandData, 5);

            if (parts.Length > 1 && byte.TryParse(parts[1], out var subIndex))
                commandData[5] = subIndex;

            await SendCommandAsync(commandData, ct);
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
            if (_isConnected)
            {
                await Task.Delay(50, ct);
            }
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
            throw new InvalidOperationException("CAN controller is not connected. Call ConnectAsync first.");
    }

    /// <summary>
    /// 根据发送的 payload 生成模拟响应
    /// </summary>
    private void SimulateResponse(uint canId, byte[] payload)
    {
        if (!_simulatedRxQueue.TryGetValue(canId, out var queue))
        {
            queue = new Queue<byte[]>();
            _simulatedRxQueue[canId] = queue;
        }

        // 生成模拟响应 payload（8字节）
        var response = new byte[8];
        _random.NextBytes(response);

        // 如果是读取请求（payload[0] == 0 表示读取），响应包含状态值
        if (payload.Length > 0 && payload[0] == 0)
        {
            response[0] = 0; // 状态 OK
            BitConverter.GetBytes(_random.Next(0, 10000)).CopyTo(response, 1);
        }
        else
        {
            response[0] = 1; // 写入确认
        }

        queue.Enqueue(response);

        // 限制队列大小，防止内存泄漏
        while (queue.Count > 100)
        {
            queue.Dequeue();
        }
    }

    private static T GenerateDefaultValue<T>()
    {
        var targetType = Nullable.GetUnderlyingType(typeof(T)) ?? typeof(T);
        object result = targetType switch
        {
            var t when t == typeof(byte) => (byte)0,
            var t when t == typeof(sbyte) => (sbyte)0,
            var t when t == typeof(short) => (short)0,
            var t when t == typeof(ushort) => (ushort)0,
            var t when t == typeof(int) => 0,
            var t when t == typeof(uint) => 0u,
            var t when t == typeof(long) => 0L,
            var t when t == typeof(ulong) => 0UL,
            var t when t == typeof(float) => 0.0f,
            var t when t == typeof(double) => 0.0,
            var t when t == typeof(byte[]) => Array.Empty<byte>(),
            _ => 0
        };
        return (T)result;
    }

    private static T ConvertPayload<T>(byte[] payload)
    {
        var targetType = Nullable.GetUnderlyingType(typeof(T)) ?? typeof(T);
        object result = targetType switch
        {
            var t when t == typeof(byte) => payload[0],
            var t when t == typeof(sbyte) => (sbyte)payload[0],
            var t when t == typeof(short) => payload.Length >= 2 ? BitConverter.ToInt16(payload, 0) : (short)0,
            var t when t == typeof(ushort) => payload.Length >= 2 ? BitConverter.ToUInt16(payload, 0) : (ushort)0,
            var t when t == typeof(int) => payload.Length >= 4 ? BitConverter.ToInt32(payload, 0) : 0,
            var t when t == typeof(uint) => payload.Length >= 4 ? BitConverter.ToUInt32(payload, 0) : 0u,
            var t when t == typeof(long) => payload.Length >= 8 ? BitConverter.ToInt64(payload, 0) : 0L,
            var t when t == typeof(ulong) => payload.Length >= 8 ? BitConverter.ToUInt64(payload, 0) : 0UL,
            var t when t == typeof(float) => payload.Length >= 4 ? BitConverter.ToSingle(payload, 0) : 0.0f,
            var t when t == typeof(double) => payload.Length >= 8 ? BitConverter.ToDouble(payload, 0) : 0.0,
            var t when t == typeof(byte[]) => payload,
            _ => 0
        };
        return (T)result;
    }

    private static byte[] ConvertToBytes<T>(T value)
    {
        var targetType = value?.GetType() ?? typeof(T);
        return targetType switch
        {
            var t when t == typeof(byte) => new[] { (byte)(object)value! },
            var t when t == typeof(sbyte) => new[] { (byte)(sbyte)(object)value! },
            var t when t == typeof(short) => BitConverter.GetBytes((short)(object)value!),
            var t when t == typeof(ushort) => BitConverter.GetBytes((ushort)(object)value!),
            var t when t == typeof(int) => BitConverter.GetBytes((int)(object)value!),
            var t when t == typeof(uint) => BitConverter.GetBytes((uint)(object)value!),
            var t when t == typeof(long) => BitConverter.GetBytes((long)(object)value!),
            var t when t == typeof(ulong) => BitConverter.GetBytes((ulong)(object)value!),
            var t when t == typeof(float) => BitConverter.GetBytes((float)(object)value!),
            var t when t == typeof(double) => BitConverter.GetBytes((double)(object)value!),
            var t when t == typeof(byte[]) => (byte[])(object)value!,
            _ => throw new NotSupportedException($"Type {targetType.Name} is not supported for CAN payload conversion")
        };
    }
}

/// <summary>
/// ZLG CAN 控制器配置
/// </summary>
public sealed class ZlgCanControllerConfig
{
    public uint DeviceType { get; set; } = ZlgCanNative.ZCAN_USBCAN_2E_U;
    public uint DeviceIndex { get; set; } = 0;
    public uint ChannelIndex { get; set; } = 0;
    public bool CanFd { get; set; } = false;
    public bool CanFdBrs { get; set; } = false;
    public uint BRP { get; set; } = 0;
    public uint TSeg1 { get; set; } = 0;
    public uint TSeg2 { get; set; } = 0;
    public uint SJW { get; set; } = 0;
    public uint DataBRP { get; set; } = 0;
    public uint DataTSeg1 { get; set; } = 0;
    public uint DataTSeg2 { get; set; } = 0;
    public uint DataSJW { get; set; } = 0;
    public int ReadTimeoutMs { get; set; } = 1000;
    public string? DeviceName { get; set; }
}
