using IIoT.Edge.Application.Abstractions.Plc.Signals;
using IIoT.Edge.Application.Abstractions.Time;
using IIoT.Edge.Module.Homogenization.Config;
using IIoT.Edge.Module.Homogenization.Config.Hardware;
using IIoT.Edge.Module.Homogenization.Payload;
using IIoT.Edge.Module.Homogenization.Resources;

namespace IIoT.Edge.Module.Homogenization.Runtime;

/// <summary>
/// 按宿主注入的 IO 绑定从 PLC 缓冲区读写匀浆信号。
/// </summary>
internal sealed class HomogenizationSignalCodec
{
    private readonly ILogicalSignalAccessor<HomogenizationSignal> _signals;
    private readonly IProductionTimeProvider _productionTime;

    public HomogenizationSignalCodec(
        ILogicalSignalAccessor<HomogenizationSignal> signals,
        IProductionTimeProvider productionTime)
    {
        _signals = signals ?? throw new ArgumentNullException(nameof(signals));
        _productionTime = productionTime;
    }

    /// <summary>
    /// 从实时数据 label 组采集当前 PLC 快照。
    /// </summary>
    public HomogenizationRealtimeSnapshot CaptureRealtimeSnapshot()
        => new()
        {
            CapturedAt = _productionTime.BusinessNow,
            StirringSpeed = _signals.ReadInt16(HomogenizationSignal.实时搅拌转速),
            StirringCurrent = _signals.ReadInt16(HomogenizationSignal.实时搅拌电流),
            DispersionSpeed = _signals.ReadInt16(HomogenizationSignal.实时分散转速),
            DispersionCurrent = _signals.ReadInt16(HomogenizationSignal.实时分散电流),
            Temperature = _signals.ReadInt16(HomogenizationSignal.实时温度),
            Vacuum = _signals.ReadInt16(HomogenizationSignal.实时真空度)
        };

    /// <summary>
    /// 从配方 label 组采集数组参数，浮点值按两个 PLC word 合成。
    /// </summary>
    public HomogenizationRecipeSnapshot CaptureRecipeSnapshot()
        => new()
        {
            CapturedAt = _productionTime.BusinessNow,
            StirringSpeed = _signals.ReadIntArray(HomogenizationSignal.配方搅拌转速, 30),
            DispersionSpeed = _signals.ReadIntArray(HomogenizationSignal.配方分散转速, 30),
            Ncm = _signals.ReadFloatArray(HomogenizationSignal.配方NCM, 30),
            Sp1 = _signals.ReadFloatArray(HomogenizationSignal.配方SP1, 30),
            Nmp = _signals.ReadFloatArray(HomogenizationSignal.配方NMP, 30),
            GlueSolution = _signals.ReadFloatArray(HomogenizationSignal.配方胶液, 30),
            Cnt = _signals.ReadFloatArray(HomogenizationSignal.配方CNT, 30),
            Vacuum = _signals.ReadBoolArray(HomogenizationSignal.配方真空, 30),
            Time = _signals.ReadIntArray(HomogenizationSignal.配方时间, 30),
            Temperature = _signals.ReadIntArray(HomogenizationSignal.配方温度, 30)
                .Select(static value => (double)value)
                .ToArray(),
            StopStep = _signals.ReadBoolArray(HomogenizationSignal.配方停机步, 30)
        };

    /// <summary>
    /// 读取设备状态码，并按 MES 码表转换为状态文本。
    /// </summary>
    public HomogenizationEquipmentStatusSnapshot CaptureEquipmentStatusSnapshot(HomogenizationMesCodeOptions mesCodes)
    {
        var statusCode = _signals.ReadInt16(HomogenizationSignal.设备状态值);
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
}
