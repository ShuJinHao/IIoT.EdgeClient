using IIoT.Edge.Module.Homogenization.Resources;

namespace IIoT.Edge.Module.Homogenization.Config;

/// <summary>
/// 匀浆 PLC 握手码表配置。它不是点位枚举，只定义 PLC 触发值和上位机写回 PLC 的应答值。
/// </summary>
public sealed class HomogenizationPlcCodeOptions
{
    /// <summary>
    /// PLC 写入触发点的复位值，业务任务通过交互访问器判断复位。
    /// </summary>
    public ushort SignalReset { get; set; }

    /// <summary>
    /// PLC 写入触发点的触发值，业务任务通过交互访问器判断触发。
    /// </summary>
    public ushort SignalTrigger { get; set; }

    /// <summary>
    /// 上位机写回 PLC 的正常完成应答码。
    /// </summary>
    public ushort AckOk { get; set; }

    /// <summary>
    /// 上位机写回 PLC 的异常失败应答码。
    /// </summary>
    public ushort AckException { get; set; }

    /// <summary>
    /// 上位机写回 PLC 的 MES 业务拒绝应答码。
    /// </summary>
    public ushort AckMesNg { get; set; }

    internal void AppendValidationErrors(ICollection<string> errors)
    {
        if (SignalReset == SignalTrigger)
        {
            errors.Add(HomogenizationText.Get(
                "Homogenization_Validate_PlcResetAndTriggerCannotEqual",
                "PLC 复位信号不能与触发信号相同。"));
        }
    }
}
