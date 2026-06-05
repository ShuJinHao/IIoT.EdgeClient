using IIoT.Edge.Application.Abstractions.Context;
using IIoT.Edge.Application.Abstractions.Mes;
using IIoT.Edge.Application.Abstractions.Modules;
using IIoT.Edge.Application.Features.Production.Planning;
using IIoT.Edge.Module.Homogenization.Config;
using IIoT.Edge.Module.Homogenization.Config.Io;
using IIoT.Edge.Module.Homogenization.Config.Parameters;
using IIoT.Edge.Module.Homogenization.Mes;
using IIoT.Edge.Module.Homogenization.Payload;
using IIoT.Edge.Module.Homogenization.Presentation;
using IIoT.Edge.Module.Homogenization.Resources;
using IIoT.Edge.Module.Homogenization.Production;
using IIoT.Edge.Module.Homogenization.Samples;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace IIoT.Edge.Module.Homogenization;

/// <summary>
/// 匀浆工序模块入口，负责按标准模块契约注册运行时、上传器、硬件模板和导航页面。
/// </summary>
public sealed class DependencyInjection : EdgeProcessModuleBase<HomogenizationCellData>
{
    public const string ModuleKey = "Homogenization";

    public override string ModuleId => ModuleKey;

    public override string ProcessType => ModuleKey;

    public override string DisplayName => HomogenizationText.Get(
        "Homogenization_DisplayName",
        "匀浆");

    protected override ProcessUploadMode? MesUploadMode
        => IIoT.Edge.Application.Abstractions.Modules.ProcessUploadMode.Single;

    protected override IStationRuntimeFactory CreateRuntimeFactory()
        => new HomogenizationStationRuntimeFactory();

    /// <summary>
    /// 使用宿主统一的 IConfiguration/Options 管线绑定匀浆配置，不在模块内自行创建配置源。
    /// </summary>
    protected override void ConfigureModuleServices(IEdgeProcessModuleBuilder builder)
    {
        builder.RegisterParameters<HomogenizationParams.Mes, HomogenizationParams.Cloud, HomogenizationParams.Business>();

        var section = builder.Configuration.GetSection($"Modules:{ModuleKey}");
        builder.Services.AddOptions<HomogenizationModuleOptions>()
            .Bind(section.GetSection("Module"));
        builder.Services.AddOptions<HomogenizationCodeOptions>()
            .Bind(section.GetSection("Codes"));

        builder.Services.AddSingleton<HomogenizationMesPayloadBuilder>();
        builder.Services.AddSingleton<HomogenizationMesChannel>();
        builder.Services.AddSingleton<IHomogenizationMesScenarioChannel>(sp =>
            sp.GetRequiredService<HomogenizationMesChannel>());
        builder.Services.AddSingleton<IProcessMesUploader>(sp =>
            sp.GetRequiredService<HomogenizationMesChannel>());
        builder.Services.AddSingleton<IProductionPlanSelectionService, HomogenizationProductionPlanService>();
        builder.Services.AddSingleton<IHomogenizationProductionGate, HomogenizationProductionGate>();
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
        => builder.RegisterHomogenizationViews();
}
