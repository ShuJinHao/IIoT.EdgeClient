namespace IIoT.Edge.Module.Homogenization.Payload;

/// <summary>
/// 匀浆实时数据快照，由实时任务按周期从 PLC 连续读取信号采集，并上传到 MES 实时数据接口。
/// </summary>
public sealed class HomogenizationRealtimeSnapshot
{
    /// <summary>
    /// 快照采集时间，作为 MES collectTime 的来源。
    /// </summary>
    public DateTime CapturedAt { get; set; }

    /// <summary>
    /// 搅拌转速，来源于 PLC 实时搅拌转速信号。
    /// </summary>
    public short StirringSpeed { get; set; }

    /// <summary>
    /// 搅拌电流，来源于 PLC 实时搅拌电流信号。
    /// </summary>
    public short StirringCurrent { get; set; }

    /// <summary>
    /// 分散转速，来源于 PLC 实时分散转速信号。
    /// </summary>
    public short DispersionSpeed { get; set; }

    /// <summary>
    /// 分散电流，来源于 PLC 实时分散电流信号。
    /// </summary>
    public short DispersionCurrent { get; set; }

    /// <summary>
    /// 温度，来源于 PLC 实时温度信号。
    /// </summary>
    public short Temperature { get; set; }

    /// <summary>
    /// 真空度，来源于 PLC 实时真空度信号。
    /// </summary>
    public short Vacuum { get; set; }
}
