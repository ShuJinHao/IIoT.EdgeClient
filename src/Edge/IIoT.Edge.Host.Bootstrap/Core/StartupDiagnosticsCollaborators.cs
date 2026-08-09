using IIoT.Edge.Module.Contracts.Modules;
using IIoT.Edge.Module.Contracts.Diagnostics;
using IIoT.Edge.Application.Common.Plugins;
using IIoT.Edge.Host.Bootstrap.Modules;

namespace IIoT.Edge.Shell.Core;

public interface IStartupDiagnosticValidator
{
    void Validate(StartupValidationContext context, List<StartupDiagnosticIssue> issues);
}

public interface IStartupAsyncDiagnosticValidator
{
    Task ValidateAsync(StartupValidationContext context, List<StartupDiagnosticIssue> issues, CancellationToken cancellationToken);
}

public interface IStartupConfigurationProfileBuilder
{
    ConfigurationProfileSnapshot Build();
}

public interface IStartupModuleRegistrationSnapshotBuilder
{
    IReadOnlyList<ModuleRegistrationSnapshot> Build(StartupValidationContext context);
}

public sealed class StartupValidationContext
{
    public required ConfigurationProfileSnapshot ConfigurationProfile { get; init; }

    public required bool SystemCloudEnabled { get; init; }

    public required IReadOnlyCollection<DevicePluginPlcSnapshot> PlcDevices { get; init; }

    public required IReadOnlyDictionary<string, IEdgeProcessModule> ModulesById { get; init; }

    public required IReadOnlyDictionary<string, ModulePluginDescriptor> DiscoveredModulesById { get; init; }

    public required IReadOnlyDictionary<string, IModuleHardwareProfileProvider> HardwareProfilesByModuleId { get; init; }

    public IReadOnlyList<DeviceModuleBindingSnapshot> DeviceBindings { get; set; } = [];
}

public static class StartupDiagnosticIssueFactory
{
    public static StartupDiagnosticIssue Create(
        string code,
        string message,
        string? moduleId = null,
        string? deviceName = null)
        => new(code, message, moduleId, deviceName);
}
