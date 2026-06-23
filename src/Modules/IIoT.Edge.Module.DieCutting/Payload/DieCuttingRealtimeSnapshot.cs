namespace IIoT.Edge.Module.DieCutting.Payload;

/// <summary>
/// 模切定时采样快照，直接来自 PLC 只读 buffer 和当前任务的设备上下文。
/// </summary>
public sealed class DieCuttingRealtimeSnapshot
{
    /// <summary>
    /// 本次快照采集时间。
    /// </summary>
    public DateTime CapturedAt { get; set; }

    /// <summary>
    /// 本次上传窗口开始时间。
    /// </summary>
    public DateTime WindowStartAt { get; set; }

    /// <summary>
    /// 本次上传窗口完成时间。
    /// </summary>
    public DateTime WindowCompleteAt { get; set; }

    /// <summary>
    /// 弹夹号，优先取 MG#1，空时取 MG#2。
    /// </summary>
    public string ClipNo { get; set; } = string.Empty;

    /// <summary>
    /// 实际产量。
    /// </summary>
    public long PunchingQuantity { get; set; }

    /// <summary>
    /// 冲切速度，已按 PLC 文档缩放。
    /// </summary>
    public decimal PunchingSpeed { get; set; }

    /// <summary>
    /// 产量单位，固定 PCS。
    /// </summary>
    public string PunchingUom { get; set; } = "PCS";

    /// <summary>
    /// MES 设备编码，正式映射待 MES 确认。
    /// </summary>
    public string PunchingDeviceCode { get; set; } = string.Empty;

    /// <summary>
    /// MES 设备名称。
    /// </summary>
    public string PunchingDeviceName { get; set; } = string.Empty;

    /// <summary>
    /// 模切批次号，当前无 PLC 来源时传空字符串。
    /// </summary>
    public string PunchingLotNumber { get; set; } = string.Empty;
}
