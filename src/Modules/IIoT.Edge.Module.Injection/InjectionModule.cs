using IIoT.Edge.Application.Abstractions.Modules;
using IIoT.Edge.Module.Abstractions;
using IIoT.Edge.Infrastructure.Integration.PassStation;
using IIoT.Edge.Presentation.Navigation;
using IIoT.Edge.Runtime.Stations.Injection;
using IIoT.Edge.SharedKernel.DataPipeline.CellData;
using IIoT.Edge.UI.Shared.Modularity;
using Microsoft.Extensions.DependencyInjection;

namespace IIoT.Edge.Module.Injection;

public sealed class InjectionModule : IEdgeStationModule
{
    public const string ModuleKey = "Injection";

    public string ModuleId => ModuleKey;

    public string ProcessType => ModuleKey;

    public void RegisterServices(IServiceCollection services)
    {
        services.AddSingleton<IProcessCloudUploader, InjectionCloudUploader>();
    }

    public void RegisterViews(IViewRegistry viewRegistry)
    {
        viewRegistry.RegisterInjectionViews();
    }

    public void RegisterCellData(ICellDataRegistry registry)
    {
        registry.Register<InjectionCellData>(ProcessType);
    }

    public void RegisterRuntime(IStationRuntimeRegistry registry)
    {
        registry.Register(new InjectionStationRuntimeFactory());
    }

    public void RegisterIntegrations(IProcessIntegrationRegistry registry)
    {
        registry.RegisterCloudUploader(ProcessType, ProcessUploadMode.Batch);
    }
}
