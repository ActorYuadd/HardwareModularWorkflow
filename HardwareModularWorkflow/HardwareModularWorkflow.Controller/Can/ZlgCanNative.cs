using System.Runtime.InteropServices;
using HardwareModularWorkflow.Controller.Can;

namespace HardwareModularWorkflow.Controller.Can;

/// <summary>
/// 周立功 ZLG CAN 接口 DLL 的 P/Invoke 声明
/// 基于 zlgcan.h / canframe.h 头文件映射
/// 注意：实际运行时需将 zlgcan.dll（x64 或 x86 版本）放置到输出目录
/// </summary>
internal static class ZlgCanNative
{
    private const string DllName = "zlgcan.dll";

    #region 设备类型常量（部分常用设备）

    public const uint ZCAN_USBCAN_2E_U = 21;
    public const uint ZCAN_USBCAN_4E_U = 31;
    public const uint ZCAN_USBCAN_8E_U = 34;
    public const uint ZCAN_USBCANFD_200U = 41;
    public const uint ZCAN_USBCANFD_100U = 42;
    public const uint ZCAN_USBCANFD_MINI = 43;
    public const uint ZCAN_USBCANFD_400U = 76;
    public const uint ZCAN_USBCANFD_800U = 59;
    public const uint ZCAN_CANFDNET_200U_TCP = 48;
    public const uint ZCAN_CANFDNET_200U_UDP = 49;
    public const uint ZCAN_PCIE_CANFD_100U = 38;
    public const uint ZCAN_PCIE_CANFD_200U = 39;
    public const uint ZCAN_PCIE_CANFD_400U = 40;
    public const uint ZCAN_PCIE_CANFD_800U = 82;
    public const uint ZCAN_PCIE_CANFD_1200U = 83;

    #endregion

    #region 错误码常量

    public const uint ZCAN_ERROR_CAN_OVERFLOW = 0x0001;
    public const uint ZCAN_ERROR_CAN_ERRALARM = 0x0002;
    public const uint ZCAN_ERROR_CAN_PASSIVE = 0x0004;
    public const uint ZCAN_ERROR_CAN_LOSE = 0x0008;
    public const uint ZCAN_ERROR_CAN_BUSERR = 0x0010;
    public const uint ZCAN_ERROR_CAN_BUSOFF = 0x0020;
    public const uint ZCAN_ERROR_CAN_BUFFER_OVERFLOW = 0x0040;
    public const uint ZCAN_ERROR_DEVICEOPENED = 0x0100;
    public const uint ZCAN_ERROR_DEVICEOPEN = 0x0200;
    public const uint ZCAN_ERROR_DEVICENOTOPEN = 0x0400;
    public const uint ZCAN_ERROR_BUFFEROVERFLOW = 0x0800;
    public const uint ZCAN_ERROR_DEVICENOTEXIST = 0x1000;
    public const uint ZCAN_ERROR_LOADKERNELDLL = 0x2000;
    public const uint ZCAN_ERROR_CMDFAILED = 0x4000;
    public const uint ZCAN_ERROR_BUFFERCREATE = 0x8000;

    public const uint STATUS_ERR = 0;
    public const uint STATUS_OK = 1;
    public const uint STATUS_ONLINE = 2;
    public const uint STATUS_OFFLINE = 3;
    public const uint STATUS_UNSUPPORTED = 4;
    public const uint STATUS_BUFFER_TOO_SMALL = 5;

    #endregion

    #region 类型常量

    public const byte TYPE_CAN = 0;
    public const byte TYPE_CANFD = 1;
    public const byte TYPE_ALL_DATA = 2;

    #endregion

    // --- 设备操作 ---

    [DllImport(DllName, CallingConvention = CallingConvention.StdCall)]
    public static extern IntPtr ZCAN_OpenDevice(uint device_type, uint device_index, uint reserved);

    [DllImport(DllName, CallingConvention = CallingConvention.StdCall)]
    public static extern IntPtr ZCAN_OpenDeviceByName(uint device_type, [MarshalAs(UnmanagedType.LPStr)] string name);

    [DllImport(DllName, CallingConvention = CallingConvention.StdCall)]
    public static extern uint ZCAN_CloseDevice(IntPtr device_handle);

    [DllImport(DllName, CallingConvention = CallingConvention.StdCall)]
    public static extern uint ZCAN_GetDeviceInf(IntPtr device_handle, ref ZCAN_DEVICE_INFO pInfo);

    [DllImport(DllName, CallingConvention = CallingConvention.StdCall)]
    public static extern uint ZCAN_IsDeviceOnLine(IntPtr device_handle);

    // --- 通道操作 ---

    [DllImport(DllName, CallingConvention = CallingConvention.StdCall)]
    public static extern IntPtr ZCAN_InitCAN(IntPtr device_handle, uint can_index, ref ZCAN_CHANNEL_INIT_CONFIG pInitConfig);

    [DllImport(DllName, CallingConvention = CallingConvention.StdCall)]
    public static extern uint ZCAN_StartCAN(IntPtr channel_handle);

    [DllImport(DllName, CallingConvention = CallingConvention.StdCall)]
    public static extern uint ZCAN_ResetCAN(IntPtr channel_handle);

    [DllImport(DllName, CallingConvention = CallingConvention.StdCall)]
    public static extern uint ZCAN_ClearBuffer(IntPtr channel_handle);

    [DllImport(DllName, CallingConvention = CallingConvention.StdCall)]
    public static extern uint ZCAN_ReadChannelErrInfo(IntPtr channel_handle, ref ZCAN_CHANNEL_ERR_INFO pErrInfo);

    [DllImport(DllName, CallingConvention = CallingConvention.StdCall)]
    public static extern uint ZCAN_ReadChannelStatus(IntPtr channel_handle, ref ZCAN_CHANNEL_STATUS pCANStatus);

    [DllImport(DllName, CallingConvention = CallingConvention.StdCall)]
    public static extern uint ZCAN_GetReceiveNum(IntPtr channel_handle, byte type);

    // --- 发送/接收 (CAN) ---

    [DllImport(DllName, CallingConvention = CallingConvention.StdCall)]
    public static extern uint ZCAN_Transmit(IntPtr channel_handle, [MarshalAs(UnmanagedType.LPArray)] ZCAN_Transmit_Data[] pTransmit, uint len);

    [DllImport(DllName, CallingConvention = CallingConvention.StdCall)]
    public static extern uint ZCAN_Receive(IntPtr channel_handle, [MarshalAs(UnmanagedType.LPArray)] ZCAN_Receive_Data[] pReceive, uint len, int wait_time);

    // --- 发送/接收 (CAN FD) ---

    [DllImport(DllName, CallingConvention = CallingConvention.StdCall)]
    public static extern uint ZCAN_TransmitFD(IntPtr channel_handle, [MarshalAs(UnmanagedType.LPArray)] ZCAN_TransmitFD_Data[] pTransmit, uint len);

    [DllImport(DllName, CallingConvention = CallingConvention.StdCall)]
    public static extern uint ZCAN_ReceiveFD(IntPtr channel_handle, [MarshalAs(UnmanagedType.LPArray)] ZCAN_ReceiveFD_Data[] pReceive, uint len, int wait_time);

    // --- 通用数据发送/接收 ---

    [DllImport(DllName, CallingConvention = CallingConvention.StdCall)]
    public static extern uint ZCAN_TransmitData(IntPtr device_handle, [MarshalAs(UnmanagedType.LPArray)] ZCANDataObj[] pTransmit, uint len);

    [DllImport(DllName, CallingConvention = CallingConvention.StdCall)]
    public static extern uint ZCAN_ReceiveData(IntPtr device_handle, [MarshalAs(UnmanagedType.LPArray)] ZCANDataObj[] pReceive, uint len, int wait_time);

    // --- 配置读写 ---

    [DllImport(DllName, CallingConvention = CallingConvention.StdCall)]
    public static extern uint ZCAN_SetValue(IntPtr device_handle, [MarshalAs(UnmanagedType.LPStr)] string path, IntPtr value);

    [DllImport(DllName, CallingConvention = CallingConvention.StdCall)]
    public static extern IntPtr ZCAN_GetValue(IntPtr device_handle, [MarshalAs(UnmanagedType.LPStr)] string path);

    // --- 常量 ---

    public const IntPtr INVALID_DEVICE_HANDLE = 0;  // (void*)0
    public const IntPtr INVALID_CHANNEL_HANDLE = 0; // (void*)0

    // CAN FD 标志常量
    public const byte CANFD_BRS = 0x01; /* bit rate switch (second bitrate for payload data) */
    public const byte CANFD_ESI = 0x02; /* error state indicator of the transmitting node */
}

/// <summary>
/// 设备信息结构体
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
internal struct ZCAN_DEVICE_INFO
{
    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 20)]
    public byte[] hw_Version;
    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 10)]
    public byte[] fw_Version;
    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 10)]
    public byte[] dr_Version;
    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 10)]
    public byte[] in_Version;
    public uint irq_Num;
    public byte can_Num;
    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 4)]
    public byte[] str_Serial_Num;
    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 8)]
    public byte[] str_hw_Type;
    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 16)]
    public byte[] reserved;

    public string HwVersion => System.Text.Encoding.ASCII.GetString(hw_Version).TrimEnd('\0');
    public string FwVersion => System.Text.Encoding.ASCII.GetString(fw_Version).TrimEnd('\0');
    public string SerialNumber => System.Text.Encoding.ASCII.GetString(str_Serial_Num).TrimEnd('\0');
}

/// <summary>
/// CAN 通道初始化配置
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
internal struct ZCAN_CHANNEL_INIT_CONFIG
{
    public uint can_type; // TYPE_CAN = 0, TYPE_CANFD = 1

    // CAN 模式配置
    public ZCAN_CAN_MODE mode;

    // CAN 波特率配置（仲裁域）
    public uint acc_code;
    public uint acc_mask;
    public uint reserved;
    public uint brp;      // 波特率预分频
    public uint tseg1;  // 时间段1
    public uint tseg2;  // 时间段2
    public uint sjw;    // 同步跳转宽度

    // CAN FD 数据域波特率
    public uint dbrp;
    public uint dtseg1;
    public uint dtseg2;
    public uint dsjw;
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
internal struct ZCAN_CAN_MODE
{
    public uint tseg1;  // 时间段1
    public uint tseg2;  // 时间段2
    public uint sjw;    // 同步跳转宽度
    public uint smp;    // 采样点
    public uint brp;    // 波特率预分频
}

/// <summary>
/// CAN 发送数据结构（标准 CAN）
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
internal struct ZCAN_Transmit_Data
{
    public ZCAN_CANFrame frame;
    public uint transmit_type; // 0: 正常发送, 1: 单次发送, 2: 自发自收, 3: 单次自发自收
}

/// <summary>
/// CAN 接收数据结构（标准 CAN）
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
internal struct ZCAN_Receive_Data
{
    public ZCAN_CANFrame frame;
    public ulong timestamp; // 硬件时间戳（单位：微秒）
}

/// <summary>
/// CAN 帧结构（标准 CAN，8字节数据）
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
internal struct ZCAN_CANFrame
{
    public uint can_id;    // 32位 CAN ID + 标志位
    public byte can_dlc;   // 数据长度（0-8）
    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 8)]
    public byte[] data;    // 数据区
    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 3)]
    public byte[] reserved; // 保留

    public ZCAN_CANFrame()
    {
        data = new byte[8];
        reserved = new byte[3];
    }
}

/// <summary>
/// CAN FD 发送数据结构
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
internal struct ZCAN_TransmitFD_Data
{
    public ZCAN_CANFDFrame frame;
    public uint transmit_type;
}

/// <summary>
/// CAN FD 接收数据结构
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
internal struct ZCAN_ReceiveFD_Data
{
    public ZCAN_CANFDFrame frame;
    public ulong timestamp;
}

/// <summary>
/// CAN FD 帧结构（最多 64 字节数据）
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
internal struct ZCAN_CANFDFrame
{
    public uint can_id;    // 32位 CAN ID + 标志位
    public byte len;       // 数据长度（0-64）
    public byte flags;     // CANFD_BRS / CANFD_ESI 等标志
    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 64)]
    public byte[] data;    // 数据区
    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 3)]
    public byte[] reserved; // 保留

    public ZCAN_CANFDFrame()
    {
        data = new byte[64];
        reserved = new byte[3];
    }
}

/// <summary>
/// 通用数据对象（CAN/CANFD 统一接口）
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
internal struct ZCANDataObj
{
    public uint dataType;       // TYPE_CAN / TYPE_CANFD
    public uint chnl;            // 通道号
    public uint extra1;          // 标志位
    public uint extra2;          // 保留
    public ZCAN_CANFDFrame data; // 数据帧（CAN 使用 data.data[0..7]）
}

/// <summary>
/// 通道错误信息
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
internal struct ZCAN_CHANNEL_ERR_INFO
{
    public uint error_code;
    public byte passive_ErrData;
    public byte arLost_ErrData;
    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 2)]
    public byte[] reserved;
}

/// <summary>
/// 通道状态信息
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
internal struct ZCAN_CHANNEL_STATUS
{
    public byte errInterrupt;
    public byte regMode;
    public byte regStatus;
    public byte regCaptureStatus;
    public byte arbLostCap;
    public byte errCodeCap;
    public byte errWarningLimit;
    public byte rxErrCount;
    public byte txErrCount;
    public byte reserved;
    public uint refCounter;  // 接收/发送计数器
}
