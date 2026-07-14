namespace HardwareModularWorkflow.Hardware.Abstractions;

/// <summary>
/// CAN 控制器接口标记：继承 IController，无额外方法
/// 具体实现由 Controller 层提供（CanVendorXAdapter 等）
/// </summary>
public interface ICanController : IController
{
}
