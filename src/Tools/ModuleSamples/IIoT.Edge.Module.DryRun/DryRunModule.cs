using IIoT.Edge.Application.Abstractions.Modules;
using IIoT.Edge.Module.Abstractions;
using IIoT.Edge.Module.DryRun.Constants;
using IIoT.Edge.Module.DryRun.Integration;
using IIoT.Edge.Module.DryRun.Payload;
using IIoT.Edge.Module.DryRun.Presentation;
using IIoT.Edge.Module.DryRun.Runtime;
using IIoT.Edge.Plugin.Shared.Modules;
using Microsoft.Extensions.DependencyInjection;

namespace IIoT.Edge.Module.DryRun;

public sealed class DryRunModule : IEdgeProcessModule
{
    public const string ModuleKey = DryRunModuleConstants.ModuleId;

    public string ModuleId => ModuleKey;

    public string ProcessType => DryRunModuleConstants.ProcessType;

    public string DisplayName => "DryRun";

    public void Configure(IEdgeProcessModuleBuilder builder)
    {
        builder.Services.AddSingleton<IProcessCloudUploader, DryRunCloudUploader>();
        builder.Services.AddSingleton<Presentation.ViewModels.DryRunDashboardViewModel>();

        builder.RegisterCellData(typeof(DryRunCellData));
        builder.RegisterRuntimeFactory(new DryRunStationRuntimeFactory());
        builder.RegisterCloudUploader(PluginCloudUploadMode.Single);
        builder.RegisterDryRunViews();
    }
}
