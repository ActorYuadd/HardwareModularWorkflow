namespace HardwareModularWorkflow.Controller.Common;

/// <summary>
/// 控制器操作异常
/// </summary>
public sealed class ControllerException : Exception
{
    public ControllerException(string message) : base(message) { }
    public ControllerException(string message, Exception innerException) : base(message, innerException) { }
}
