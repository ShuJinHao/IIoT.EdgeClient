using IIoT.Edge.Application.Abstractions.Modules;

namespace IIoT.Edge.Application.Modules.Hardware;

public interface IModulePlcSignalDefinition
{
    string Label { get; }

    string Direction { get; }

    string DefaultAddress { get; }

    int AddressCount { get; }

    string DataType { get; }

    int SortOrder { get; }

    string DisplayName { get; }

    string Category { get; }

    string GroupName { get; }

    string DisplayRole { get; }
}

public abstract class ModuleHardwareProfileProviderBase<TSignal> : IModuleHardwareProfileProvider
    where TSignal : IModulePlcSignalDefinition
{
    public abstract string ModuleId { get; }

    protected abstract IReadOnlyList<TSignal> Signals { get; }

    protected virtual bool RequireCategory => false;

    protected virtual bool ValidateSequentialOrder => false;

    public abstract ModulePlcDefaults GetDefaultPlcSettings();

    public IReadOnlyList<ModuleIoTemplateEntry> GetDefaultIoTemplate()
        => Signals
            .Select(CreateTemplateEntry)
            .ToArray();

    public ModuleHardwareValidationResult ValidatePlcConfiguration(
        string deviceName,
        string? deviceModel,
        IReadOnlyCollection<ModuleIoSnapshot> mappings)
        => ModuleHardwareProfileValidator.Validate(
            deviceName,
            mappings,
            Signals.Select(static signal => new ModuleHardwareSignalRequirement(
                    signal.Label,
                    signal.AddressCount,
                    signal.DataType,
                    signal.Direction,
                    signal.SortOrder))
                .ToArray(),
            RequireCategory,
            ValidateSequentialOrder);

    protected abstract string CreateTemplateRemark(TSignal signal);

    private ModuleIoTemplateEntry CreateTemplateEntry(TSignal signal)
        => new(
            signal.Label,
            signal.DefaultAddress,
            signal.AddressCount,
            signal.DataType,
            signal.Direction,
            signal.SortOrder,
            CreateTemplateRemark(signal),
            signal.Category,
            signal.GroupName,
            signal.DisplayRole);
}
