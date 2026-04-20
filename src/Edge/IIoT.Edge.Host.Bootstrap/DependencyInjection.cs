using IIoT.Edge.Application;
using IIoT.Edge.Application.Abstractions.Context;
using IIoT.Edge.Application.Abstractions.DataPipeline;
using IIoT.Edge.Application.Abstractions.DataPipeline.Stores;
using IIoT.Edge.Application.Abstractions.DataPipeline.SyncTask;
using IIoT.Edge.Application.Abstractions.Device;
using IIoT.Edge.Application.Abstractions.Modules;
using IIoT.Edge.Application.Abstractions.Plc;
using IIoT.Edge.Application.Abstractions.Tasks;
using IIoT.Edge.Application.Common.Tasks;
using IIoT.Edge.Module.Abstractions;
using IIoT.Edge.Infrastructure.DeviceComm;
using IIoT.Edge.Infrastructure.Integration;
using IIoT.Edge.Infrastructure.Persistence.Dapper;
using IIoT.Edge.Infrastructure.Persistence.EfCore;
using IIoT.Edge.Presentation.Navigation;
using IIoT.Edge.Presentation.Navigation.Features.DiagnosticsView;
using IIoT.Edge.Presentation.Panels;
using IIoT.Edge.Presentation.Shell;
using IIoT.Edge.Runtime;
using IIoT.Edge.Runtime.DataPipeline.Tasks;
using IIoT.Edge.SharedKernel.DataPipeline.Capacity;
using IIoT.Edge.Shell.Core;
using IIoT.Edge.UI.Shared;
using IIoT.Edge.UI.Shared.Modularity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using System.IO;

namespace IIoT.Edge.Host.Bootstrap;

public static class DependencyInjection
{
    public static IServiceCollection AddEdgeHostBootstrap(
        this IServiceCollection services,
        IViewRegistry viewRegistry,
        IConfiguration configuration,
        string dbDir,
        IReadOnlyCollection<ModulePluginDescriptor> discoveredModules,
        IReadOnlyCollection<ModuleCatalogIssue> moduleCatalogIssues,
        IReadOnlyCollection<string> configuredEnabledModuleIds,
        IEnumerable<IEdgeStationModule> modules)
    {
        ArgumentNullException.ThrowIfNull(discoveredModules);
        ArgumentNullException.ThrowIfNull(moduleCatalogIssues);
        ArgumentNullException.ThrowIfNull(configuredEnabledModuleIds);
        ArgumentNullException.ThrowIfNull(modules);

        var enabledModules = modules.ToList();
        var discoveredModuleList = discoveredModules.ToArray();
        var moduleCatalogIssueList = moduleCatalogIssues.ToArray();
        var configuredEnabledModuleList = configuredEnabledModuleIds.ToArray();
        var moduleAssemblies = enabledModules
            .Select(static module => module.GetType().Assembly)
            .Distinct()
            .ToArray();
        var efDbPath = Path.Combine(dbDir, "edge.db");
        var excelDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "data", "excel");

        Directory.CreateDirectory(dbDir);
        Directory.CreateDirectory(excelDir);

        services.AddSingleton(configuration);
        services.AddSingleton(viewRegistry);
        services.AddSingleton<IViewRegistry>(viewRegistry);
        services.AddSingleton<IReadOnlyCollection<ModulePluginDescriptor>>(discoveredModuleList);
        services.AddSingleton<IReadOnlyCollection<ModuleCatalogIssue>>(moduleCatalogIssueList);
        services.AddSingleton<IReadOnlyCollection<string>>(configuredEnabledModuleList);
        services.AddSingleton<IDevelopmentSampleInitializer, DevelopmentSampleInitializer>();
        services.AddSingleton<IStartupDiagnosticsStore, StartupDiagnosticsStore>();
        services.AddSingleton<ICloudUploadDiagnosticsStore, CloudUploadDiagnosticsStore>();
        services.AddSingleton<IMesUploadDiagnosticsStore, MesUploadDiagnosticsStore>();
        services.AddSingleton<IMesRetryDiagnosticsStore, MesRetryDiagnosticsStore>();
        services.AddSingleton<ICriticalPersistenceFallbackWriter, CriticalPersistenceFallbackWriter>();
        services.Configure<DataPipelineCapacityOptions>(configuration.GetSection(DataPipelineCapacityOptions.SectionName));

        var shiftConfig = new ShiftConfig();
        configuration.GetSection("Shift").Bind(shiftConfig);
        services.AddSingleton(shiftConfig);

        services.AddEdgeApplication();
        services.AddEfCorePersistenceInfrastructure(efDbPath);
        services.AddDapperPersistenceInfrastructure(dbDir);
        services.AddIntegrationInfrastructure(configuration, excelDir);
        services.AddDeviceCommInfrastructure();
        services.AddEdgeRuntime();

        services.AddMediatR(cfg =>
        {
            cfg.LicenseKey = configuration["MediatR:LicenseKey"] ?? string.Empty;
            cfg.RegisterServicesFromAssemblies(
                [
                    typeof(IIoT.Edge.Application.DependencyInjection).Assembly,
                    typeof(IIoT.Edge.Presentation.Navigation.DependencyInjection).Assembly,
                    typeof(IIoT.Edge.Presentation.Panels.DependencyInjection).Assembly,
                    ..moduleAssemblies
                ]);
        });

        services.AddUiShared();
        services.AddAutoMapper(
            _ => { },
            [
                typeof(IIoT.Edge.Application.DependencyInjection).Assembly,
                typeof(IIoT.Edge.Presentation.Shell.DependencyInjection).Assembly,
                typeof(IIoT.Edge.Presentation.Navigation.DependencyInjection).Assembly,
                typeof(IIoT.Edge.Presentation.Panels.DependencyInjection).Assembly,
                typeof(IIoT.Edge.Infrastructure.Integration.DependencyInjection).Assembly,
                typeof(IIoT.Edge.Infrastructure.DeviceComm.DependencyInjection).Assembly,
                ..moduleAssemblies
            ]);
        services.AddShellPresentation();
        services.AddNavigationPresentation();
        services.AddPanelPresentation();

        RegisterHostViews(new HostViewRegistry(viewRegistry));
        RegisterModules(services, viewRegistry, enabledModules);
        viewRegistry.RegisterPanelViews();

        services.AddSingleton<IManagedBackgroundService>(sp =>
            new LongRunningBackgroundTaskService(
                new DelegatingBackgroundTask(
                    "RuntimeState.AutoSave",
                    ct => sp.GetRequiredService<IProductionContextStore>()
                        .StartAutoSaveAsync(ct, intervalSeconds: 30))));

        services.AddSingleton<IManagedBackgroundService>(sp =>
            new DelegatingBackgroundService(
                "Device.Heartbeat",
                ct => sp.GetRequiredService<IDeviceService>().StartAsync(ct),
                _ => sp.GetRequiredService<IDeviceService>().StopAsync()));

        services.AddSingleton<IManagedBackgroundService>(sp =>
            new DelegatingBackgroundService(
                "PLC.Runtime",
                ct => sp.GetRequiredService<IPlcConnectionManager>().InitializeAsync(ct),
                ct => sp.GetRequiredService<IPlcConnectionManager>().StopAsync(ct)));

        services.AddSingleton<IManagedBackgroundService>(sp =>
            new LongRunningBackgroundTaskGroupService(
                "DataPipeline.Runtime",
                [
                    sp.GetRequiredService<ProcessQueueTask>(),
                    sp.GetRequiredService<CloudRetryTask>(),
                    sp.GetRequiredService<MesRetryTask>()
                ]));

        services.AddSingleton<IManagedBackgroundService>(sp =>
            new DelegatingBackgroundService(
                "Cloud.CapacitySync",
                ct => sp.GetRequiredService<ICapacitySyncTask>().StartAsync(ct),
                _ => sp.GetRequiredService<ICapacitySyncTask>().StopAsync()));

        services.AddSingleton<IManagedBackgroundService>(sp =>
            new DelegatingBackgroundService(
                "Cloud.DeviceLogSync",
                ct => sp.GetRequiredService<IDeviceLogSyncTask>().StartAsync(ct),
                _ => sp.GetRequiredService<IDeviceLogSyncTask>().StopAsync()));

        services.AddSingleton<INavigationService, NavigationService>();
        services.AddSingleton<IAppLifecycleCoordinator, AppLifecycleManager>();
        services.AddSingleton<AppLifecycleManager>(sp =>
            (AppLifecycleManager)sp.GetRequiredService<IAppLifecycleCoordinator>());

        return services;
    }

    private static void RegisterHostViews(IViewRegistry registry)
    {
        registry.RegisterRoute(
            CoreViewIds.Diagnostics,
            typeof(DiagnosticsPage),
            typeof(DiagnosticsViewModel),
            cacheView: false);
        registry.RegisterMenu(new MenuInfo
        {
            Title = "系统诊断",
            ViewId = CoreViewIds.Diagnostics,
            Icon = "Stethoscope",
            Order = 999,
            RequiredPermission = string.Empty
        });
    }

    private static void RegisterModules(
        IServiceCollection services,
        IViewRegistry viewRegistry,
        IReadOnlyCollection<IEdgeStationModule> modules)
    {
        var cellDataRegistry = new CellDataRegistry();
        var runtimeRegistry = new StationRuntimeRegistry();
        var integrationRegistry = new ProcessIntegrationRegistry();

        services.AddSingleton<ICellDataRegistry>(cellDataRegistry);
        services.AddSingleton<IStationRuntimeRegistry>(runtimeRegistry);
        services.AddSingleton<IProcessIntegrationRegistry>(integrationRegistry);

        ValidateModuleIdentity(modules);

        foreach (var module in modules)
        {
            services.AddSingleton<IEdgeStationModule>(module);

            module.RegisterServices(services);
            module.RegisterCellData(cellDataRegistry);
            module.RegisterRuntime(runtimeRegistry);
            module.RegisterIntegrations(integrationRegistry);
            module.RegisterViews(new ModuleViewRegistry(viewRegistry, module.ModuleId));
        }

        ValidateModuleRegistrations(modules, cellDataRegistry, runtimeRegistry, integrationRegistry);
    }

    private static void ValidateModuleIdentity(IEnumerable<IEdgeStationModule> modules)
    {
        var moduleIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var processTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var module in modules)
        {
            if (!moduleIds.Add(module.ModuleId))
            {
                throw new InvalidOperationException($"Duplicate ModuleId detected: {module.ModuleId}");
            }

            if (!processTypes.Add(module.ProcessType))
            {
                throw new InvalidOperationException($"Duplicate ProcessType detected: {module.ProcessType}");
            }
        }
    }

    private static void ValidateModuleRegistrations(
        IEnumerable<IEdgeStationModule> modules,
        ICellDataRegistry cellDataRegistry,
        IStationRuntimeRegistry runtimeRegistry,
        IProcessIntegrationRegistry integrationRegistry)
    {
        foreach (var module in modules)
        {
            if (!cellDataRegistry.IsRegistered(module.ProcessType))
            {
                throw new InvalidOperationException(
                    $"Module '{module.ModuleId}' is missing CellData registration for process type '{module.ProcessType}'.");
            }

            if (!runtimeRegistry.HasFactory(module.ModuleId))
            {
                throw new InvalidOperationException(
                    $"Module '{module.ModuleId}' is missing PLC runtime factory registration.");
            }

            if (!integrationRegistry.HasCloudUploader(module.ProcessType))
            {
                throw new InvalidOperationException(
                    $"Module '{module.ModuleId}' is missing cloud uploader registration for process type '{module.ProcessType}'.");
            }
        }
    }

    private sealed class DelegatingBackgroundTask : IBackgroundTask
    {
        private readonly Func<CancellationToken, Task> _startAsync;
        private readonly Func<CancellationToken, Task> _stopAsync;

        public DelegatingBackgroundTask(
            string taskName,
            Func<CancellationToken, Task> startAsync,
            Func<CancellationToken, Task>? stopAsync = null)
        {
            TaskName = taskName;
            _startAsync = startAsync ?? throw new ArgumentNullException(nameof(startAsync));
            _stopAsync = stopAsync ?? (_ => Task.CompletedTask);
        }

        public string TaskName { get; }

        public Task StartAsync(CancellationToken ct) => _startAsync(ct);

        public Task StopAsync(CancellationToken ct) => _stopAsync(ct);
    }
}
