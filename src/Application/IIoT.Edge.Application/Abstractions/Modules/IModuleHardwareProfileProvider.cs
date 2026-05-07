using IIoT.Edge.Application.Modules.Hardware;

namespace IIoT.Edge.Application.Abstractions.Modules;

public interface IModuleHardwareProfileProvider
{
    string ModuleId { get; }

    ModulePlcDefaults GetDefaultPlcSettings();

    PlcIoRuntimePolicy GetIoRuntimePolicy();

    IReadOnlyList<ModuleIoTemplateEntry> GetDefaultIoTemplate();

    IReadOnlyList<ModuleIoTemplateEntry> GetIoMappingCandidates();

    ModuleHardwareValidationResult ValidatePlcConfiguration(
        string deviceName,
        string? deviceModel,
        IReadOnlyCollection<ModuleIoSnapshot> mappings);
}
