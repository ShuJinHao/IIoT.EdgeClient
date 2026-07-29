using IIoT.Edge.Module.Contracts.Auth;
using IIoT.Edge.Module.Contracts.Config;
using IIoT.Edge.Module.Contracts.Cloud;
using IIoT.Edge.Module.Contracts.DataPipeline;
using IIoT.Edge.Module.Contracts.Tasks;
using IIoT.Edge.Module.Contracts.Modules;
using IIoT.Edge.Module.Contracts.Plc;
using IIoT.Edge.Module.Contracts.Updates;
using IIoT.Edge.Application.Auth;
using IIoT.Edge.Application.Common.Diagnostics;
using IIoT.Edge.Application.Common.Plc;
using IIoT.Edge.Application.Common.Tasks;
using IIoT.Edge.Application.Features.DataPipeline.DeadLetters;
using IIoT.Edge.Application.Features.Config.LocalParameterConfig;
using IIoT.Edge.Application.Features.Config.CloudApi;
using IIoT.Edge.Application.Features.Config.ModuleParameters;
using IIoT.Edge.Application.Features.Config.ParamView;
using IIoT.Edge.Application.Features.Config.SchemaReconciliation;
using IIoT.Edge.Application.Features.Formula.RecipeView;
using IIoT.Edge.Application.Features.Hardware.HardwareConfigView;
using IIoT.Edge.Application.Features.Hardware.IOView;
using IIoT.Edge.Application.Features.Hardware.PlcTaskBindings;
using IIoT.Edge.Application.Features.Production.CapacityView;
using IIoT.Edge.Application.Features.Production.Equipment;
using IIoT.Edge.Application.Features.Production.Planning;
using IIoT.Edge.Module.Contracts.Production;
using IIoT.Edge.Application.Features.Production.Monitor;
using IIoT.Edge.Application.Features.Updates;
using IIoT.Edge.Application.Modules.Hardware;
using IIoT.Edge.Module.Contracts.Hardware;
using IIoT.Edge.Module.Sdk.Hardware;
using IIoT.Edge.Application.Modules.Samples;
using Microsoft.Extensions.DependencyInjection;

namespace IIoT.Edge.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddEdgeApplication(this IServiceCollection services)
    {
        services.AddSingleton<CapacityCloudQueryService>();
        services.AddSingleton<IClientPermissionService, ClientPermissionService>();
        services.AddSingleton<LocalParameterConfigService>();
        services.AddSingleton<ILocalParameterConfigService>(sp => sp.GetRequiredService<LocalParameterConfigService>());
        services.AddSingleton<ILocalParameterConfigChangePublisher>(sp => sp.GetRequiredService<LocalParameterConfigService>());
        services.AddSingleton<ILocalSystemConfigSnapshotReader>(sp => sp.GetRequiredService<LocalParameterConfigService>());
        services.AddSingleton<LocalSystemRuntimeConfigService>();
        services.AddSingleton<ILocalSystemRuntimeConfigService>(sp => sp.GetRequiredService<LocalSystemRuntimeConfigService>());
        services.AddSingleton<ICloudExecutionPolicy, CloudExecutionPolicy>();
        services.AddSingleton<ICloudSystemSwitchMigration, CloudSystemSwitchMigration>();
        services.AddSingleton<ModuleParamValueSnapshotLoader>();
        services.AddSingleton<ModuleHardwareProfileResolver>();
        services.AddSingleton<IModuleDevelopmentSeedWriter, ModuleDevelopmentSeedWriter>();
        services.AddSingleton(typeof(IModuleParamProvider<,,>), typeof(ModuleParamProvider<,,>));
        services.AddSingleton<IModuleParamRoleProvider, ModuleParamRoleProvider>();
        services.AddSingleton<IConfigSchemaReconciler, ConfigSchemaReconciler>();
        foreach (var category in new[] { ModuleParamCategory.Mes, ModuleParamCategory.Cloud, ModuleParamCategory.Business })
        {
            var schemaId = ModuleParamSchemaIds.ForCategory(category);
            services.AddSingleton<IConfigSchemaSource>(sp => new ModuleParamSchemaSource(
                sp.GetRequiredService<IModuleParamRegistry>(),
                category,
                schemaId));
            services.AddSingleton<IConfigValueStore>(sp => new ModuleParamConfigValueStore(
                sp.GetRequiredService<ILocalParameterConfigService>(),
                category,
                schemaId));
        }

        services.AddSingleton<IConfigSchemaSource, CloudApiConfigSchemaSource>();
        services.AddSingleton<IConfigValueStore, CloudApiConfigValueStore>();
        services.AddSingleton<IConfigSchemaSource, IoMappingSchemaSource>();
        services.AddSingleton<IConfigValueStore, IoMappingConfigValueStore>();
        services.AddSingleton<IDeadLetterMaintenanceService, DeadLetterMaintenanceService>();
        services.AddTransient<IParamViewCrudService, ParamViewCrudService>();
        services.AddTransient<IIoViewQueryFacade, IoViewQueryFacade>();
        services.AddTransient<IHardwareConfigCrudService, HardwareConfigCrudService>();
        services.AddTransient<IPlcTaskBindingService, PlcTaskBindingService>();
        services.AddTransient<IPlcTaskBindingPersistenceTransaction, PlcTaskBindingService>();
        services.AddSingleton<IPlcTaskBindingTransactionService, UnavailablePlcTaskBindingTransactionService>();
        services.AddSingleton<IPlcRuntimeApplyService, NoopPlcRuntimeApplyService>();
        services.AddTransient<IRecipeViewCrudService, RecipeViewCrudService>();
        services.AddTransient<ICapacityQueryFacade, CapacityQueryFacade>();
        services.AddTransient<IMonitorSnapshotQueryFacade, MonitorSnapshotQueryFacade>();
        services.AddTransient<IMonitorConfiguredDeviceLoader, MonitorConfiguredDeviceLoader>();
        services.AddTransient<IMonitorStateMachineTaskProjection, MonitorStateMachineTaskProjection>();
        services.AddTransient<IMonitorSnapshotSourceMatcher, MonitorSnapshotSourceMatcher>();
        services.AddTransient<IMonitorSnapshotProjectionBuilder, MonitorSnapshotProjectionBuilder>();
        services.AddTransient<IEquipmentPanelService, EquipmentPanelService>();
        services.AddSingleton<IProductionPlanSelectionServiceResolver, ProductionPlanSelectionServiceResolver>();
        services.AddSingleton<IEdgeVersionCompatibilityPolicy, EdgeVersionCompatibilityPolicy>();
        services.AddSingleton<IEdgeReleaseService, EdgeReleaseService>();
        services.AddSingleton<IEdgeRuntimeHeartbeatService, EdgeRuntimeHeartbeatService>();
        services.AddSingleton<IEdgeSyncDiagnosticsQuery, EdgeSyncDiagnosticsQuery>();
        services.AddSingleton(new BackgroundServiceCoordinatorOptions());
        services.AddSingleton<IBackgroundServiceCoordinator, BackgroundServiceCoordinator>();
        return services;
    }
}
