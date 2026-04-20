using IIoT.Edge.Application.Abstractions.Modules;
using IIoT.Edge.Module.Abstractions;
using IIoT.Edge.Module.Stacking.Constants;
using IIoT.Edge.Module.Stacking.Integration;
using IIoT.Edge.Module.Stacking.Payload;
using IIoT.Edge.Module.Stacking.Presentation;
using IIoT.Edge.Module.Stacking.Runtime;
using IIoT.Edge.Module.Stacking.Samples;
using IIoT.Edge.UI.Shared.Modularity;
using Microsoft.Extensions.DependencyInjection;

namespace IIoT.Edge.Module.Stacking;

public sealed class StackingModule : IEdgeStationModule
{
    public const string ModuleKey = StackingModuleConstants.ModuleId;

    public string ModuleId => ModuleKey;

    public string ProcessType => StackingModuleConstants.ProcessType;

    public void RegisterServices(IServiceCollection services)
    {
        services.AddSingleton<IProcessCloudUploader, StackingCloudUploader>();
        services.AddSingleton<IModuleHardwareProfileProvider, StackingHardwareProfileProvider>();
        services.AddSingleton<IDevelopmentSampleContributor, StackingDevelopmentSampleContributor>();
        services.AddSingleton<Presentation.ViewModels.StackingSkeletonViewModel>();
    }

    public void RegisterViews(IViewRegistry viewRegistry)
    {
        viewRegistry.RegisterStackingViews();
    }

    public void RegisterCellData(ICellDataRegistry registry)
    {
        registry.Register<StackingCellData>(ProcessType);
    }

    public void RegisterRuntime(IStationRuntimeRegistry registry)
    {
        registry.Register(new StackingStationRuntimeFactory());
    }

    public void RegisterIntegrations(IProcessIntegrationRegistry registry)
    {
        registry.RegisterCloudUploader(ProcessType, ProcessUploadMode.Single);
    }
}
