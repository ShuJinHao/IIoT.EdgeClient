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
    /// 批次号，来自 PLC 批次号点位。
    /// </summary>
    public string BatchNumber { get; set; } = string.Empty;

    /// <summary>
    /// 弹夹号，优先取 MG#1，空时取 MG#2。
    /// </summary>
    public string ClipNo { get; set; } = string.Empty;

    /// <summary>
    /// MG#1 弹夹号原始值。
    /// </summary>
    public string ClipNoMg1 { get; set; } = string.Empty;

    /// <summary>
    /// MG#2 弹夹号原始值。
    /// </summary>
    public string ClipNoMg2 { get; set; } = string.Empty;

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
    /// 模切批次号，兼容旧字段，值与 <see cref="BatchNumber"/> 一致。
    /// </summary>
    public string PunchingLotNumber
    {
        get => BatchNumber;
        set => BatchNumber = value?.Trim() ?? string.Empty;
    }

    /// <summary>
    /// MG#1 收料片数设定值。
    /// </summary>
    public int Mg1ReceivingSet { get; set; }

    /// <summary>
    /// MG#1 收料片数实际值。
    /// </summary>
    public int Mg1ReceivingActual { get; set; }

    /// <summary>
    /// MG#2 收料片数设定值。
    /// </summary>
    public int Mg2ReceivingSet { get; set; }

    /// <summary>
    /// MG#2 收料片数实际值。
    /// </summary>
    public int Mg2ReceivingActual { get; set; }

    /// <summary>
    /// 弹夹 OK 级片数量。
    /// </summary>
    public long OkSheetQuantity { get; set; }

    /// <summary>
    /// 极片长度，单位毫米。
    /// </summary>
    public decimal? PlateLengthMm { get; set; }

    /// <summary>
    /// 极片宽度，单位毫米。
    /// </summary>
    public decimal? PlateWidthMm { get; set; }

    /// <summary>
    /// 操作员工号。
    /// </summary>
    public string OperatorCode { get; set; } = string.Empty;

    /// <summary>
    /// 模具编号。
    /// </summary>
    public string MoldCode { get; set; } = string.Empty;

    /// <summary>
    /// 切刀编号。
    /// </summary>
    public string CutterCode { get; set; } = string.Empty;

    /// <summary>
    /// PLC 快照原始字段列表，用于本地展示和诊断。
    /// </summary>
    public IReadOnlyList<DieCuttingSnapshotItem> RawItems { get; set; } = [];

    public string CreateOutboundFingerprint()
        => string.Join(
            "\u001f",
            PunchingQuantity,
            BatchNumber,
            ClipNoMg1,
            ClipNoMg2,
            Mg1ReceivingActual,
            Mg2ReceivingActual,
            OkSheetQuantity,
            OperatorCode,
            MoldCode,
            CutterCode);
}

/// <summary>
/// 模切 PLC 快照字段项。
/// </summary>
public sealed record DieCuttingSnapshotItem(
    string Code,
    string Name,
    string Value);

/// <summary>
/// 模切设备状态快照，独立于出站记录上传。
/// </summary>
public sealed class DieCuttingDeviceStatusSnapshot
{
    /// <summary>
    /// 状态采集时间。
    /// </summary>
    public DateTime CapturedAt { get; set; }

    /// <summary>
    /// PLC R100 原始状态码。
    /// </summary>
    public short StatusCode { get; set; }

    /// <summary>
    /// 状态诊断消息。
    /// </summary>
    public IReadOnlyList<string> Messages { get; set; } = [];

    /// <summary>
    /// 状态码是否属于 MES 约定的有效范围。
    /// </summary>
    public bool IsKnownStatus => StatusCode is -1 or 0 or 1 or 2 or 3;

    public string CreateFingerprint()
        => StatusCode.ToString(System.Globalization.CultureInfo.InvariantCulture);
}
