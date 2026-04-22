using IIoT.Edge.Application.Abstractions.Modules;
using IIoT.Edge.Module.Abstractions;
using IIoT.Edge.Module.Homogenization.Constants;
using IIoT.Edge.Module.Homogenization.Diagnostics;
using IIoT.Edge.Module.Homogenization.Integration;
using IIoT.Edge.Module.Homogenization.Payload;
using IIoT.Edge.Module.Homogenization.Presentation;
using IIoT.Edge.Module.Homogenization.Runtime;
using IIoT.Edge.Module.Homogenization.Samples;
using IIoT.Edge.Plugin.Shared.Modules;
using Microsoft.Extensions.DependencyInjection;

namespace IIoT.Edge.Module.Homogenization;

public sealed class HomogenizationModule : IEdgeProcessModule
{
    public string ModuleId => HomogenizationModuleConstants.ModuleId;

    public string ProcessType => HomogenizationModuleConstants.ProcessType;

    public string DisplayName => "匀浆";

    public void Configure(IEdgeProcessModuleBuilder builder)
    {
        builder.Services.AddSingleton<IProcessCloudUploader, HomogenizationCloudUploader>();
        builder.Services.AddSingleton<IDevelopmentSampleContributor, HomogenizationDevelopmentSampleContributor>();
        builder.Services.AddSingleton<HomogenizationDataViewModel>();
        builder.Services.AddSingleton<HomogenizationCapacityViewModel>();
        builder.Services.AddSingleton<HomogenizationMonitorViewModel>();
        builder.Services.AddSingleton<HomogenizationIoViewModel>();
        builder.Services.AddSingleton<HomogenizationRecipeViewModel>();
        builder.Services.AddSingleton<HomogenizationParamViewModel>();
        builder.Services.AddSingleton<HomogenizationHardwareConfigViewModel>();

        builder.RegisterCellData(typeof(HomogenizationCellData));
        builder.RegisterRuntimeFactory(new HomogenizationStationRuntimeFactory());
        builder.RegisterCloudUploader(PluginCloudUploadMode.Single);
        builder.RegisterHomogenizationViews();
    }
}
