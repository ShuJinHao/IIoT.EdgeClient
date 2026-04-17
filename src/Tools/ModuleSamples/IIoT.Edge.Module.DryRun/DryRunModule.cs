using IIoT.Edge.Application.Abstractions.Modules;
using IIoT.Edge.Infrastructure.Integration.PassStation;
using IIoT.Edge.Module.Abstractions;
using IIoT.Edge.Presentation.Navigation;
using IIoT.Edge.Runtime.Stations.DryRun;
using IIoT.Edge.SharedKernel.DataPipeline.CellData;
using IIoT.Edge.SharedKernel.Modules.DryRun;
using IIoT.Edge.UI.Shared.Modularity;
using Microsoft.Extensions.DependencyInjection;

namespace IIoT.Edge.Module.DryRun;

public sealed class DryRunModule : IEdgeStationModule
{
    public const string ModuleKey = DryRunModuleConstants.ModuleId;

    public string ModuleId => ModuleKey;

    public string ProcessType => DryRunModuleConstants.ProcessType;

    public void RegisterServices(IServiceCollection services)
    {
        services.AddSingleton<IProcessCloudUploader, DryRunCloudUploader>();
    }

    public void RegisterViews(IViewRegistry viewRegistry)
    {
        viewRegistry.RegisterDryRunViews();
    }

    public void RegisterCellData(ICellDataRegistry registry)
    {
        registry.Register<DryRunCellData>(ProcessType);
    }

    public void RegisterRuntime(IStationRuntimeRegistry registry)
    {
        registry.Register(new DryRunStationRuntimeFactory());
    }

    public void RegisterIntegrations(IProcessIntegrationRegistry registry)
    {
        registry.RegisterCloudUploader(ProcessType, ProcessUploadMode.Single);
    }
}
