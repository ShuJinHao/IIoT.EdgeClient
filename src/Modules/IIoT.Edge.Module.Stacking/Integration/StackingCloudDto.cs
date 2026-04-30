namespace IIoT.Edge.Module.Stacking.Integration;

/// <summary>
/// 叠片云端过站 DTO，字段形态贴合云端叠片接口。
/// </summary>
public sealed class StackingCloudDto
{
    /// <summary>
    /// 叠片电芯条码。
    /// </summary>
    public string Barcode { get; set; } = string.Empty;

    /// <summary>
    /// 当前叠片记录绑定的托盘码。
    /// </summary>
    public string TrayCode { get; set; } = string.Empty;

    /// <summary>
    /// 叠片层数。
    /// </summary>
    public int LayerCount { get; set; }

    /// <summary>
    /// PLC 工序序号，用于云端追溯采集顺序。
    /// </summary>
    public int SequenceNo { get; set; }

    /// <summary>
    /// 电芯结果，按云端约定上传 OK/NG/UNKNOWN。
    /// </summary>
    public string CellResult { get; set; } = string.Empty;

    /// <summary>
    /// 叠片过站完成时间。
    /// </summary>
    public DateTime CompletedTime { get; set; }
}
