using IIoT.Edge.Application.Modules;
using IIoT.Edge.Application.Modules.Hardware;
using IIoT.Edge.SharedKernel.Enums;

namespace IIoT.Edge.Module.Homogenization.Config.Hardware;

/// <summary>
/// 匀浆硬件模板提供者，把插件内 PLC 信号清单转换为宿主可导入的 IO 模板。
/// </summary>
public sealed class HomogenizationHardwareProfileProvider
    : ModuleHardwareProfileProviderBase<HomogenizationSignalDefinition>
{
    /// <summary>
    /// 当前硬件模板所属的匀浆模块标识。
    /// </summary>
    public override string ModuleId => DependencyInjection.ModuleKey;

    /// <summary>
    /// 匀浆插件维护的 PLC 信号清单，业务信号不放到共享层。
    /// </summary>
    protected override IReadOnlyList<HomogenizationSignalDefinition> Signals
        => HomogenizationPlcSignalProfile.Signals;

    /// <summary>
    /// 匀浆模板要求信号必须有分类，便于 UI 按交互、连续数据、单点数据分组展示。
    /// </summary>
    protected override bool RequireCategory => true;

    /// <summary>
    /// 匀浆默认 PLC 连接参数，作为开发样本和新设备导入的基础值。
    /// </summary>
    public override ModulePlcDefaults GetDefaultPlcSettings()
        => new(PlcType.Mc.ToString(), 3000, 6000);

    protected override string CreateTemplateRemark(HomogenizationSignalDefinition signal)
        => $"匀浆模块 - {signal.DisplayName}";
}
