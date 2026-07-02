using IIoT.Edge.SharedKernel.DataPipeline.CellData;

namespace IIoT.Edge.Module.DieCutting.Payload;

/// <summary>
/// 模切数据管道载荷。PLC 采样任务只负责生成该载荷，MES/Cloud consumer 从 DataPipeline 消费。
/// </summary>
public sealed class DieCuttingCellData : CellDataBase
{
    /// <summary>
    /// 模切数据管道记录类型，用于区分出站生产记录和设备状态记录。
    /// </summary>
    public static class RecordKinds
    {
        public const string RealtimeOutbound = "RealtimeOutbound";
        public const string DeviceStatus = "DeviceStatus";
    }

    /// <summary>
    /// 工序类型由 AP/CP 插件写入。
    /// </summary>
    public override string ProcessType => string.IsNullOrWhiteSpace(ModuleProcessType)
        ? "DieCutting"
        : ModuleProcessType;

    /// <summary>
    /// AP/CP 模切插件的实际工序类型。
    /// </summary>
    public string ModuleProcessType { get; set; } = string.Empty;

    /// <summary>
    /// 模切记录展示名，优先显示弹夹号。
    /// </summary>
    public override string DisplayLabel => string.IsNullOrWhiteSpace(ClipNo) ? DeviceName : ClipNo;

    /// <summary>
    /// 本条载荷的记录类型。
    /// </summary>
    public string RecordKind { get; set; } = RecordKinds.RealtimeOutbound;

    /// <summary>
    /// PLC 快照采集时间。
    /// </summary>
    public DateTime CapturedAt { get; set; }

    /// <summary>
    /// 本轮采样窗口开始时间。
    /// </summary>
    public DateTime WindowStartAt { get; set; }

    /// <summary>
    /// 本轮采样窗口完成时间。
    /// </summary>
    public DateTime WindowCompleteAt { get; set; }

    /// <summary>
    /// PLC 读取到的批次号。
    /// </summary>
    public string BatchNumber { get; set; } = string.Empty;

    /// <summary>
    /// 当前弹夹号。
    /// </summary>
    public string ClipNo { get; set; } = string.Empty;

    /// <summary>
    /// 一号收料弹夹号。
    /// </summary>
    public string ClipNoMg1 { get; set; } = string.Empty;

    /// <summary>
    /// 二号收料弹夹号。
    /// </summary>
    public string ClipNoMg2 { get; set; } = string.Empty;

    /// <summary>
    /// 模切设备编码。
    /// </summary>
    public string PunchingDeviceCode { get; set; } = string.Empty;

    /// <summary>
    /// 模切设备名称。
    /// </summary>
    public string PunchingDeviceName { get; set; } = string.Empty;

    /// <summary>
    /// 实际产量。
    /// </summary>
    public long PunchingQuantity { get; set; }

    /// <summary>
    /// 实际产量单位。
    /// </summary>
    public string PunchingUom { get; set; } = "PCS";

    /// <summary>
    /// 冲切速度。
    /// </summary>
    public decimal PunchingSpeed { get; set; }

    /// <summary>
    /// 放卷长度。
    /// </summary>
    public long UnwindingLength { get; set; }

    /// <summary>
    /// 一号收料片数设定值。
    /// </summary>
    public int Mg1ReceivingSet { get; set; }

    /// <summary>
    /// 一号收料片数实际值。
    /// </summary>
    public int Mg1ReceivingActual { get; set; }

    /// <summary>
    /// 二号收料片数设定值。
    /// </summary>
    public int Mg2ReceivingSet { get; set; }

    /// <summary>
    /// 二号收料片数实际值。
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
    /// 原始 PLC 快照项，供排查字段映射问题。
    /// </summary>
    public List<DieCuttingSnapshotItem> RawItems { get; set; } = [];

    /// <summary>
    /// 设备状态码，仅设备状态记录使用。
    /// </summary>
    public short? StatusCode { get; set; }

    /// <summary>
    /// 设备状态附加消息。
    /// </summary>
    public List<string> StatusMessages { get; set; } = [];
}
