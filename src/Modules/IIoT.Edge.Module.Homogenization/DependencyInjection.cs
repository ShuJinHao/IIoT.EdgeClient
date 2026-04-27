using IIoT.Edge.Application.Abstractions.Context;
using IIoT.Edge.Application.Abstractions.Modules;
using IIoT.Edge.Module.Homogenization.Config;
using IIoT.Edge.Module.Homogenization.Config.Hardware;
using IIoT.Edge.Module.Homogenization.Integration;
using IIoT.Edge.Module.Homogenization.Payload;
using IIoT.Edge.Module.Homogenization.Presentation;
using IIoT.Edge.Module.Homogenization.Resources;
using IIoT.Edge.Module.Homogenization.Runtime;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace IIoT.Edge.Module.Homogenization;

/// <summary>
/// 匀浆工序模块入口，负责按标准模块契约注册运行时、上传器、硬件模板和导航页面。
/// </summary>
public sealed class DependencyInjection : IEdgeProcessModule
{
    public const string ModuleKey = "Homogenization";

    public string ModuleId => ModuleKey;

    public string ProcessType => ModuleKey;

    public string DisplayName => HomogenizationText.Get("Homogenization_DisplayName", "匀浆");

    /// <summary>
    /// 使用宿主统一的 IConfiguration/Options 管线绑定匀浆配置，不在模块内自行创建配置源。
    /// </summary>
    public void Configure(IEdgeProcessModuleBuilder builder)
    {
        var section = builder.Configuration.GetSection($"Modules:{ModuleKey}");
        builder.Services.AddOptions<HomogenizationModuleOptions>()
            .Bind(section.GetSection("Module"));
        builder.Services.AddOptions<HomogenizationMesOptions>()
            .Bind(section.GetSection("Mes"));
        builder.Services.AddOptions<HomogenizationCodeOptions>()
            .Bind(section.GetSection("Codes"));
        builder.Services.AddSingleton<IValidateOptions<HomogenizationModuleOptions>, HomogenizationModuleOptionsValidator>();
        builder.Services.AddSingleton<IValidateOptions<HomogenizationMesOptions>, HomogenizationMesOptionsValidator>();
        builder.Services.AddSingleton<IValidateOptions<HomogenizationCodeOptions>, HomogenizationCodeOptionsValidator>();

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

