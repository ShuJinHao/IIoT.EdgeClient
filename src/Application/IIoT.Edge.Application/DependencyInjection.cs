using IIoT.Edge.Application.Abstractions.Auth;
using IIoT.Edge.Application.Abstractions.Config;
using IIoT.Edge.Application.Abstractions.DataPipeline;
using IIoT.Edge.Application.Abstractions.Tasks;
using IIoT.Edge.Application.Abstractions.Modules;
using IIoT.Edge.Application.Auth;
using IIoT.Edge.Application.Common.Diagnostics;
using IIoT.Edge.Application.Common.Tasks;
using IIoT.Edge.Application.Features.DataPipeline.DeadLetters;
using IIoT.Edge.Application.Features.Config.LocalParameterConfig;
using IIoT.Edge.Application.Features.Config.ModuleParameters;
using IIoT.Edge.Application.Features.Config.ParamView;
using IIoT.Edge.Application.Features.Formula.RecipeView;
using IIoT.Edge.Application.Features.Hardware.HardwareConfigView;
using IIoT.Edge.Application.Features.Hardware.IOView;
using IIoT.Edge.Application.Features.Hardware.PlcTaskBindings;
using IIoT.Edge.Application.Features.Production.CapacityView;
using IIoT.Edge.Application.Features.Production.DataView;
using IIoT.Edge.Application.Features.Production.Equipment;
using IIoT.Edge.Application.Features.Production.Monitor;
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
        services.AddSingleton<LocalSystemRuntimeConfigService>();
        services.AddSingleton<ILocalSystemRuntimeConfigService>(sp => sp.GetRequiredService<LocalSystemRuntimeConfigService>());
        services.AddSingleton(typeof(IModuleParamProvider<,,>), typeof(ModuleParamProvider<,,>));
        services.AddSingleton<IModuleParamRoleProvider, ModuleParamRoleProvider>();
        services.AddSingleton<IDeadLetterMaintenanceService, DeadLetterMaintenanceService>();
        services.AddTransient<IParamViewCrudService, ParamViewCrudService>();
        services.AddTransient<IIoViewQueryFacade, IoViewQueryFacade>();
        services.AddTransient<IHardwareConfigCrudService, HardwareConfigCrudService>();
        services.AddTransient<IPlcTaskBindingService, PlcTaskBindingService>();
        services.AddTransient<IRecipeViewCrudService, RecipeViewCrudService>();
        services.AddTransient<ICapacityQueryFacade, CapacityQueryFacade>();
        services.AddTransient<IProductionDataQueryFacade, ProductionDataQueryFacade>();
        services.AddTransient<IMonitorSnapshotQueryFacade, MonitorSnapshotQueryFacade>();
        services.AddTransient<IMonitorConfiguredDeviceLoader, MonitorConfiguredDeviceLoader>();
        services.AddTransient<IMonitorStateMachineTaskProjection, MonitorStateMachineTaskProjection>();
        services.AddTransient<IMonitorSnapshotSourceMatcher, MonitorSnapshotSourceMatcher>();
        services.AddTransient<IMonitorSnapshotProjectionBuilder, MonitorSnapshotProjectionBuilder>();
        services.AddTransient<IEquipmentPanelService, EquipmentPanelService>();
        services.AddSingleton<IEdgeSyncDiagnosticsQuery, EdgeSyncDiagnosticsQuery>();
        services.AddSingleton<IBackgroundServiceCoordinator, BackgroundServiceCoordinator>();
        return services;
    }
}
