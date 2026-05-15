namespace IIoT.Edge.Host.Bootstrap.Core.Modules;

public sealed record ModulePluginDescriptor(
    string ModuleId,
    string ProcessType,
    string DisplayName,
    string Version,
    string HostApiVersion,
    string MinHostVersion,
    string MaxHostVersion,
    IReadOnlyList<string> Dependencies,
    string AssemblyName,
    string EntryTypeName,
    string PluginDirectory,
    string ManifestPath,
    string EntryAssemblyPath);
