using IIoT.Edge.Application.Abstractions.Modules;
using IIoT.Edge.Application.Abstractions.Plc.Signals;
using IIoT.Edge.Application.Modules;
using IIoT.Edge.Application.Modules.Hardware;
using IIoT.Edge.Module.Stacking.Constants;
using IIoT.Edge.SharedKernel.Enums;

namespace IIoT.Edge.Module.Stacking.Config.Hardware;

/// <summary>
/// 叠片硬件模板提供者，把插件内信号清单转换为默认 IO 模板。
/// </summary>
public sealed class StackingHardwareProfileProvider
    : ModuleHardwareProfileProviderBase<StackingSignal>
{
    public StackingHardwareProfileProvider(IModulePlcSignalProfile<StackingSignal> signalProfile)
        : base(signalProfile)
    {
    }

    /// <summary>
    /// 叠片样本要求信号排序连续，防止硬件模板顺序与运行时读取约定脱节。
    /// </summary>
    protected override bool ValidateSequentialOrder => true;

    /// <summary>
    /// 叠片开发样本默认使用 S7 协议。
    /// </summary>
    public override ModulePlcDefaults GetDefaultPlcSettings()
        => new(PlcType.S7.ToString(), 3000, 102);

    protected override string CreateTemplateRemark(ModuleSignalDefinition<StackingSignal> signal)
        => $"叠片模块 - {signal.DisplayName}";
}
