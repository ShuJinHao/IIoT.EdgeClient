using IIoT.Edge.Application.Abstractions.Modules;
using IIoT.Edge.Application.Modules;
using IIoT.Edge.Module.Stacking.Constants;
using IIoT.Edge.SharedKernel.Enums;

namespace IIoT.Edge.Module.Stacking.Runtime;

public sealed class StackingHardwareProfileProvider : IModuleHardwareProfileProvider
{
    public string ModuleId => StackingModuleConstants.ModuleId;

    public ModulePlcDefaults GetDefaultPlcSettings()
        => new(PlcType.S7.ToString(), 3000, 102);

    public IReadOnlyList<ModuleIoTemplateEntry> GetDefaultIoTemplate()
        => StackingPlcSignalProfile.Signals
            .Select(static x => new ModuleIoTemplateEntry(
                x.Label,
                x.DefaultAddress,
                x.AddressCount,
                x.DataType,
                x.Direction,
                x.SortOrder,
                $"叠片模块 - {x.DisplayName}"))
            .ToArray();

    public string GetProtocolSummary()
        => string.Join(
            Environment.NewLine,
            StackingPlcSignalProfile.Signals.Select(static x =>
                $"{x.Label}：默认地址={x.DefaultAddress}，方向={x.Direction}，类型={x.DataType}，长度={x.AddressCount}，排序={x.SortOrder}"));

    public ModuleHardwareValidationResult ValidatePlcConfiguration(
        string deviceName,
        string? deviceModel,
        IReadOnlyCollection<ModuleIoSnapshot> mappings)
        => ModuleHardwareProfileValidator.Validate(
            deviceName,
            mappings,
            StackingPlcSignalProfile.Signals.Select(ToRequirement).ToArray(),
            validateSequentialOrder: true);

    private static ModuleHardwareSignalRequirement ToRequirement(StackingSignalDefinition signal)
        => new(
            signal.Label,
            signal.AddressCount,
            signal.DataType,
            signal.Direction,
            signal.SortOrder);
}
