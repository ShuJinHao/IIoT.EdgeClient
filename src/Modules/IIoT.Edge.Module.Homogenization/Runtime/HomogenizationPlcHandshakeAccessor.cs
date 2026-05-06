using IIoT.Edge.Application.Abstractions.Modules;
using IIoT.Edge.Application.Abstractions.Plc.Signals;
using IIoT.Edge.Module.Homogenization.Config;
using IIoT.Edge.Module.Homogenization.Config.Hardware;

namespace IIoT.Edge.Module.Homogenization.Runtime;

/// <summary>
/// 匀浆信号交互访问器，封装 PLC 触发点、上位机应答点和握手码表之间的对应关系。
/// </summary>
internal sealed class HomogenizationPlcHandshakeAccessor
{
    private readonly ILogicalSignalAccessor<HomogenizationPlcSignals.Interaction> _signals;
    private readonly HomogenizationPlcCodeOptions _codeOptions;

    private static readonly IReadOnlyDictionary<HomogenizationPlcSignals.Interaction, HomogenizationPlcSignals.Interaction> AckSignals =
        new Dictionary<HomogenizationPlcSignals.Interaction, HomogenizationPlcSignals.Interaction>
        {
            [HomogenizationPlcSignals.Interaction.进站触发] = HomogenizationPlcSignals.Interaction.进站应答,
            [HomogenizationPlcSignals.Interaction.出料触发] = HomogenizationPlcSignals.Interaction.出料应答,
            [HomogenizationPlcSignals.Interaction.配方上传触发] = HomogenizationPlcSignals.Interaction.配方应答,
            [HomogenizationPlcSignals.Interaction.设备状态上传触发] = HomogenizationPlcSignals.Interaction.设备状态应答
        };

    /// <summary>
    /// 使用强类型信号访问器和 PLC 握手码表创建交互访问器。
    /// </summary>
    public HomogenizationPlcHandshakeAccessor(
        ILogicalSignalAccessor<HomogenizationPlcSignals.Interaction> signals,
        HomogenizationPlcCodeOptions codeOptions)
    {
        _signals = signals ?? throw new ArgumentNullException(nameof(signals));
        _codeOptions = codeOptions ?? throw new ArgumentNullException(nameof(codeOptions));
    }

    /// <summary>
    /// 判断指定 PLC→PC 触发点是否处于触发状态。
    /// </summary>
    public bool IsTriggered(HomogenizationPlcSignals.Interaction triggerSignal)
        => ReadTrigger(triggerSignal) == _codeOptions.SignalTrigger;

    /// <summary>
    /// 判断指定 PLC→PC 触发点是否处于复位状态。
    /// </summary>
    public bool IsReset(HomogenizationPlcSignals.Interaction triggerSignal)
        => ReadTrigger(triggerSignal) == _codeOptions.SignalReset;

    /// <summary>
    /// 按当前触发点写入正常完成应答码。
    /// </summary>
    public void ReplyOk(HomogenizationPlcSignals.Interaction triggerSignal)
        => Reply(triggerSignal, _codeOptions.AckOk);

    /// <summary>
    /// 按当前触发点写入异常失败应答码。
    /// </summary>
    public void ReplyException(HomogenizationPlcSignals.Interaction triggerSignal)
        => Reply(triggerSignal, _codeOptions.AckException);

    /// <summary>
    /// 按当前触发点写入 MES 业务拒绝应答码。
    /// </summary>
    public void ReplyMesNg(HomogenizationPlcSignals.Interaction triggerSignal)
        => Reply(triggerSignal, _codeOptions.AckMesNg);

    /// <summary>
    /// 按当前触发点写入复位应答码。
    /// </summary>
    public void ReplyReset(HomogenizationPlcSignals.Interaction triggerSignal)
        => Reply(triggerSignal, _codeOptions.SignalReset);

    /// <summary>
    /// 根据 MES 调用结果写入对应应答码，业务任务不直接接触 PLC code 值。
    /// </summary>
    public void ReplyResult(HomogenizationPlcSignals.Interaction triggerSignal, MesCallResult result)
    {
        if (result.IsSuccess)
        {
            ReplyOk(triggerSignal);
            return;
        }

        if (result.Outcome == MesCallOutcome.BusinessRejected)
        {
            ReplyMesNg(triggerSignal);
            return;
        }

        ReplyException(triggerSignal);
    }

    /// <summary>
    /// 镜像心跳输入到心跳输出，心跳仍属于信号交互线程，但不使用触发/应答业务码。
    /// </summary>
    public (ushort Input, ushort Output) MirrorHeartbeat()
    {
        var input = _signals.ReadUInt16(HomogenizationPlcSignals.Interaction.心跳输入);
        var output = input == 0 ? (ushort)1 : input;
        _signals.WriteUInt16(HomogenizationPlcSignals.Interaction.心跳输出, output);
        return (input, output);
    }

    private ushort ReadTrigger(HomogenizationPlcSignals.Interaction triggerSignal)
    {
        EnsureTriggerSignal(triggerSignal);
        return _signals.ReadUInt16(triggerSignal);
    }

    private void Reply(HomogenizationPlcSignals.Interaction triggerSignal, ushort value)
        => _signals.WriteUInt16(ResolveAckSignal(triggerSignal), value);

    private static HomogenizationPlcSignals.Interaction ResolveAckSignal(HomogenizationPlcSignals.Interaction triggerSignal)
    {
        EnsureTriggerSignal(triggerSignal);
        return AckSignals[triggerSignal];
    }

    private static void EnsureTriggerSignal(HomogenizationPlcSignals.Interaction triggerSignal)
    {
        if (!AckSignals.ContainsKey(triggerSignal))
        {
            throw new InvalidOperationException($"匀浆信号【{triggerSignal}】不是 PLC→PC 业务触发点，不能用于触发/应答判断。");
        }
    }
}
