using IIoT.Edge.Application.Abstractions.Plc.Signals;
using IIoT.Edge.Application.Abstractions.Time;
using IIoT.Edge.Module.Homogenization.Config;
using IIoT.Edge.Module.Homogenization.Config.Hardware;
using IIoT.Edge.Module.Homogenization.Payload;
using IIoT.Edge.Module.Homogenization.Resources;

namespace IIoT.Edge.Module.Homogenization.Runtime;

/// <summary>
/// 匀浆业务数据解码器，只读取单点读数据和连续读数据，不参与 PLC 信号交互应答。
/// </summary>
internal sealed class HomogenizationSignalCodec
{
    private readonly ILogicalSignalAccessor<HomogenizationPlcSignals.SingleRead> _singleReadSignals;
    private readonly ILogicalSignalAccessor<HomogenizationPlcSignals.ContinuousRead> _continuousReadSignals;
    private readonly IProductionTimeProvider _productionTime;

    /// <summary>
    /// 使用两类读数据访问器创建解码器，确保业务数据不再混入信号交互枚举。
    /// </summary>
    public HomogenizationSignalCodec(
        ILogicalSignalAccessor<HomogenizationPlcSignals.SingleRead> singleReadSignals,
        ILogicalSignalAccessor<HomogenizationPlcSignals.ContinuousRead> continuousReadSignals,
        IProductionTimeProvider productionTime)
    {
        _singleReadSignals = singleReadSignals ?? throw new ArgumentNullException(nameof(singleReadSignals));
        _continuousReadSignals = continuousReadSignals ?? throw new ArgumentNullException(nameof(continuousReadSignals));
        _productionTime = productionTime;
    }

    /// <summary>
    /// 从托盘码连续读数据区读取当前托盘码。
    /// </summary>
    public string ReadTrayCode()
        => _continuousReadSignals.ReadAscii(HomogenizationPlcSignals.ContinuousRead.托盘码);

    /// <summary>
    /// 从实时数据单点组采集当前 PLC 快照。
    /// </summary>
    public HomogenizationRealtimeSnapshot CaptureRealtimeSnapshot()
        => new()
        {
            CapturedAt = _productionTime.BusinessNow,
            StirringSpeed = _singleReadSignals.ReadInt16(HomogenizationPlcSignals.SingleRead.实时搅拌转速),
            StirringCurrent = _singleReadSignals.ReadInt16(HomogenizationPlcSignals.SingleRead.实时搅拌电流),
            DispersionSpeed = _singleReadSignals.ReadInt16(HomogenizationPlcSignals.SingleRead.实时分散转速),
            DispersionCurrent = _singleReadSignals.ReadInt16(HomogenizationPlcSignals.SingleRead.实时分散电流),
            Temperature = _singleReadSignals.ReadInt16(HomogenizationPlcSignals.SingleRead.实时温度),
            Vacuum = _singleReadSignals.ReadInt16(HomogenizationPlcSignals.SingleRead.实时真空度)
        };

    /// <summary>
    /// 从配方连续读数据组采集数组参数，浮点值按两个 PLC word 合成。
    /// </summary>
    public HomogenizationRecipeSnapshot CaptureRecipeSnapshot()
        => new()
        {
            CapturedAt = _productionTime.BusinessNow,
            StirringSpeed = _continuousReadSignals.ReadIntArray(HomogenizationPlcSignals.ContinuousRead.配方搅拌转速, 30),
            DispersionSpeed = _continuousReadSignals.ReadIntArray(HomogenizationPlcSignals.ContinuousRead.配方分散转速, 30),
            Ncm = _continuousReadSignals.ReadFloatArray(HomogenizationPlcSignals.ContinuousRead.配方NCM, 30),
            Sp1 = _continuousReadSignals.ReadFloatArray(HomogenizationPlcSignals.ContinuousRead.配方SP1, 30),
            Nmp = _continuousReadSignals.ReadFloatArray(HomogenizationPlcSignals.ContinuousRead.配方NMP, 30),
            GlueSolution = _continuousReadSignals.ReadFloatArray(HomogenizationPlcSignals.ContinuousRead.配方胶液, 30),
            Cnt = _continuousReadSignals.ReadFloatArray(HomogenizationPlcSignals.ContinuousRead.配方CNT, 30),
            Vacuum = _continuousReadSignals.ReadBoolArray(HomogenizationPlcSignals.ContinuousRead.配方真空, 30),
            Time = _continuousReadSignals.ReadIntArray(HomogenizationPlcSignals.ContinuousRead.配方时间, 30),
            Temperature = _continuousReadSignals.ReadIntArray(HomogenizationPlcSignals.ContinuousRead.配方温度, 30)
                .Select(static value => (double)value)
                .ToArray(),
            StopStep = _continuousReadSignals.ReadBoolArray(HomogenizationPlcSignals.ContinuousRead.配方停机步, 30)
        };

    /// <summary>
    /// 读取设备状态码，并按 MES 码表转换为状态文本。
    /// </summary>
    public HomogenizationEquipmentStatusSnapshot CaptureEquipmentStatusSnapshot(HomogenizationMesCodeOptions mesCodes)
    {
        var statusCode = _singleReadSignals.ReadInt16(HomogenizationPlcSignals.SingleRead.设备状态值);
        var statusText = mesCodes.ResolveEquipmentStatusText(statusCode);

        var messages = new List<string>();
        if (statusCode == -1)
        {
            messages.Add("PLC 返回报警状态。");
        }

        var unknownStatus = HomogenizationText.Get("Homogenization_EquipmentStatus_Unknown", "未知");
        if (string.Equals(statusText, unknownStatus, StringComparison.Ordinal))
        {
            messages.Add($"设备状态码未知：{statusCode}。");
        }

        return new HomogenizationEquipmentStatusSnapshot
        {
            CapturedAt = _productionTime.BusinessNow,
            StatusCode = statusCode,
            StatusText = statusText,
            Messages = messages
        };
    }

    /// <summary>
    /// 从出料单点读数据组采集出料记录所需的补充字段。
    /// </summary>
    public HomogenizationOutboundReadings CaptureOutboundReadings()
        => new(
            _singleReadSignals.ReadUInt16(HomogenizationPlcSignals.SingleRead.出料CNT实际值),
            _singleReadSignals.ReadUInt16(HomogenizationPlcSignals.SingleRead.出料CNT目标值),
            _singleReadSignals.ReadUInt16(HomogenizationPlcSignals.SingleRead.出料CNTA罐重量),
            _singleReadSignals.ReadUInt16(HomogenizationPlcSignals.SingleRead.出料CNTB罐重量),
            _singleReadSignals.ReadUInt16(HomogenizationPlcSignals.SingleRead.出料NMP实际值),
            _singleReadSignals.ReadUInt16(HomogenizationPlcSignals.SingleRead.出料NMP目标值),
            _singleReadSignals.ReadUInt16(HomogenizationPlcSignals.SingleRead.出料胶液实际值),
            _singleReadSignals.ReadUInt16(HomogenizationPlcSignals.SingleRead.出料设定搅拌时间),
            _singleReadSignals.ReadUInt16(HomogenizationPlcSignals.SingleRead.出料剩余搅拌时间),
            _singleReadSignals.ReadUInt16(HomogenizationPlcSignals.SingleRead.出料设定分散时间),
            _singleReadSignals.ReadUInt16(HomogenizationPlcSignals.SingleRead.出料剩余分散时间));
}

/// <summary>
/// 出料业务所需的单点读数据快照，由匀浆数据解码器从 PLC 缓冲区读取。
/// </summary>
internal sealed record HomogenizationOutboundReadings(
    ushort CntActualKg,
    ushort CntTargetKg,
    ushort CntTankAWeightKg,
    ushort CntTankBWeightKg,
    ushort NmpActualKg,
    ushort NmpTargetKg,
    ushort GlueActualKg,
    ushort SetStirringTimeMinutes,
    ushort RemainingStirringTimeMinutes,
    ushort SetDispersionTimeMinutes,
    ushort RemainingDispersionTimeMinutes);
