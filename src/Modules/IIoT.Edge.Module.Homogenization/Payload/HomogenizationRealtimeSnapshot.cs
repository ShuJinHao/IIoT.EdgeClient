namespace IIoT.Edge.Module.Homogenization.Payload;

/// <summary>
/// 匀浆实时数据快照，由实时任务按周期从 PLC Buffer 读取业务数字，并按配置进入上传链路。
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

    /// <summary>
    /// 生成实时业务数字指纹，采集时间不参与变化判断。
    /// </summary>
    public string CreateFingerprint()
        => string.Join(
            "\u001f",
            StirringSpeed,
            StirringCurrent,
            DispersionSpeed,
            DispersionCurrent,
            Temperature,
            Vacuum);
}
