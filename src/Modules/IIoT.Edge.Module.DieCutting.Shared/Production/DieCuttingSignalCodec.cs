using IIoT.Edge.Application.Abstractions.Plc.Signals;
using IIoT.Edge.Application.Abstractions.Time;
using IIoT.Edge.Application.Modules.Hardware;
using IIoT.Edge.Module.DieCutting.Config;
using IIoT.Edge.Module.DieCutting.Config.Io;
using IIoT.Edge.Module.DieCutting.Payload;

namespace IIoT.Edge.Module.DieCutting.Production;

/// <summary>
/// 模切 PLC 只读数据解码器，只读取业务数据快照，不参与 PLC 写入或应答。
/// </summary>
internal sealed class DieCuttingSignalCodec
{
    public static readonly IReadOnlyList<string> RequiredSignalKeys = Enum.GetValues<DieCuttingPlcSignals.SingleRead>()
        .Select(static key => EnumPlcSignalMetadata.GetRead(key).SignalKey)
        .Concat(Enum.GetValues<DieCuttingPlcSignals.ContinuousRead>()
            .Select(static key => EnumPlcSignalMetadata.GetRead(key).SignalKey))
        .ToArray();

    private readonly ILogicalSignalAccessor<DieCuttingPlcSignals.SingleRead> _singleReadSignals;
    private readonly ILogicalSignalAccessor<DieCuttingPlcSignals.ContinuousRead> _continuousReadSignals;
    private readonly IProductionTimeProvider _productionTime;

    /// <summary>
    /// 使用单点读和连续读访问器创建模切解码器。
    /// </summary>
    public DieCuttingSignalCodec(
        ILogicalSignalAccessor<DieCuttingPlcSignals.SingleRead> singleReadSignals,
        ILogicalSignalAccessor<DieCuttingPlcSignals.ContinuousRead> continuousReadSignals,
        IProductionTimeProvider productionTime)
    {
        _singleReadSignals = singleReadSignals ?? throw new ArgumentNullException(nameof(singleReadSignals));
        _continuousReadSignals = continuousReadSignals ?? throw new ArgumentNullException(nameof(continuousReadSignals));
        _productionTime = productionTime;
    }

    /// <summary>
    /// 从当前 buffer 采集模切 MES 上传快照。
    /// </summary>
    public DieCuttingRealtimeSnapshot CaptureRealtimeSnapshot(
        DieCuttingDeviceIdentity identity,
        DateTime windowStartAt)
    {
        var capturedAt = _productionTime.BusinessNow;
        var batchNumber = ReadText(DieCuttingPlcSignals.ContinuousRead.批次号);
        var mg1ClipNo = ReadText(DieCuttingPlcSignals.ContinuousRead.弹夹号MG1);
        var mg2ClipNo = ReadText(DieCuttingPlcSignals.ContinuousRead.弹夹号MG2);
        var unwindingLength = _singleReadSignals.ReadInt32(DieCuttingPlcSignals.SingleRead.放卷长度);
        var punchingQuantity = _singleReadSignals.ReadInt32(DieCuttingPlcSignals.SingleRead.实际产量);
        var punchingSpeed = decimal.Round(
            _singleReadSignals.ReadInt32(DieCuttingPlcSignals.SingleRead.冲切速度) / 100000m,
            5);
        var plateLength = ToDecimal(_singleReadSignals.ReadInt32(DieCuttingPlcSignals.SingleRead.实际长度));
        var plateWidth = ToDecimal(_singleReadSignals.ReadInt32(DieCuttingPlcSignals.SingleRead.极片宽度));
        var mg1Set = _singleReadSignals.ReadUInt16(DieCuttingPlcSignals.SingleRead.收料片数MG1设定);
        var mg1Actual = _singleReadSignals.ReadUInt16(DieCuttingPlcSignals.SingleRead.收料片数MG1实际);
        var mg2Set = _singleReadSignals.ReadUInt16(DieCuttingPlcSignals.SingleRead.收料片数MG2设定);
        var mg2Actual = _singleReadSignals.ReadUInt16(DieCuttingPlcSignals.SingleRead.收料片数MG2实际);
        var okSheetQuantity = _singleReadSignals.ReadInt32(DieCuttingPlcSignals.SingleRead.弹夹OK级片数量);

        return new DieCuttingRealtimeSnapshot
        {
            CapturedAt = capturedAt,
            WindowStartAt = windowStartAt,
            WindowCompleteAt = capturedAt,
            BatchNumber = batchNumber,
            ClipNo = SelectClipNo(mg1ClipNo, mg2ClipNo),
            ClipNoMg1 = mg1ClipNo,
            ClipNoMg2 = mg2ClipNo,
            PunchingQuantity = punchingQuantity,
            PunchingSpeed = punchingSpeed,
            UnwindingLength = unwindingLength,
            PunchingUom = "PCS",
            PunchingDeviceCode = identity.DeviceCode,
            PunchingDeviceName = identity.DeviceName,
            Mg1ReceivingSet = mg1Set,
            Mg1ReceivingActual = mg1Actual,
            Mg2ReceivingSet = mg2Set,
            Mg2ReceivingActual = mg2Actual,
            OkSheetQuantity = okSheetQuantity,
            PlateLengthMm = plateLength,
            PlateWidthMm = plateWidth,
            OperatorCode = ReadText(DieCuttingPlcSignals.ContinuousRead.操作员工号),
            MoldCode = ReadText(DieCuttingPlcSignals.ContinuousRead.模具编号),
            CutterCode = ReadText(DieCuttingPlcSignals.ContinuousRead.切刀编号),
            RawItems = BuildRawItems(
                punchingQuantity,
                punchingSpeed,
                plateLength,
                plateWidth,
                unwindingLength,
                mg1Set,
                mg1Actual,
                mg2Set,
                mg2Actual,
                okSheetQuantity)
        };
    }

    public DieCuttingDeviceStatusSnapshot CaptureDeviceStatusSnapshot()
    {
        var statusCode = _singleReadSignals.ReadInt16(DieCuttingPlcSignals.SingleRead.设备状态);
        var messages = new List<string>();
        if (statusCode == -1)
        {
            messages.Add("PLC 返回报警状态。");
        }

        if (statusCode is not (-1 or 0 or 1 or 2 or 3))
        {
            messages.Add($"设备状态码未知：{statusCode}。");
        }

        return new DieCuttingDeviceStatusSnapshot
        {
            CapturedAt = _productionTime.BusinessNow,
            StatusCode = statusCode,
            Messages = messages
        };
    }

    private string ReadText(DieCuttingPlcSignals.ContinuousRead key)
        => _continuousReadSignals.ReadAscii(key).Trim();

    private static string SelectClipNo(string mg1, string mg2)
    {
        if (!string.IsNullOrWhiteSpace(mg1))
        {
            return mg1.Trim();
        }

        return mg2.Trim();
    }

    private IReadOnlyList<DieCuttingSnapshotItem> BuildRawItems(
        long punchingQuantity,
        decimal punchingSpeed,
        decimal? plateLength,
        decimal? plateWidth,
        long unwindingLength,
        int mg1Set,
        int mg1Actual,
        int mg2Set,
        int mg2Actual,
        long okSheetQuantity)
        =>
        [
            Item("unwindingLength", "放卷长度", unwindingLength),
            Item("moldLifeSetting", "模具寿命设定", _singleReadSignals.ReadInt32(DieCuttingPlcSignals.SingleRead.模具寿命设定)),
            Item("cutterLifeSetting", "切刀寿命设定", _singleReadSignals.ReadInt32(DieCuttingPlcSignals.SingleRead.切刀寿命设定)),
            Item("punchingQuantity", "实际产量", punchingQuantity),
            Item("punchingSpeed", "模切速度", punchingSpeed),
            Item("mg1ReceivingSet", "MG#1 收料片数设定", mg1Set),
            Item("mg1ReceivingActual", "MG#1 收料片数实际", mg1Actual),
            Item("mg2ReceivingSet", "MG#2 收料片数设定", mg2Set),
            Item("mg2ReceivingActual", "MG#2 收料片数实际", mg2Actual),
            Item("okSheetQuantity", "弹夹OK级片数量", okSheetQuantity),
            Item("rollTensionActual", "卷料张力实际", _singleReadSignals.ReadUInt16(DieCuttingPlcSignals.SingleRead.卷料张力实际)),
            Item("rollTensionSet", "卷料张力设定", _singleReadSignals.ReadUInt16(DieCuttingPlcSignals.SingleRead.卷料张力设定)),
            Item("correctionPositionActual", "自动纠偏位置实际", _singleReadSignals.ReadInt32(DieCuttingPlcSignals.SingleRead.自动纠偏位置实际)),
            Item("cutterPositionActual", "切刀位置实际", _singleReadSignals.ReadInt32(DieCuttingPlcSignals.SingleRead.切刀位置实际)),
            Item("heartbeat", "心跳", _singleReadSignals.ReadUInt16(DieCuttingPlcSignals.SingleRead.心跳)),
            Item("polePieceWidth", "极片宽度", plateWidth),
            Item("actualLength", "实际长度", plateLength),
            Item("theoreticalSheetQuantity", "理论片数", _singleReadSignals.ReadUInt16(DieCuttingPlcSignals.SingleRead.理论片数)),
            Item("mesCommunicationException", "MES通讯异常", ReadBool(DieCuttingPlcSignals.SingleRead.MES通讯异常)),
            Item("loadConfirm", "上料确认", ReadBool(DieCuttingPlcSignals.SingleRead.上料确认)),
            Item("operatorCode", "操作员工号", ReadText(DieCuttingPlcSignals.ContinuousRead.操作员工号)),
            Item("moldCode", "模具编号", ReadText(DieCuttingPlcSignals.ContinuousRead.模具编号)),
            Item("cutterCode", "切刀编号", ReadText(DieCuttingPlcSignals.ContinuousRead.切刀编号)),
            Item("clipNoCache", "弹夹编号缓存", ReadText(DieCuttingPlcSignals.ContinuousRead.弹夹编号缓存))
        ];

    private bool ReadBool(DieCuttingPlcSignals.SingleRead key)
        => _singleReadSignals.ReadUInt16(key) != 0;

    private static decimal? ToDecimal(int value)
        => value;

    private static DieCuttingSnapshotItem Item(string code, string name, object? value)
        => new(code, name, value?.ToString() ?? string.Empty);
}
