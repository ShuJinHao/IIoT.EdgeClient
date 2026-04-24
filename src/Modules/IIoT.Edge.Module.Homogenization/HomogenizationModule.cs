using IIoT.Edge.Application.Abstractions.Modules;
using IIoT.Edge.Application.Abstractions.Context;
using IIoT.Edge.Module.Abstractions;
using IIoT.Edge.Module.Homogenization.Config;
using IIoT.Edge.Module.Homogenization.Config.Hardware;
using IIoT.Edge.Module.Homogenization.Integration;
using IIoT.Edge.Module.Homogenization.Payload;
using IIoT.Edge.Module.Homogenization.Presentation;
using IIoT.Edge.Module.Homogenization.Runtime;
using IIoT.Edge.Plugin.Shared.Modules;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace IIoT.Edge.Module.Homogenization;

public sealed class HomogenizationModule : IEdgeProcessModule
{
    public string ModuleId => HomogenizationModuleConstants.ModuleId;

    public string ProcessType => HomogenizationModuleConstants.ProcessType;

    public string DisplayName => "匀浆";

    public void Configure(IEdgeProcessModuleBuilder builder)
    {
        builder.Services.AddSingleton(sp =>
            HomogenizationModuleConfiguration.Load(sp.GetService<IConfiguration>()));
        builder.Services.AddSingleton(sp => sp.GetRequiredService<HomogenizationModuleConfiguration>().Module);
        builder.Services.AddSingleton(sp => sp.GetRequiredService<HomogenizationModuleConfiguration>().Mes);
        builder.Services.AddSingleton(sp => sp.GetRequiredService<HomogenizationModuleConfiguration>().Codes);

        builder.Services.AddSingleton<IProcessCloudUploader, HomogenizationCloudUploader>();
        builder.Services.AddSingleton<IProcessMesUploader, HomogenizationMesUploader>();
        builder.Services.AddSingleton<IHomogenizationMesApiService, HomogenizationMesApiService>();
        builder.Services.AddSingleton<IProductionContextFactory, HomogenizationContextFactory>();
        builder.Services.AddSingleton<IModuleHardwareProfileProvider, HomogenizationHardwareProfileProvider>();
        builder.Services.AddSingleton<HomogenizationCellDataValidator>();
        builder.Services.AddSingleton<IDevelopmentSampleContributor, HomogenizationDevelopmentSampleContributor>();
        builder.Services.AddSingleton<HomogenizationDataViewModel>();

        builder.RegisterCellData(typeof(HomogenizationCellData));
        builder.RegisterRuntimeFactory(new HomogenizationStationRuntimeFactory());
        builder.RegisterCloudUploader(PluginCloudUploadMode.Single);
        builder.RegisterMesUploader(PluginMesUploadMode.Single);
        builder.RegisterHomogenizationViews();
    }
}
