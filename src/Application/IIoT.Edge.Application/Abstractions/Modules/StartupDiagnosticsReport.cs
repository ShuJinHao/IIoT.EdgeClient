namespace IIoT.Edge.Application.Abstractions.Modules;

public sealed record StartupDiagnosticIssue(
    string Code,
    string Message,
    string? ModuleId = null,
    string? DeviceName = null);

public sealed record ModuleRegistrationSnapshot(
    string ModuleId,
    string ProcessType,
    string AssemblyName,
    bool IsEnabled,
    bool HasCellDataRegistration,
    bool HasRuntimeFactory,
    bool HasCloudUploader,
    bool HasMesUploader,
    bool HasHardwareProfile);

public sealed record DeviceModuleBindingSnapshot(
    string DeviceName,
    string? ModuleId,
    bool ModuleExists,
    bool ModuleEnabled,
    bool HasIoMappings);

public sealed record StartupDiagnosticsReport(
    DateTime GeneratedAt,
    IReadOnlyList<string> DiscoveredModules,
    IReadOnlyList<string> EnabledModules,
    IReadOnlyList<ModuleRegistrationSnapshot> ModuleRegistrations,
    IReadOnlyList<DeviceModuleBindingSnapshot> DeviceBindings,
    IReadOnlyList<StartupDiagnosticIssue> Issues)
{
    public static StartupDiagnosticsReport Empty()
        => new(
            DateTime.MinValue,
            [],
            [],
            [],
            [],
            []);
}
