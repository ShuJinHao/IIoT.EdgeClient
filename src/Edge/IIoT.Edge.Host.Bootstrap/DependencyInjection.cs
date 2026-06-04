using IIoT.Edge.Application;
using IIoT.Edge.Application.Abstractions.Config;
using IIoT.Edge.Application.Abstractions.Context;
using IIoT.Edge.Application.Abstractions.DataPipeline;
using IIoT.Edge.Application.Abstractions.DataPipeline.Stores;
using IIoT.Edge.Application.Abstractions.DataPipeline.SyncTask;
using IIoT.Edge.Application.Abstractions.Device;
using IIoT.Edge.Application.Abstractions.Integration;
using IIoT.Edge.Application.Abstractions.Modules;
using IIoT.Edge.Application.Features.Config.ModuleParameters;
using IIoT.Edge.Application.Abstractions.Plc;
using IIoT.Edge.Application.Abstractions.Tasks;
using IIoT.Edge.Application.Abstractions.Time;
using IIoT.Edge.Application.Common.Tasks;
using IIoT.Edge.Application.Common.Time;
using IIoT.Edge.Host.Bootstrap.Modules;
using IIoT.Edge.Infrastructure.DeviceComm;
using IIoT.Edge.Infrastructure.Integration;
using IIoT.Edge.Infrastructure.Integration.Mes;
using IIoT.Edge.Infrastructure.Integration.Recipe;
using IIoT.Edge.Infrastructure.Persistence.Dapper;
using IIoT.Edge.Infrastructure.Persistence.EfCore;
using IIoT.Edge.Presentation.Navigation;
using IIoT.Edge.Presentation.Navigation.Features.DiagnosticsView;
using IIoT.Edge.Presentation.Panels;
using IIoT.Edge.Presentation.Shell;
using IIoT.Edge.Presentation.VisualTestData;
using IIoT.Edge.Runtime;
using IIoT.Edge.Runtime.DataPipeline.Tasks;
using IIoT.Edge.SharedKernel.DataPipeline.Capacity;
using IIoT.Edge.SharedKernel.DataPipeline.CellData;
using IIoT.Edge.Shell.Core;
using IIoT.Edge.UI.Shared.Modularity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using System.IO;

namespace IIoT.Edge.Host.Bootstrap;

public static class DependencyInjection
{
    public static IServiceCollection AddEdgeHostBootstrap(
        this IServiceCollection services,
        IViewRegistry viewRegistry,
        IConfiguration configuration,
        EdgeRuntimePaths runtimePaths,
        string environmentName,
        IReadOnlyCollection<ModulePluginDescriptor> discoveredModules,
        IReadOnlyCollection<ModuleCatalogIssue> moduleCatalogIssues,
        IReadOnlyCollection<string> configuredEnabledModuleIds,
        IEnumerable<IEdgeProcessModule> modules)
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
        var efDbPath = Path.Combine(runtimePaths.DatabaseDirectory, "edge.db");

        Directory.CreateDirectory(runtimePaths.DatabaseDirectory);
        Directory.CreateDirectory(runtimePaths.ExcelDirectory);
        Directory.CreateDirectory(runtimePaths.LogDirectory);

        services.AddSingleton(configuration);
        services.AddSingleton(runtimePaths);
        services.AddSingleton<IHostEnvironment>(
            new EdgeHostEnvironment(environmentName, AppContext.BaseDirectory));
        var cellDataTypeRegistry = new CellDataTypeRegistry();
        services.AddSingleton<ICellDataTypeRegistry>(cellDataTypeRegistry);
        services.AddSingleton<ICellDataJsonSerializer, CellDataJsonSerializer>();
        var productionTimeOptions =
            configuration.GetSection(ProductionTimeOptions.SectionName).Get<ProductionTimeOptions>()
            ?? new ProductionTimeOptions();
        productionTimeOptions.Validate();
        services.AddSingleton(productionTimeOptions);
        services.AddSingleton<IProductionTimeProvider, ProductionTimeProvider>();
        services.AddSingleton(viewRegistry);
        services.AddSingleton<IViewRegistry>(viewRegistry);
        services.AddSingleton<IReadOnlyCollection<ModulePluginDescriptor>>(discoveredModuleList);
        services.AddSingleton<IReadOnlyCollection<ModuleCatalogIssue>>(moduleCatalogIssueList);
        services.AddSingleton<IReadOnlyCollection<string>>(configuredEnabledModuleList);
        services.TryAddSingleton<ICrashLogWriter, CrashLogWriter>();
        services.TryAddSingleton<IModulePluginAssemblyResolver, ModulePluginAssemblyResolver>();
        services.TryAddSingleton<IModulePluginLoader, ModulePluginLoader>();
        services.TryAddSingleton<IModulePluginCompatibilityPolicy, ModulePluginCompatibilityPolicy>();
        services.TryAddSingleton<IModuleCatalog, DirectoryModuleCatalog>();
        services.AddSingleton<IDevelopmentSampleInitializer, DevelopmentSampleInitializer>();
        services.AddSingleton<IStartupDiagnosticsStore, StartupDiagnosticsStore>();
        services.AddSingleton<ICloudUploadDiagnosticsStore, CloudUploadDiagnosticsStore>();
        services.AddSingleton<IMesUploadDiagnosticsStore, MesUploadDiagnosticsStore>();
        services.AddSingleton<IMesRetryDiagnosticsStore, MesRetryDiagnosticsStore>();
        services.AddSingleton<IExternalHeartbeatStateStore, ExternalHeartbeatStateStore>();
        services.AddSingleton<ICriticalPersistenceFallbackWriter, CriticalPersistenceFallbackWriter>();
        services.Configure<DataPipelineCapacityOptions>(configuration.GetSection(DataPipelineCapacityOptions.SectionName));
        services.AddSingleton(configuration.GetSection(DataPipelineRuntimeOptions.SectionName).Get<DataPipelineRuntimeOptions>() ?? new DataPipelineRuntimeOptions());

        var shiftConfig = new ShiftConfig();
        configuration.GetSection("Shift").Bind(shiftConfig);
        services.AddSingleton(shiftConfig);

        services.AddEdgeApplication();
        services.AddEfCorePersistenceInfrastructure(efDbPath);
        services.AddDapperPersistenceInfrastructure(runtimePaths.DatabaseDirectory);
        services.AddIntegrationInfrastructure(configuration, runtimePaths);
        services.AddDeviceCommInfrastructure();
        services.AddEdgeRuntime(runtimePaths);

        services.AddMediatR(cfg =>
        {
            var licenseKey = ResolveMediatRLicenseKey(configuration);
            if (!string.IsNullOrWhiteSpace(licenseKey))
            {
                cfg.LicenseKey = licenseKey;
            }

            cfg.RegisterServicesFromAssemblies(
                [
                    typeof(IIoT.Edge.Application.DependencyInjection).Assembly,
                    typeof(IIoT.Edge.Presentation.Navigation.DependencyInjection).Assembly,
                    typeof(IIoT.Edge.Presentation.Panels.DependencyInjection).Assembly,
                    ..moduleAssemblies
                ]);
        });

        services.AddShellPresentation();
        services.AddNavigationPresentation();
        services.AddPanelPresentation();
        services.AddVisualTestDataPresentation(configuration);

        RegisterHostViews(new HostViewRegistry(viewRegistry));
        RegisterModules(services, viewRegistry, configuration, enabledModules, cellDataTypeRegistry);
        viewRegistry.RegisterPanelViews();

        AddLongRunningManagedBackgroundTask(
            services,
            sp => new DelegatingBackgroundTask(
                "RuntimeState.AutoSave",
                ct => sp.GetRequiredService<IProductionContextStore>()
                    .StartAutoSaveAsync(ct, intervalSeconds: 30)));
        AddManagedBackgroundService(services, "Config.RuntimeWarmup",
            (sp, ct) => sp.GetRequiredService<ILocalSystemRuntimeConfigService>().EnsureInitializedAsync(ct));
        AddManagedBackgroundService(services, "Device.Heartbeat",
            (sp, ct) => sp.GetRequiredService<IDeviceService>().StartAsync(ct),
            (sp, _) => sp.GetRequiredService<IDeviceService>().StopAsync());
        AddManagedBackgroundService(services, "MES.Heartbeat",
            (sp, ct) => sp.GetRequiredService<MesHeartbeatTask>().StartAsync(ct),
            (sp, _) => sp.GetRequiredService<MesHeartbeatTask>().StopAsync());
        AddManagedBackgroundService(services, "PLC.Runtime",
            (sp, ct) => sp.GetRequiredService<IPlcConnectionManager>().InitializeAsync(ct),
            (sp, ct) => sp.GetRequiredService<IPlcConnectionManager>().StopAsync(ct));
        AddLongRunningManagedBackgroundTaskGroup(
            services,
            "DataPipeline.Runtime",
            sp =>
            [
                sp.GetRequiredService<ProcessQueueTask>(),
                sp.GetRequiredService<CloudRetryTask>(),
                sp.GetRequiredService<MesRetryTask>()
            ]);
        AddManagedBackgroundService(services, "Cloud.CapacitySync",
            (sp, ct) => sp.GetRequiredService<ICapacitySyncTask>().StartAsync(ct),
            (sp, _) => sp.GetRequiredService<ICapacitySyncTask>().StopAsync());
        AddManagedBackgroundService(services, "Cloud.DeviceLogSync",
            (sp, ct) => sp.GetRequiredService<IDeviceLogSyncTask>().StartAsync(ct),
            (sp, _) => sp.GetRequiredService<IDeviceLogSyncTask>().StopAsync());
        AddLongRunningManagedBackgroundTask(
            services,
            sp => sp.GetRequiredService<RecipeSyncTask>());

        services.AddSingleton<IAppStartupInitializer, AppStartupInitializer>();
        services.AddSingleton<IStartupPluginLifecycleSnapshotBuilder, StartupPluginLifecycleSnapshotBuilder>();
        services.AddSingleton<IStartupConfigurationProfileBuilder, StartupConfigurationProfileBuilder>();
        services.AddSingleton<IStartupModuleRegistrationSnapshotBuilder, StartupModuleRegistrationSnapshotBuilder>();
        services.AddSingleton<IStartupDiagnosticValidator, StartupAppSettingsValidator>();
        services.AddSingleton<IStartupDiagnosticValidator, StartupModuleRegistrationValidator>();
        services.AddSingleton<IStartupAsyncDiagnosticValidator, StartupPlcConfigurationValidator>();
        services.AddSingleton<IStartupDiagnosticsReportBuilder, StartupDiagnosticsReportBuilder>();
        services.AddSingleton<IPlcRuntimeTaskBinder, PlcRuntimeTaskBinder>();
        services.AddSingleton<IAppRuntimeStateCoordinator, AppRuntimeStateCoordinator>();
        services.AddSingleton<IAppLifecycleCoordinator, AppLifecycleManager>();
        services.AddSingleton<AppLifecycleManager>(sp =>
            (AppLifecycleManager)sp.GetRequiredService<IAppLifecycleCoordinator>());

        return services;
    }

    private static string? ResolveMediatRLicenseKey(IConfiguration configuration)
        => FirstNonEmpty(
            Environment.GetEnvironmentVariable("MediatR__LicenseKey"),
            Environment.GetEnvironmentVariable("MEDIATR_LICENSE_KEY"),
            configuration["MediatR:LicenseKey"]);

    private static void AddManagedBackgroundService(IServiceCollection services, string serviceName,
        Func<IServiceProvider, CancellationToken, Task> startAsync,
        Func<IServiceProvider, CancellationToken, Task>? stopAsync = null)
        => services.AddSingleton<IManagedBackgroundService>(sp =>
            new DelegatingBackgroundService(
                serviceName,
                ct => startAsync(sp, ct),
                stopAsync is null ? null : ct => stopAsync(sp, ct)));

    private static void AddLongRunningManagedBackgroundTask(IServiceCollection services, Func<IServiceProvider, IBackgroundTask> taskFactory)
        => services.AddSingleton<IManagedBackgroundService>(sp =>
            new LongRunningBackgroundTaskService(taskFactory(sp)));

    private static void AddLongRunningManagedBackgroundTaskGroup(IServiceCollection services, string serviceName,
        Func<IServiceProvider, IEnumerable<IBackgroundTask>> taskFactory)
        => services.AddSingleton<IManagedBackgroundService>(sp =>
            new LongRunningBackgroundTaskGroupService(serviceName, taskFactory(sp)));

    private static string? FirstNonEmpty(params string?[] values)
        => values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim();

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
            TitleResourceKey = "Navigation_Menu_CoreDiagnostics",
            ViewId = CoreViewIds.Diagnostics,
            Icon = "Stethoscope",
            Order = 999,
            RequiredPermission = string.Empty
        });
    }

    private static void RegisterModules(
        IServiceCollection services,
        IViewRegistry viewRegistry,
        IConfiguration configuration,
        IReadOnlyCollection<IEdgeProcessModule> modules,
        ICellDataTypeRegistry cellDataTypeRegistry)
    {
        var cellDataRegistry = new CellDataRegistry(cellDataTypeRegistry);
        var runtimeRegistry = new StationRuntimeRegistry();
        var integrationRegistry = new ProcessIntegrationRegistry();
        var moduleParamRegistry = new ModuleParamRegistry();

        services.AddSingleton<ICellDataRegistry>(cellDataRegistry);
        services.AddSingleton<IStationRuntimeRegistry>(runtimeRegistry);
        services.AddSingleton<IProcessIntegrationRegistry>(integrationRegistry);
        services.AddSingleton<IModuleParamRegistry>(moduleParamRegistry);

        ValidateModuleIdentity(modules);

        foreach (var module in modules)
        {
            services.AddSingleton<IEdgeProcessModule>(module);
            var builder = new EdgeProcessModuleBuilder(
                module.ModuleId,
                module.ProcessType,
                services,
                configuration,
                new ModuleViewRegistry(viewRegistry, module.ModuleId),
                cellDataRegistry,
                runtimeRegistry,
                integrationRegistry,
                moduleParamRegistry);

            module.Configure(builder);
        }
    }

    private static void ValidateModuleIdentity(IEnumerable<IEdgeProcessModule> modules)
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

    private sealed class EdgeHostEnvironment(string environmentName, string contentRootPath) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = string.IsNullOrWhiteSpace(environmentName)
            ? Environments.Production
            : environmentName.Trim();

        public string ApplicationName { get; set; } = "IIoT.Edge.Shell";

        public string ContentRootPath { get; set; } = contentRootPath;

        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
