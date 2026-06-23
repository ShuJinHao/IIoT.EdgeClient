using IIoT.Edge.Application.Abstractions.Context;
using IIoT.Edge.Application.Abstractions.Mes;
using IIoT.Edge.Application.Abstractions.Modules;
using IIoT.Edge.Module.DieCutting.Config;
using IIoT.Edge.Module.DieCutting.Config.Io;
using IIoT.Edge.Module.DieCutting.Config.Parameters;
using IIoT.Edge.Module.DieCutting.Mes;
using IIoT.Edge.Module.DieCutting.Payload;
using IIoT.Edge.Module.DieCutting.Presentation;
using IIoT.Edge.Module.DieCutting.Production;
using IIoT.Edge.Module.DieCutting.Samples;
using Microsoft.Extensions.DependencyInjection;

namespace IIoT.Edge.Module.DieCutting;

/// <summary>
/// 模切只读采集插件入口，注册只读 PLC 点位、采样上传任务、MES 通道和开发样本。
/// </summary>
public sealed class DependencyInjection : EdgeProcessModuleBase<DieCuttingCellData>
{
    public const string ModuleKey = "DieCutting";

    public override string ModuleId => ModuleKey;

    public override string ProcessType => ModuleKey;

    public override string DisplayName => "模切";

    protected override ProcessUploadMode? MesUploadMode => ProcessUploadMode.Single;

    protected override IStationRuntimeFactory CreateRuntimeFactory()
        => new DieCuttingStationRuntimeFactory();

    protected override void ConfigureModuleServices(IEdgeProcessModuleBuilder builder)
    {
        builder.RegisterParameters<DieCuttingParams.Mes, DieCuttingParams.Cloud, DieCuttingParams.Business>();

        var section = builder.Configuration.GetSection($"Modules:{ModuleKey}");
        builder.Services.AddOptions<DieCuttingModuleOptions>()
            .Bind(section.GetSection("Module"));

        builder.Services.AddSingleton<DieCuttingMesChannel>();
        builder.Services.AddSingleton<IDieCuttingMesScenarioChannel>(sp =>
            sp.GetRequiredService<DieCuttingMesChannel>());
        builder.Services.AddSingleton<IProcessMesUploader>(sp =>
            sp.GetRequiredService<DieCuttingMesChannel>());
        builder.Services.AddSingleton<IProductionContextFactory, DieCuttingContextFactory>();
        builder.RegisterStandardPlcSignalProfiles<
            DieCuttingPlcSignals.Interaction,
            DieCuttingPlcSignals.SingleRead,
            DieCuttingPlcSignals.ContinuousRead,
            DieCuttingPlcSignals.SingleWrite,
            DieCuttingPlcSignals.ContinuousWrite>();
        builder.RegisterHardwareProfile<DieCuttingHardwareProfileProvider>();
        builder.RegisterDevelopmentSample<DieCuttingDevelopmentSampleContributor>();
    }

    protected override void RegisterModuleViews(IEdgeProcessModuleBuilder builder)
        => builder.RegisterDieCuttingViews();
}
