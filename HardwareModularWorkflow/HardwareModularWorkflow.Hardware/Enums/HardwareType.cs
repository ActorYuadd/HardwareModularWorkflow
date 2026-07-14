namespace HardwareModularWorkflow.Hardware.Enums;

/// <summary>
/// 硬件类型枚举
/// </summary>
public enum HardwareType
{
    /// <summary>电机</summary>
    Motor,
    /// <summary>制温</summary>
    Temperature,
    /// <summary>制冷</summary>
    Cooling,
    /// <summary>自定义类型（通过 JSON Schema 定义）</summary>
    Custom
}
