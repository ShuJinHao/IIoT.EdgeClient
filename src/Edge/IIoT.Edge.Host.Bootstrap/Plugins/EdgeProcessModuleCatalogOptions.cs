namespace IIoT.Edge.Host.Bootstrap.Plugins;

public sealed record EdgeProcessModuleCatalogOptions(
    IReadOnlyCollection<string> SearchDirectories,
    string EntryAssemblySuffix = ".dll");
