using IIoT.Edge.Application.Abstractions.Modules;
using IIoT.Edge.Module.Injection.Integration;
using IIoT.Edge.Module.Injection.Payload;
using IIoT.Edge.Module.Injection.Presentation;
using IIoT.Edge.Module.Injection.Runtime;
using IIoT.Edge.Module.Abstractions;
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
        services.AddSingleton<InjectionDataViewModel>();
        services.AddSingleton<InjectionCapacityViewModel>();
        services.AddSingleton<InjectionMonitorViewModel>();
        services.AddSingleton<InjectionIoViewModel>();
        services.AddSingleton<InjectionRecipeViewModel>();
        services.AddSingleton<InjectionParamViewModel>();
        services.AddSingleton<InjectionHardwareConfigViewModel>();
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
