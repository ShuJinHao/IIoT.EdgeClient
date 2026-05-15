namespace IIoT.Edge.Host.Bootstrap.Core.Plugins;

public sealed record EdgeProcessModuleCatalogOptions(
    IReadOnlyCollection<string> SearchDirectories,
    string EntryAssemblySuffix = ".dll");
