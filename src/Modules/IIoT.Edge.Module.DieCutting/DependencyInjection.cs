using IIoT.Edge.Application.Abstractions.Context;
using IIoT.Edge.Application.Abstractions.Config;
using IIoT.Edge.Application.Abstractions.Logging;
using IIoT.Edge.Application.Abstractions.Mes;
using IIoT.Edge.Application.Abstractions.Modules;
using IIoT.Edge.Application.Abstractions.Time;
using IIoT.Edge.Application.Features.Production.Planning;
using IIoT.Edge.Application.Modules;
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
/// 模切只读采集插件共享入口，注册只读 PLC 点位、采样上传任务、MES 通道和开发样本。
/// </summary>
public abstract class DieCuttingModuleBase : EdgeProcessModuleBase<DieCuttingCellData>
{
    private readonly DieCuttingModuleDefinition _definition;

    protected DieCuttingModuleBase(DieCuttingModuleDefinition definition)
    {
        _definition = definition ?? throw new ArgumentNullException(nameof(definition));
    }

    public override string ModuleId => _definition.ModuleId;

    public override string ProcessType => _definition.ProcessType;

    public override string DisplayName => _definition.DisplayName;

    protected override ProcessUploadMode? MesUploadMode => ProcessUploadMode.Single;

    protected override IStationRuntimeFactory CreateRuntimeFactory()
        => new DieCuttingStationRuntimeFactory(_definition);

    protected override void ConfigureModuleServices(IEdgeProcessModuleBuilder builder)
    {
        builder.Services.AddSingleton(_definition);
        builder.RegisterParameters<DieCuttingParams.Mes, DieCuttingParams.Cloud, DieCuttingParams.Business>(
        [
            new ModuleParamDefaultOverride(
                ModuleParamCategory.Mes,
                nameof(DieCuttingParams.Mes.UpperComputerNo),
                _definition.UpperComputerNo),
            new ModuleParamDefaultOverride(
                ModuleParamCategory.Mes,
                nameof(DieCuttingParams.Mes.OperationCode),
                _definition.OperationCode)
        ]);

        var section = builder.Configuration.GetSection($"Modules:{ModuleId}");
        builder.Services.AddOptions<DieCuttingModuleOptions>()
            .Bind(section.GetSection("Module"));

        builder.Services.AddSingleton<DieCuttingMesChannel>(sp =>
            new(
                _definition,
                sp.GetRequiredService<MesRequestExecutor>(),
                sp.GetRequiredService<IModuleParamRoleProvider>(),
                sp.GetRequiredService<IModuleParamProvider<DieCuttingParams.Mes, DieCuttingParams.Cloud, DieCuttingParams.Business>>(),
                sp.GetRequiredService<ILogService>(),
                sp.GetRequiredService<IProductionTimeProvider>()));
        builder.Services.AddSingleton<IDieCuttingMesScenarioChannel>(sp =>
            sp.GetRequiredService<DieCuttingMesChannel>());
        builder.Services.AddSingleton<IProcessMesUploader>(sp =>
            sp.GetRequiredService<IDieCuttingMesScenarioChannel>());
        builder.Services.AddSingleton<DieCuttingProductionPlanService>();
        builder.Services.AddSingleton<IProductionPlanSelectionService>(sp =>
            sp.GetRequiredService<DieCuttingProductionPlanService>());
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
        => builder.RegisterDieCuttingViews(DisplayName);
}
