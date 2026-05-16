using IIoT.Edge.Application.Abstractions.Context;
using IIoT.Edge.Application.Abstractions.Modules;
using IIoT.Edge.Application.Modules.Mes;
using IIoT.Edge.Module.Homogenization.Config;
using IIoT.Edge.Module.Homogenization.Config.Hardware;
using IIoT.Edge.Module.Homogenization.Config.Parameters;
using IIoT.Edge.Module.Homogenization.Integration;
using IIoT.Edge.Module.Homogenization.Integration.Cloud;
using IIoT.Edge.Module.Homogenization.Payload;
using IIoT.Edge.Module.Homogenization.Presentation;
using IIoT.Edge.Module.Homogenization.Resources;
using IIoT.Edge.Module.Homogenization.Runtime;
using IIoT.Edge.Module.Homogenization.Samples;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using HomogenizationCloudUploadChannel = IIoT.Edge.Application.Modules.Cloud.ICloudUploadChannel<
    IIoT.Edge.Module.Homogenization.Payload.HomogenizationCellData,
    object>;
using HomogenizationMesScenarioChannel = IIoT.Edge.Application.Modules.Mes.IMesScenarioChannel<
    IIoT.Edge.Module.Homogenization.Payload.HomogenizationCellData,
    string,
    IIoT.Edge.Module.Homogenization.Payload.HomogenizationRealtimeSnapshot,
    IIoT.Edge.Module.Homogenization.Payload.HomogenizationRecipeSnapshot,
    IIoT.Edge.Module.Homogenization.Payload.HomogenizationEquipmentStatusSnapshot>;

namespace IIoT.Edge.Module.Homogenization;

/// <summary>
/// 匀浆工序 Avalonia 模块入口，按标准模块契约注册运行时、上传器、硬件模板和页面。
/// </summary>
public sealed class DependencyInjection : EdgeProcessModuleBase<HomogenizationCellData>
{
    public const string ModuleKey = "Homogenization";

    public override string ModuleId => ModuleKey;

    public override string ProcessType => ModuleKey;

    public override string DisplayName => HomogenizationText.Get(
        "Homogenization_DisplayName",
        "匀浆");

    protected override ProcessUploadMode CloudUploadMode => ProcessUploadMode.Batch;

    protected override MesUploadMode? MesUploadMode
        => IIoT.Edge.Application.Abstractions.Modules.MesUploadMode.Single;

    protected override IStationRuntimeFactory CreateRuntimeFactory()
        => new HomogenizationStationRuntimeFactory();

    protected override void ConfigureModuleServices(IEdgeProcessModuleBuilder builder)
    {
        builder.RegisterParameters<HomogenizationParams.Mes, HomogenizationParams.Cloud, HomogenizationParams.Business>();

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

        builder.Services.AddSingleton<HomogenizationCloudUploader>();
        builder.Services.AddSingleton<HomogenizationCloudUploadChannel>(sp =>
            sp.GetRequiredService<HomogenizationCloudUploader>());
        builder.Services.AddSingleton<IProcessCloudUploader>(sp =>
            sp.GetRequiredService<HomogenizationCloudUploader>());
        builder.Services.AddSingleton<HomogenizationMesChannel>();
        builder.Services.AddSingleton<HomogenizationMesScenarioChannel>(sp =>
            sp.GetRequiredService<HomogenizationMesChannel>());
        builder.Services.AddSingleton<IProcessMesUploader>(sp =>
            sp.GetRequiredService<HomogenizationMesChannel>());
        builder.Services.AddSingleton<IProductionContextFactory, HomogenizationContextFactory>();
        builder.RegisterStandardPlcSignalProfiles<
            HomogenizationPlcSignals.Interaction,
            HomogenizationPlcSignals.SingleRead,
            HomogenizationPlcSignals.ContinuousRead,
            HomogenizationPlcSignals.SingleWrite,
            HomogenizationPlcSignals.ContinuousWrite>();
        builder.RegisterHardwareProfile<HomogenizationHardwareProfileProvider>();
        builder.Services.AddSingleton<HomogenizationCellDataValidator>();
        builder.RegisterDevelopmentSample<HomogenizationDevelopmentSampleContributor>();
        builder.Services.AddSingleton<HomogenizationDataViewModel>();
    }

    protected override void RegisterModuleViews(IEdgeProcessModuleBuilder builder)
        => builder.RegisterHomogenizationAvaloniaViews();
}
