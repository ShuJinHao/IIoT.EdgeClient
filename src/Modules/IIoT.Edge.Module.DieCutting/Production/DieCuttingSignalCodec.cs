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
    public static readonly IReadOnlyList<string> RequiredSignalKeys =
    [
        EnumPlcSignalMetadata.GetRead(DieCuttingPlcSignals.SingleRead.实际产量).SignalKey,
        EnumPlcSignalMetadata.GetRead(DieCuttingPlcSignals.SingleRead.冲切速度).SignalKey,
        EnumPlcSignalMetadata.GetRead(DieCuttingPlcSignals.ContinuousRead.弹夹号MG1).SignalKey,
        EnumPlcSignalMetadata.GetRead(DieCuttingPlcSignals.ContinuousRead.弹夹号MG2).SignalKey
    ];

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
        DateTime windowStartAt,
        string? punchingLotNumber)
    {
        var capturedAt = _productionTime.BusinessNow;
        return new DieCuttingRealtimeSnapshot
        {
            CapturedAt = capturedAt,
            WindowStartAt = windowStartAt,
            WindowCompleteAt = capturedAt,
            ClipNo = ReadClipNo(),
            PunchingQuantity = _singleReadSignals.ReadInt32(DieCuttingPlcSignals.SingleRead.实际产量),
            PunchingSpeed = decimal.Round(
                _singleReadSignals.ReadInt32(DieCuttingPlcSignals.SingleRead.冲切速度) / 100000m,
                5),
            PunchingUom = "PCS",
            PunchingDeviceCode = identity.DeviceCode,
            PunchingDeviceName = identity.DeviceName,
            PunchingLotNumber = punchingLotNumber?.Trim() ?? string.Empty
        };
    }

    private string ReadClipNo()
    {
        var mg1 = _continuousReadSignals.ReadAscii(DieCuttingPlcSignals.ContinuousRead.弹夹号MG1);
        if (!string.IsNullOrWhiteSpace(mg1))
        {
            return mg1.Trim();
        }

        var mg2 = _continuousReadSignals.ReadAscii(DieCuttingPlcSignals.ContinuousRead.弹夹号MG2);
        return mg2.Trim();
    }
}
