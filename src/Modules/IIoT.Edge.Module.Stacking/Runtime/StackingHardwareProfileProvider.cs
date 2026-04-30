using IIoT.Edge.Application.Modules;
using IIoT.Edge.Application.Modules.Hardware;
using IIoT.Edge.Module.Stacking.Constants;
using IIoT.Edge.SharedKernel.Enums;

namespace IIoT.Edge.Module.Stacking.Runtime;

/// <summary>
/// 叠片硬件模板提供者，把插件内信号清单转换为默认 IO 模板和协议摘要。
/// </summary>
public sealed class StackingHardwareProfileProvider
    : ModuleHardwareProfileProviderBase<StackingSignalDefinition>
{
    /// <summary>
    /// 硬件模板所属模块。
    /// </summary>
    public override string ModuleId => StackingModuleConstants.ModuleId;

    /// <summary>
    /// 叠片插件自己的 PLC 信号清单。
    /// </summary>
    protected override IReadOnlyList<StackingSignalDefinition> Signals
        => StackingPlcSignalProfile.Signals;

    /// <summary>
    /// 叠片样本要求信号排序连续，防止硬件模板顺序与运行时读取约定脱节。
    /// </summary>
    protected override bool ValidateSequentialOrder => true;

    /// <summary>
    /// 叠片开发样本默认使用 S7 协议。
    /// </summary>
    public override ModulePlcDefaults GetDefaultPlcSettings()
        => new(PlcType.S7.ToString(), 3000, 102);

    protected override string CreateTemplateRemark(StackingSignalDefinition signal)
        => $"叠片模块 - {signal.DisplayName}";

    protected override string FormatProtocolSummaryLine(StackingSignalDefinition signal)
        => $"{signal.Label}：默认地址={signal.DefaultAddress}，方向 {signal.Direction}，类型 {signal.DataType}，长度 {signal.AddressCount}，排序 {signal.SortOrder}";
}
