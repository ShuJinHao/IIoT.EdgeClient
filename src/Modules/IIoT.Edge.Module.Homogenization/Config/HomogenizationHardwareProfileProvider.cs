using IIoT.Edge.Application.Abstractions.Modules;
using IIoT.Edge.Application.Modules.Hardware;
using IIoT.Edge.SharedKernel.Enums;

namespace IIoT.Edge.Module.Homogenization.Config;

/// <summary>
/// 匀浆硬件模板提供者，只保留模块参数，标准点位展开由通用基类完成。
/// </summary>
public sealed class HomogenizationHardwareProfileProvider
    : StandardModuleHardwareProfileProviderBase<
        HomogenizationPlcSignals.Interaction,
        HomogenizationPlcSignals.SingleRead,
        HomogenizationPlcSignals.ContinuousRead,
        HomogenizationPlcSignals.SingleWrite,
        HomogenizationPlcSignals.ContinuousWrite>
{
    public HomogenizationHardwareProfileProvider()
        : this(
            new EnumInteractionSignalProfile<HomogenizationPlcSignals.Interaction>(DependencyInjection.ModuleKey),
            new EnumReadSignalProfile<HomogenizationPlcSignals.SingleRead>(DependencyInjection.ModuleKey, "单点读数据"),
            new EnumReadSignalProfile<HomogenizationPlcSignals.ContinuousRead>(DependencyInjection.ModuleKey, "连续读数据"),
            new EnumWriteSignalProfile<HomogenizationPlcSignals.SingleWrite>(DependencyInjection.ModuleKey, "单点写数据"),
            new EnumWriteSignalProfile<HomogenizationPlcSignals.ContinuousWrite>(DependencyInjection.ModuleKey, "连续写数据"))
    {
    }

    public HomogenizationHardwareProfileProvider(
        IModulePlcSignalProfile<HomogenizationPlcSignals.Interaction> interactionProfile,
        IModulePlcSignalProfile<HomogenizationPlcSignals.SingleRead> singleReadProfile,
        IModulePlcSignalProfile<HomogenizationPlcSignals.ContinuousRead> continuousReadProfile,
        IModulePlcSignalProfile<HomogenizationPlcSignals.SingleWrite> singleWriteProfile,
        IModulePlcSignalProfile<HomogenizationPlcSignals.ContinuousWrite> continuousWriteProfile)
        : base(
            interactionProfile,
            singleReadProfile,
            continuousReadProfile,
            singleWriteProfile,
            continuousWriteProfile)
    {
    }

    /// <summary>
    /// 匀浆模板备注使用的中文模块名。
    /// </summary>
    protected override string ModuleDisplayName => "匀浆模块";

    public override ModulePlcDefaults GetDefaultPlcSettings()
        => new(PlcType.Mc.ToString(), 3000, 6000);

    public override PlcIoRuntimePolicy GetIoRuntimePolicy()
        => new(
            SignalLoopIntervalMs: 10,
            MaxSignalBlockWordCount: 100,
            WriteGapPolicy: PlcIoWriteGapPolicy.Zero);
}
