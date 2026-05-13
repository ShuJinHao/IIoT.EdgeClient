using IIoT.Edge.Application.Abstractions.Modules;
using IIoT.Edge.Application.Abstractions.Config;
using Microsoft.Extensions.Configuration;

namespace IIoT.Edge.Host.Bootstrap.Avalonia;

public sealed record AvaloniaHostBootstrapOptions(
    IConfiguration Configuration,
    EdgeRuntimePaths RuntimePaths,
    string EnvironmentName,
    IReadOnlyCollection<string> ModuleIds,
    IReadOnlyCollection<IEdgeProcessModule>? Modules = null);
