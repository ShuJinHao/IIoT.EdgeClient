using IIoT.Edge.Application.Abstractions.Modules;
using IIoT.Edge.UI.Shared.Modularity;
using Microsoft.Extensions.DependencyInjection;

namespace IIoT.Edge.Module.Abstractions;

public interface IEdgeStationModule
{
    string ModuleId { get; }

    string ProcessType { get; }

    void RegisterServices(IServiceCollection services);

    void RegisterViews(IViewRegistry viewRegistry);

    void RegisterCellData(ICellDataRegistry registry);

    void RegisterRuntime(IStationRuntimeRegistry registry);

    void RegisterIntegrations(IProcessIntegrationRegistry registry);
}
