using IIoT.Edge.Application.Abstractions.Modules;
using IIoT.Edge.Module.Abstractions;
using IIoT.Edge.Infrastructure.Integration.PassStation;
using IIoT.Edge.Presentation.Navigation;
using IIoT.Edge.Runtime.Stations.Stacking;
using IIoT.Edge.SharedKernel.DataPipeline.CellData;
using IIoT.Edge.SharedKernel.Modules.Stacking;
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
