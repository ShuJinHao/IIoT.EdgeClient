using IIoT.Edge.Application.Abstractions.Modules;
using IIoT.Edge.Module.Abstractions;
using IIoT.Edge.Module.ScanCaptureStarter.Constants;
using IIoT.Edge.Module.ScanCaptureStarter.Integration;
using IIoT.Edge.Module.ScanCaptureStarter.Payload;
using IIoT.Edge.Module.ScanCaptureStarter.Presentation;
using IIoT.Edge.Module.ScanCaptureStarter.Runtime;
using IIoT.Edge.Module.ScanCaptureStarter.Samples;
using IIoT.Edge.Plugin.Shared.Modules;
using Microsoft.Extensions.DependencyInjection;

namespace IIoT.Edge.Module.ScanCaptureStarter;

public sealed class ScanCaptureStarterModule : IEdgeProcessModule
{
    public string ModuleId => StarterModuleConstants.ModuleId;

    public string ProcessType => StarterModuleConstants.ProcessType;

    public string DisplayName => "ScanCaptureStarter";

    public void Configure(IEdgeProcessModuleBuilder builder)
    {
        builder.Services.AddSingleton<IProcessCloudUploader, StarterCloudUploader>();
        builder.Services.AddSingleton<IModuleHardwareProfileProvider, StarterHardwareProfileProvider>();
        builder.Services.AddSingleton<IDevelopmentSampleContributor, ScanCaptureStarterDevelopmentSampleContributor>();
        builder.Services.AddSingleton<Presentation.ViewModels.StarterSkeletonViewModel>();
        builder.Services.AddSingleton<Presentation.StarterParamViewModel>();
        builder.Services.AddSingleton<Presentation.StarterHardwareConfigViewModel>();

        builder.RegisterCellData(typeof(StarterCellData));
        builder.RegisterRuntimeFactory(new StarterStationRuntimeFactory());
        builder.RegisterCloudUploader(PluginCloudUploadMode.Single);
        builder.RegisterScanCaptureStarterViews();
    }
}
