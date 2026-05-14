using IIoT.Edge.Application.Abstractions.Modules;

namespace IIoT.Edge.Host.Bootstrap.Modules;

public sealed record ModuleCatalogIssue(
    string Code,
    string Message,
    string? ModuleId = null,
    string? ManifestPath = null,
    string? EntryAssemblyPath = null,
    string? PluginDirectoryName = null);

public sealed record ModuleCatalogDiscoveryResult(
    IReadOnlyList<ModulePluginDescriptor> Modules,
    IReadOnlyList<ModuleCatalogIssue> Issues);

public sealed record ModuleCatalogActivationResult(
    IReadOnlyList<IEdgeProcessModule> Modules,
    IReadOnlyList<string> EnabledModuleIds,
    IReadOnlyList<ModuleCatalogIssue> Issues);
