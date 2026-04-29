using IIoT.Edge.Application.Modules;
using IIoT.Edge.Application.Modules.Hardware;
using IIoT.Edge.Module.Stacking.Constants;
using IIoT.Edge.SharedKernel.Enums;

namespace IIoT.Edge.Module.Stacking.Runtime;

public sealed class StackingHardwareProfileProvider
    : ModuleHardwareProfileProviderBase<StackingSignalDefinition>
{
    public override string ModuleId => StackingModuleConstants.ModuleId;

    protected override IReadOnlyList<StackingSignalDefinition> Signals
        => StackingPlcSignalProfile.Signals;

    protected override bool ValidateSequentialOrder => true;

    public override ModulePlcDefaults GetDefaultPlcSettings()
        => new(PlcType.S7.ToString(), 3000, 102);

    protected override string CreateTemplateRemark(StackingSignalDefinition signal)
        => $"叠片模块 - {signal.DisplayName}";

    protected override string FormatProtocolSummaryLine(StackingSignalDefinition signal)
        => $"{signal.Label}：默认地址={signal.DefaultAddress}，方向 {signal.Direction}，类型 {signal.DataType}，长度 {signal.AddressCount}，排序 {signal.SortOrder}";
}
