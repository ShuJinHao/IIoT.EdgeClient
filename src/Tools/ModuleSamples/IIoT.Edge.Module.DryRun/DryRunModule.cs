using IIoT.Edge.Application.Abstractions.Modules;
using IIoT.Edge.Module.Abstractions;
using IIoT.Edge.Module.DryRun.Constants;
using IIoT.Edge.Module.DryRun.Integration;
using IIoT.Edge.Module.DryRun.Payload;
using IIoT.Edge.Module.DryRun.Presentation;
using IIoT.Edge.Module.DryRun.Runtime;
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
        services.AddSingleton<Presentation.ViewModels.DryRunDashboardViewModel>();
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
