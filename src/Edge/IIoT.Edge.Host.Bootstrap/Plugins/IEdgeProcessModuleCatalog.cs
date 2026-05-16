using IIoT.Edge.Application.Abstractions.Modules;

namespace IIoT.Edge.Host.Bootstrap.Plugins;

public interface IEdgeProcessModuleCatalog
{
    IReadOnlyList<IEdgeProcessModule> LoadModules();
}
