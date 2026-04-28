using IIoT.Edge.Application.Abstractions.Modules;
using IIoT.Edge.Application.Modules;
using IIoT.Edge.SharedKernel.Enums;

namespace IIoT.Edge.Module.Homogenization.Config.Hardware;

public sealed class HomogenizationHardwareProfileProvider : IModuleHardwareProfileProvider
{
    public string ModuleId => DependencyInjection.ModuleKey;

    public ModulePlcDefaults GetDefaultPlcSettings()
        => new(PlcType.Mc.ToString(), 3000, 6000);

    public IReadOnlyList<ModuleIoTemplateEntry> GetDefaultIoTemplate()
        => HomogenizationPlcSignalProfile.Signals
            .Select(static signal => new ModuleIoTemplateEntry(
                signal.Label,
                string.Empty,
                signal.AddressCount,
                signal.DataType,
                signal.Direction,
                signal.SortOrder,
                $"匀浆模块 - {signal.DisplayName}",
                signal.Category,
                signal.GroupName,
                signal.DisplayRole))
            .ToArray();

    public string GetProtocolSummary()
        => string.Join(
            Environment.NewLine,
            HomogenizationPlcSignalProfile.Signals.Select(static signal =>
                $"{signal.Label}：分类={signal.Category}，分组={signal.GroupName}，方向={signal.Direction}，类型={signal.DataType}，长度={signal.AddressCount}，排序={signal.SortOrder}"));

    public ModuleHardwareValidationResult ValidatePlcConfiguration(
        string deviceName,
        string? deviceModel,
        IReadOnlyCollection<ModuleIoSnapshot> mappings)
        => ModuleHardwareProfileValidator.Validate(
            deviceName,
            mappings,
            HomogenizationPlcSignalProfile.Signals.Select(ToRequirement).ToArray(),
            requireCategory: true);

    private static ModuleHardwareSignalRequirement ToRequirement(HomogenizationSignalDefinition signal)
        => new(
            signal.Label,
            signal.AddressCount,
            signal.DataType,
            signal.Direction,
            signal.SortOrder);
}
