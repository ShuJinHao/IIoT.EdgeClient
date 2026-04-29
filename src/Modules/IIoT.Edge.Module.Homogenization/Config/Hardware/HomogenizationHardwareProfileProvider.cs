using IIoT.Edge.Application.Modules;
using IIoT.Edge.Application.Modules.Hardware;
using IIoT.Edge.SharedKernel.Enums;

namespace IIoT.Edge.Module.Homogenization.Config.Hardware;

public sealed class HomogenizationHardwareProfileProvider
    : ModuleHardwareProfileProviderBase<HomogenizationSignalDefinition>
{
    public override string ModuleId => DependencyInjection.ModuleKey;

    protected override IReadOnlyList<HomogenizationSignalDefinition> Signals
        => HomogenizationPlcSignalProfile.Signals;

    protected override bool RequireCategory => true;

    public override ModulePlcDefaults GetDefaultPlcSettings()
        => new(PlcType.Mc.ToString(), 3000, 6000);

    protected override string CreateTemplateRemark(HomogenizationSignalDefinition signal)
        => $"匀浆模块 - {signal.DisplayName}";

    protected override string FormatProtocolSummaryLine(HomogenizationSignalDefinition signal)
        => $"{signal.Label}：分类 {signal.Category}，分组 {signal.GroupName}，方向 {signal.Direction}，类型 {signal.DataType}，长度 {signal.AddressCount}，排序 {signal.SortOrder}";
}
