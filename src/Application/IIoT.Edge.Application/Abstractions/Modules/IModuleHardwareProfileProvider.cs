using IIoT.Edge.Application.Modules.Hardware;

namespace IIoT.Edge.Application.Abstractions.Modules;

public interface IModuleHardwareProfileProvider
{
    string ModuleId { get; }

    ModulePlcDefaults GetDefaultPlcSettings();

    IReadOnlyList<ModuleIoTemplateEntry> GetDefaultIoTemplate();

    ModuleHardwareValidationResult ValidatePlcConfiguration(
        string deviceName,
        string? deviceModel,
        IReadOnlyCollection<ModuleIoSnapshot> mappings);
}
