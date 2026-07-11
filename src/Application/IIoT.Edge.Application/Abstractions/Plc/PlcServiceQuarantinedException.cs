namespace IIoT.Edge.Application.Abstractions.Plc;

/// <summary>
/// PLC 协议任务在受控关闭期限内未退出，当前 service 已进入隔离状态。
/// 隔离实例不得继续读写，也不得在同一物理 PLC 上创建替代 runtime。
/// </summary>
public sealed class PlcServiceQuarantinedException : InvalidOperationException
{
    public const string StableReasonCode = "plc_service_quarantined";

    public PlcServiceQuarantinedException(
        string serviceName,
        string operationName,
        string detail,
        Exception? innerException = null)
        : base(
            $"[{StableReasonCode}] {serviceName} 的 {operationName} 未在受控期限内完成，实例已隔离：{detail}",
            innerException)
    {
        ServiceName = serviceName;
        OperationName = operationName;
    }

    public string ReasonCode => StableReasonCode;

    public string ServiceName { get; }

    public string OperationName { get; }
}
