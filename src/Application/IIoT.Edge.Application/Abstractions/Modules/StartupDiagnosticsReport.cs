namespace IIoT.Edge.Application.Abstractions.Modules;

public sealed record StartupDiagnosticIssue(
    string Code,
    string Message,
    string? ModuleId = null,
    string? DeviceName = null);

public enum PluginLifecycleState
{
    Discovered = 0,
    DisabledByConfig = 1,
    ManifestInvalid = 2,
    DependencyMissing = 3,
    HostVersionIncompatible = 4,
    LoadFailed = 5,
    Activated = 6
}

public sealed record ConfigurationProfileSnapshot(
    string EnvironmentName,
    string? MachineProfile,
    string? MachineProfileFileName,
    bool IsMachineProfileLoaded);

public sealed record PluginLifecycleSnapshot(
    string ModuleId,
    string DisplayName,
    string? ProcessType,
    string Version,
    PluginLifecycleState State,
    string Message);

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
    ConfigurationProfileSnapshot ConfigurationProfile,
    IReadOnlyList<string> DiscoveredModules,
    IReadOnlyList<string> EnabledModules,
    IReadOnlyList<string> ActivatedModules,
    IReadOnlyList<PluginLifecycleSnapshot> PluginStates,
    IReadOnlyList<ModuleRegistrationSnapshot> ModuleRegistrations,
    IReadOnlyList<DeviceModuleBindingSnapshot> DeviceBindings,
    IReadOnlyList<StartupDiagnosticIssue> Issues)
{
    public static StartupDiagnosticsReport Empty()
        => new(
            DateTime.MinValue,
            new ConfigurationProfileSnapshot("Production", null, null, false),
            [],
            [],
            [],
            [],
            [],
            [],
            []);
}
