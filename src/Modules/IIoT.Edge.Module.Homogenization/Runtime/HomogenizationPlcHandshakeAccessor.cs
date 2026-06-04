using IIoT.Edge.Application.Abstractions.Mes;
﻿using IIoT.Edge.Application.Abstractions.Modules;
using IIoT.Edge.Application.Abstractions.Plc.Signals;
using IIoT.Edge.Module.Homogenization.Config;
using IIoT.Edge.Module.Homogenization.Config.Hardware;

namespace IIoT.Edge.Module.Homogenization.Runtime;

/// <summary>
/// 匀浆信号交互访问器，按业务动作封装 PLC 触发判断和上位机应答写入。
/// </summary>
internal sealed class HomogenizationPlcHandshakeAccessor
{
    private readonly ILogicalSignalAccessor<HomogenizationPlcSignals.Interaction> _signals;
    private readonly HomogenizationPlcCodeOptions _codeOptions;

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
    /// 判断指定业务动作的 PLC->PC 读点是否处于触发状态。
    /// </summary>
    public bool IsTriggered(HomogenizationPlcSignals.Interaction interaction)
        => ReadInteraction(interaction) == _codeOptions.SignalTrigger;

    /// <summary>
    /// 判断指定业务动作的 PLC->PC 读点是否处于复位状态。
    /// </summary>
    public bool IsReset(HomogenizationPlcSignals.Interaction interaction)
        => ReadInteraction(interaction) == _codeOptions.SignalReset;

    /// <summary>
    /// 向指定业务动作的 PC->PLC 写点写入正常完成应答码。
    /// </summary>
    public void ReplyOk(HomogenizationPlcSignals.Interaction interaction)
        => Reply(interaction, _codeOptions.AckOk);

    /// <summary>
    /// 向指定业务动作的 PC->PLC 写点写入异常失败应答码。
    /// </summary>
    public void ReplyException(HomogenizationPlcSignals.Interaction interaction)
        => Reply(interaction, _codeOptions.AckException);

    /// <summary>
    /// 向指定业务动作的 PC->PLC 写点写入 MES 业务拒绝应答码。
    /// </summary>
    public void ReplyMesNg(HomogenizationPlcSignals.Interaction interaction)
        => Reply(interaction, _codeOptions.AckMesNg);

    /// <summary>
    /// 向指定业务动作的 PC->PLC 写点写入复位应答码。
    /// </summary>
    public void ReplyReset(HomogenizationPlcSignals.Interaction interaction)
        => Reply(interaction, _codeOptions.SignalReset);

    /// <summary>
    /// 根据 MES 调用结果写入对应应答码，业务任务不直接接触 PLC code 值。
    /// </summary>
    public void ReplyResult(HomogenizationPlcSignals.Interaction interaction, MesCallResult result)
    {
        if (result.IsSuccess)
        {
            ReplyOk(interaction);
            return;
        }

        if (result.Outcome == MesCallOutcome.BusinessRejected)
        {
            ReplyMesNg(interaction);
            return;
        }

        ReplyException(interaction);
    }

    private ushort ReadInteraction(HomogenizationPlcSignals.Interaction interaction)
        => _signals.ReadUInt16(interaction);

    private void Reply(HomogenizationPlcSignals.Interaction interaction, ushort value)
        => _signals.WriteUInt16(interaction, value);
}

