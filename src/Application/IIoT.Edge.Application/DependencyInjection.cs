using IIoT.Edge.Application.Abstractions.Auth;
using IIoT.Edge.Application.Abstractions.Config;
using IIoT.Edge.Application.Abstractions.Tasks;
using IIoT.Edge.Application.Abstractions.Modules;
using IIoT.Edge.Application.Auth;
using IIoT.Edge.Application.Common.Diagnostics;
using IIoT.Edge.Application.Common.Tasks;
using IIoT.Edge.Application.Features.Config.LocalParameterConfig;
using IIoT.Edge.Application.Features.Config.ParamView;
using IIoT.Edge.Application.Features.Formula.RecipeView;
using IIoT.Edge.Application.Features.Hardware.HardwareConfigView;
using IIoT.Edge.Application.Features.Production.CapacityView;
using IIoT.Edge.Application.Features.Production.DataView;
using IIoT.Edge.Application.Features.Production.Equipment;
using IIoT.Edge.Application.Features.Production.Monitor;
using IIoT.Edge.Application.Features.SysLog.LogView;
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
        services.AddTransient<IParamViewCrudService, ParamViewCrudService>();
        services.AddTransient<IHardwareConfigCrudService, HardwareConfigCrudService>();
        services.AddTransient<IRecipeViewCrudService, RecipeViewCrudService>();
        services.AddTransient<ICapacityViewService, CapacityViewService>();
        services.AddTransient<IDataViewService, DataViewService>();
        services.AddTransient<IMonitorViewService, MonitorViewService>();
        services.AddTransient<IEquipmentPanelService, EquipmentPanelService>();
        services.AddTransient<ILogViewService, LogViewService>();
        services.AddSingleton<IEdgeSyncDiagnosticsQuery, EdgeSyncDiagnosticsQuery>();
        services.AddSingleton<IBackgroundServiceCoordinator, BackgroundServiceCoordinator>();
        return services;
    }
}
