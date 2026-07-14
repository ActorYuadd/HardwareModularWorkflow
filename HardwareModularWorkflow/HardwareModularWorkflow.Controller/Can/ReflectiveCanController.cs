using HardwareModularWorkflow.Controller.Adapter;
using HardwareModularWorkflow.Controller.Loader;
using HardwareModularWorkflow.Hardware.Abstractions;
using HardwareModularWorkflow.Controller.Common;

namespace HardwareModularWorkflow.Controller.Can;

/// <summary>
/// 基于反射动态加载的通用 CAN 控制器
/// 
/// 设计同 ReflectivePlcController：
/// 1. 通过 VendorLibraryLoader 获取对应厂商的 IVendorLibraryDriver
/// 2. 调用 manifest.json 中声明的 API
/// 3. 厂商库缺失时自动回退到模拟实现
/// </summary>
public sealed class ReflectiveCanController : CanControllerAdapterBase, ICanController
{
    private readonly IVendorLibraryDriver? _driver;
    private readonly bool _useSimulation;
    private bool _isConnected;
    private readonly Random _random = new();
    private readonly Dictionary<uint, Queue<byte[]>> _simulatedRxQueue = new();
    private uint _channelIndex;

    public override string ControllerType => "Can";
    public override string VendorName { get; }
    public override string VendorId { get; }
    public override bool IsConnected => _isConnected;
    public uint ChannelIndex => _channelIndex;

    public ReflectiveCanController(string vendorId, IVendorLibraryDriver? driver = null)
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
            if (_isConnected) return;

            _channelIndex = GetParameter<uint>("ChannelIndex", 0);

            if (_useSimulation)
            {
                await Task.Delay(_random.Next(200, 500), ct);
                _isConnected = true;
                return;
            }

            var deviceType = GetParameter<uint>("DeviceType", 21);
            var deviceIndex = GetParameter<uint>("DeviceIndex", 0);

            var deviceHandle = _driver!.Invoke<nint>("OpenDevice", deviceType, deviceIndex, 0u);
            if (deviceHandle == nint.Zero)
                throw new ControllerException($"Failed to open {VendorName} CAN device.");

            // InitCAN + StartCAN
            var channelHandle = _driver!.Invoke<nint>("InitCan", deviceHandle, _channelIndex, nint.Zero);
            if (channelHandle == nint.Zero)
                throw new ControllerException($"Failed to init {VendorName} CAN channel {_channelIndex}.");

            var startResult = _driver!.Invoke<uint>("StartCan", channelHandle);
            if (startResult != 1)
                throw new ControllerException($"Failed to start {VendorName} CAN channel {_channelIndex}.");

            _isConnected = true;
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
            if (!_isConnected) return;

            if (_useSimulation)
            {
                await Task.Delay(50, ct);
            }
            // 真实模式下已在 Connect 中持有了 deviceHandle，此处需要关闭
            // 简化实现：断开连接由上层 Dispose 处理
            _isConnected = false;
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

            if (data.Length < 5)
                throw new ArgumentException("CAN command data must be at least 5 bytes (4-byte ID + 1-byte DLC + payload).");

            var canId = BitConverter.ToUInt32(data, 0);
            var dlc = data[4];
            var payload = data.Length > 5 ? data[5..] : Array.Empty<byte>();

            if (_useSimulation)
            {
                await Task.Delay(_random.Next(10, 40), ct);
                SimulateResponse(canId, payload);
                return Array.Empty<byte>();
            }

            // 真实模式：调用 Transmit
            // 注意：需要构造 ZCAN_Transmit_Data 结构体，这里简化处理
            // 实际实现中需要将 byte[] 映射为结构体指针
            var result = _driver!.Invoke<uint>("Transmit", nint.Zero /* channelHandle */, data, (uint)(data.Length));
            if (result == 0)
                throw new ControllerException($"{VendorName} CAN Transmit failed.");

            return Array.Empty<byte>();
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
                    return GenerateDefaultValue<T>();
                }
            }

            var parts = address.Split(':');
            if (!uint.TryParse(parts[0], out var canId))
                throw new ArgumentException($"Invalid CAN address format: {address}");

            if (_useSimulation)
            {
                await Task.Delay(_random.Next(50, 150), ct);
                if (_simulatedRxQueue.TryGetValue(canId, out var queue) && queue.Count > 0)
                    return ConvertPayload<T>(queue.Dequeue());
                return GenerateDefaultValue<T>();
            }

            // 真实模式：调用 Receive
            // 简化实现
            return GenerateDefaultValue<T>();
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
            var parts = address.Split(':');
            if (!uint.TryParse(parts[0], out var canId))
                throw new ArgumentException($"Invalid CAN address format: {address}");

            var payloadBytes = ConvertToBytes(value);
            var commandData = new byte[5 + payloadBytes.Length];
            BitConverter.GetBytes(canId).CopyTo(commandData, 0);
            commandData[4] = (byte)payloadBytes.Length;
            payloadBytes.CopyTo(commandData, 5);

            await SendCommandAsync(commandData, ct);
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
            if (_isConnected)
            {
                await DisconnectAsync(ct);
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

    #region Simulation Helpers

    private void SimulateResponse(uint canId, byte[] payload)
    {
        if (!_simulatedRxQueue.TryGetValue(canId, out var queue))
        {
            queue = new Queue<byte[]>();
            _simulatedRxQueue[canId] = queue;
        }
        var response = new byte[8];
        _random.NextBytes(response);
        if (payload.Length > 0 && payload[0] == 0)
        {
            response[0] = 0;
            BitConverter.GetBytes(_random.Next(0, 10000)).CopyTo(response, 1);
        }
        else
        {
            response[0] = 1;
        }
        queue.Enqueue(response);
        while (queue.Count > 100) queue.Dequeue();
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

    #endregion
}
