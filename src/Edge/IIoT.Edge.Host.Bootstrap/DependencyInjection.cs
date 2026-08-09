using IIoT.Edge.Module.Contracts.Cloud;
using IIoT.Edge.Module.Contracts.Mes;
using IIoT.Edge.Module.Contracts.Shared;
using IIoT.Edge.Application;
using IIoT.Edge.Module.Contracts.Config;
using IIoT.Edge.Module.Contracts.Context;
using IIoT.Edge.Module.Contracts.DataPipeline;
using IIoT.Edge.Module.Contracts.DataPipeline.Stores;
using IIoT.Edge.Module.Contracts.Device;
using IIoT.Edge.Module.Contracts.Logging;
using IIoT.Edge.Module.Contracts.Modules;
using IIoT.Edge.Application.Features.Config.ModuleParameters;
using IIoT.Edge.Application.Features.Hardware.PlcTaskBindings;
using IIoT.Edge.Module.Contracts.Plc;
using IIoT.Edge.Module.Contracts.Tasks;
using IIoT.Edge.Module.Contracts.Time;
using IIoT.Edge.Module.Contracts.Updates;
using IIoT.Edge.Application.Common.Tasks;
using IIoT.Edge.Application.Common.Time;
using IIoT.Edge.Application.Common.Identity;
using IIoT.Edge.Application.Common.DataPipeline;
using IIoT.Edge.Module.Contracts.Diagnostics;
using IIoT.Edge.Host.Bootstrap.Modules;
using IIoT.Edge.Infrastructure.DeviceComm;
using IIoT.Edge.Infrastructure.Integration;
using IIoT.Edge.Infrastructure.Integration.EdgeHost;
using IIoT.Edge.Infrastructure.Integration.Mes;
using IIoT.Edge.Infrastructure.Integration.Recipe;
using IIoT.Edge.Infrastructure.Persistence.Dapper;
using IIoT.Edge.Infrastructure.Persistence.EfCore;
using IIoT.Edge.Infrastructure.Update;
using IIoT.Edge.Presentation.Navigation;
using IIoT.Edge.Presentation.Navigation.Features.DiagnosticsView;
using IIoT.Edge.Presentation.Panels;
using IIoT.Edge.Presentation.Shell;
using IIoT.Edge.Host.DataPipeline;
using IIoT.Edge.Host.DataPipeline.Tasks;
using IIoT.Edge.Host.DataPipeline.Services;
using IIoT.Edge.Module.Contracts.DataPipeline.Capacity;
using IIoT.Edge.Module.Contracts.DataPipeline.CellData;
using IIoT.Edge.Shell.Core;
using IIoT.Edge.UI.Shared.Modularity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using System.IO;
#if DEBUG
using IIoT.Edge.Presentation.VisualTestData;
#endif

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
        IEnumerable<IEdgeProcessModule> modules,
        IReadOnlyCollection<StartupDiagnosticIssue>? bootstrapDiagnosticIssues = null)
    {
        ArgumentNullException.ThrowIfNull(discoveredModules);
        ArgumentNullException.ThrowIfNull(moduleCatalogIssues);
        ArgumentNullException.ThrowIfNull(configuredEnabledModuleIds);
        ArgumentNullException.ThrowIfNull(modules);

        var enabledModules = modules.ToList();
        var discoveredModuleList = discoveredModules.ToArray();
        var moduleCatalogIssueList = moduleCatalogIssues.ToList();
        var bootstrapDiagnosticIssueList = bootstrapDiagnosticIssues?.ToList() ?? [];
        var configuredEnabledModuleList = configuredEnabledModuleIds.ToArray();
        var devicePluginRuntimeContext = new ConfigurationDevicePluginRuntimeContext(configuration);
        if (devicePluginRuntimeContext.Current.IsV3)
        {
            var binding = devicePluginRuntimeContext.Current;
            var descriptors = discoveredModuleList
                .Where(descriptor => string.Equals(
                    descriptor.ModuleId,
                    binding.ModuleId,
                    StringComparison.OrdinalIgnoreCase))
                .ToArray();
            var configuredMatches = configuredEnabledModuleList.Length == 1
                && string.Equals(
                    configuredEnabledModuleList[0],
                    binding.ModuleId,
                    StringComparison.OrdinalIgnoreCase);
            var descriptorMatches = descriptors.Length == 1
                && string.Equals(
                    descriptors[0].ProcessType,
                    binding.ProcessType,
                    StringComparison.OrdinalIgnoreCase);
            var moduleMatches = enabledModules.Count == 1
                && string.Equals(
                    enabledModules[0].ModuleId,
                    binding.ModuleId,
                    StringComparison.OrdinalIgnoreCase);

            if (!configuredMatches || !descriptorMatches || !moduleMatches)
            {
                moduleCatalogIssueList.Add(new ModuleCatalogIssue(
                    "DEVICE_PLUGIN_BINDING_MISMATCH",
                    "v3 Binding 的 ModuleId/ProcessType 与已签名插件清单或实际加载入口不一致，已拒绝注册。",
                    binding.ModuleId));
                enabledModules = [];
            }
            else
            {
                enabledModules =
                [
                    new RuntimeBoundEdgeProcessModule(enabledModules[0], binding.ProcessType)
                ];
            }
        }
        else
        {
            enabledModules = BindLegacyProcessTypesFromManifests(
                enabledModules,
                discoveredModuleList,
                moduleCatalogIssueList);
        }
        var efDbPath = Path.Combine(runtimePaths.DatabaseDirectory, "edge.db");

        services.AddSingleton(configuration);
        services.AddSingleton(runtimePaths);
        services.AddSingleton<IDevicePluginRuntimeContext>(devicePluginRuntimeContext);
        services.AddSingleton<IHostEnvironment>(
            new EdgeHostEnvironment(environmentName, AppContext.BaseDirectory));
        var cellDataTypeRegistry = new CellDataTypeRegistry();
        services.AddSingleton<ICellDataTypeRegistry>(cellDataTypeRegistry);
        services.AddSingleton<ICellDataJsonSerializer, CellDataJsonSerializer>();
        var productionTimeOptions =
            configuration.GetSection(ProductionTimeOptions.SectionName).Get<ProductionTimeOptions>()
            ?? new ProductionTimeOptions();
        if (!ProductionTimeProvider.IsTimeZoneAvailable(productionTimeOptions.TimeZoneId))
        {
            var invalidTimeZoneId = productionTimeOptions.TimeZoneId;
            productionTimeOptions.TimeZoneId = "Asia/Shanghai";
            bootstrapDiagnosticIssueList.Add(StartupDiagnosticIssueFactory.Create(
                "PRODUCTION_TIME_ZONE_INVALID",
                $"ProductionTime:TimeZoneId 无效，已回退到 Asia/Shanghai 并继续启动：{invalidTimeZoneId ?? "<null>"}。"));
        }
        services.AddSingleton(productionTimeOptions);
        services.AddSingleton<IProductionTimeProvider, ProductionTimeProvider>();
        services.AddSingleton(viewRegistry);
        services.AddSingleton<IViewRegistry>(viewRegistry);
        services.AddSingleton<IReadOnlyCollection<ModulePluginDescriptor>>(discoveredModuleList);
        services.AddSingleton<IReadOnlyCollection<ModuleCatalogIssue>>(moduleCatalogIssueList);
        services.AddSingleton<IReadOnlyCollection<StartupDiagnosticIssue>>(bootstrapDiagnosticIssueList);
        services.AddSingleton<IReadOnlyCollection<string>>(configuredEnabledModuleList);
        services.TryAddSingleton<ICrashLogWriter, CrashLogWriter>();
        services.TryAddSingleton<IModulePluginAssemblyResolver, ModulePluginAssemblyResolver>();
        services.TryAddSingleton<IModulePluginLoader, ModulePluginLoader>();
        services.TryAddSingleton<IModulePluginCompatibilityPolicy, ModulePluginCompatibilityPolicy>();
        services.TryAddSingleton<IModuleCatalog, DirectoryModuleCatalog>();
        services.AddSingleton<IModuleSeedInitializer, ModuleSeedInitializer>();
        services.AddSingleton<IStartupDiagnosticsStore, StartupDiagnosticsStore>();
        services.AddSingleton<ICloudUploadDiagnosticsStore, CloudUploadDiagnosticsStore>();
        services.AddSingleton<IMesUploadDiagnosticsStore, MesUploadDiagnosticsStore>();
        services.AddSingleton<IMesRetryDiagnosticsStore, MesRetryDiagnosticsStore>();
        services.AddSingleton<IExternalHeartbeatStateStore, ExternalHeartbeatStateStore>();
        services.AddSingleton<ICriticalPersistenceFallbackWriter, CriticalPersistenceFallbackWriter>();
        services.Configure<DataPipelineCapacityOptions>(configuration.GetSection(DataPipelineCapacityOptions.SectionName));
        services.AddSingleton(configuration.GetSection(DataPipelineRuntimeOptions.SectionName).Get<DataPipelineRuntimeOptions>() ?? new DataPipelineRuntimeOptions());
        services.AddSingleton(
            configuration.GetSection(DataPipelineRetryScheduleOptions.SectionName)
                .Get<DataPipelineRetryScheduleOptions>()
            ?? new DataPipelineRetryScheduleOptions());

        var shiftConfig = new ShiftConfig();
        configuration.GetSection("Shift").Bind(shiftConfig);
        services.AddSingleton(shiftConfig);

        services.AddEdgeApplication();
        services.AddEdgeUpdateInfrastructure(runtimePaths.BaseDirectory);
        services.AddEfCorePersistenceInfrastructure(efDbPath);
        services.AddDapperPersistenceInfrastructure(runtimePaths.DatabaseDirectory);
        services.AddIntegrationInfrastructure(configuration, runtimePaths);
        services.AddDeviceCommInfrastructure();
        services.AddEdgeRuntime(runtimePaths);

        services.AddShellPresentation();
        services.AddNavigationPresentation();
        services.AddPanelPresentation();
#if DEBUG
        services.AddVisualTestDataPresentation(configuration);
#endif

        RegisterHostViews(new HostViewRegistry(viewRegistry));
        enabledModules = RegisterModules(
            services,
            viewRegistry,
            configuration,
            enabledModules,
            cellDataTypeRegistry,
            moduleCatalogIssueList);
        var moduleAssemblies = enabledModules
            .Select(static module => module is RuntimeBoundEdgeProcessModule runtimeBound
                ? runtimeBound.ImplementationAssembly
                : module.GetType().Assembly)
            .Distinct()
            .ToArray();
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
        AddManagedBackgroundService(services, "Cloud.RuntimeHeartbeat",
            (sp, ct) => sp.GetRequiredService<IEdgeRuntimeHeartbeatService>().StartAsync(
                CreateRuntimeHeartbeatTarget(configuration, runtimePaths),
                ct),
            (sp, ct) => sp.GetRequiredService<IEdgeRuntimeHeartbeatService>().StopAsync(ct));
        AddManagedBackgroundService(services, "MES.Heartbeat",
            (sp, ct) => sp.GetRequiredService<MesHeartbeatTask>().StartAsync(ct),
            (sp, _) => sp.GetRequiredService<MesHeartbeatTask>().StopAsync());
        AddManagedBackgroundService(services, "PLC.Runtime",
            (sp, ct) => sp.GetRequiredService<IPlcConnectionManager>().InitializeAsync(ct),
            (sp, ct) => sp.GetRequiredService<IPlcConnectionManager>().StopAsync(ct));
        AddLongRunningManagedBackgroundTask(
            services,
            sp => sp.GetRequiredService<EdgeHostPlcRuntimeStateReportTask>());
        AddLongRunningManagedBackgroundTask(
            services,
            sp => sp.GetRequiredService<ProcessQueueTask>(),
            trackRuntimeStatus: true);
        AddLongRunningManagedBackgroundTask(
            services,
            sp => sp.GetRequiredService<CloudRetryTask>(),
            trackRuntimeStatus: true);
        AddLongRunningManagedBackgroundTask(
            services,
            sp => sp.GetRequiredService<MesRetryTask>(),
            trackRuntimeStatus: true);
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
        services.AddSingleton<IStartupAsyncDiagnosticValidator, StartupMesConfigurationValidator>();
        services.AddSingleton<IStartupAsyncDiagnosticValidator, StartupPlcConfigurationValidator>();
        services.AddSingleton<IStartupDiagnosticsReportBuilder, StartupDiagnosticsReportBuilder>();
        services.AddSingleton<IPlcRuntimeTaskBinder, PlcRuntimeTaskBinder>();
        services.AddSingleton<IPlcTaskBindingRuntimeTransaction, PlcTaskBindingRuntimeTransaction>();
        services.AddSingleton<IPlcTaskBindingTransactionService, PlcTaskBindingTransactionService>();
        services.AddSingleton<IPlcRuntimeDeviceReloader, PlcRuntimeDeviceReloader>();
        services.AddSingleton<IPlcRuntimeApplyService, PlcRuntimeApplyService>();
        services.AddSingleton<IAppRuntimeStateCoordinator, AppRuntimeStateCoordinator>();
        services.AddSingleton<IAppLifecycleCoordinator, AppLifecycleManager>();
        services.AddSingleton<AppLifecycleManager>(sp =>
            (AppLifecycleManager)sp.GetRequiredService<IAppLifecycleCoordinator>());

        return services;
    }

    internal static List<IEdgeProcessModule> BindLegacyProcessTypesFromManifests(
        IReadOnlyCollection<IEdgeProcessModule> modules,
        IReadOnlyCollection<ModulePluginDescriptor> descriptors,
        ICollection<ModuleCatalogIssue> issues)
    {
        ArgumentNullException.ThrowIfNull(modules);
        ArgumentNullException.ThrowIfNull(descriptors);
        ArgumentNullException.ThrowIfNull(issues);

        var resolved = new List<IEdgeProcessModule>(modules.Count);
        foreach (var module in modules)
        {
            if (!string.IsNullOrWhiteSpace(module.ProcessType))
            {
                resolved.Add(module);
                continue;
            }

            var manifestMatches = descriptors
                .Where(descriptor => string.Equals(
                    descriptor.ModuleId,
                    module.ModuleId,
                    StringComparison.OrdinalIgnoreCase))
                .ToArray();
            if (manifestMatches.Length != 1
                || string.IsNullOrWhiteSpace(manifestMatches[0].ProcessType))
            {
                issues.Add(new ModuleCatalogIssue(
                    "LEGACY_PLUGIN_PROCESS_TYPE_UNRESOLVED",
                    "旧插件未显式声明 ProcessType，且无法从唯一、已验证的旧 manifest 解析工序，已拒绝注册；禁止回退到 ModuleId。",
                    module.ModuleId));
                continue;
            }

            resolved.Add(new RuntimeBoundEdgeProcessModule(
                module,
                manifestMatches[0].ProcessType));
        }

        return resolved;
    }

    private static string? ResolveMediatRLicenseKey(IConfiguration configuration)
        => FirstNonEmpty(
            Environment.GetEnvironmentVariable("MediatR__LicenseKey"),
            Environment.GetEnvironmentVariable("MEDIATR_LICENSE_KEY"),
            configuration["MediatR:LicenseKey"]);

    private static EdgeUpdateTarget CreateRuntimeHeartbeatTarget(
        IConfiguration configuration,
        EdgeRuntimePaths runtimePaths)
    {
        var machineProfile = configuration["Shell:MachineProfile"]?.Trim();
        return new EdgeUpdateTarget(
            string.IsNullOrWhiteSpace(machineProfile) ? "Default" : machineProfile,
            runtimePaths.BaseDirectory,
            Environment.ProcessPath ?? string.Empty);
    }

    private static void AddManagedBackgroundService(IServiceCollection services, string serviceName,
        Func<IServiceProvider, CancellationToken, Task> startAsync,
        Func<IServiceProvider, CancellationToken, Task>? stopAsync = null)
        => services.AddSingleton<IManagedBackgroundService>(sp =>
            new DelegatingBackgroundService(
                serviceName,
                ct => startAsync(sp, ct),
                stopAsync is null ? null : ct => stopAsync(sp, ct)));

    private static void AddLongRunningManagedBackgroundTask(
        IServiceCollection services,
        Func<IServiceProvider, IBackgroundTask> taskFactory,
        bool trackRuntimeStatus = false)
        => services.AddSingleton<IManagedBackgroundService>(sp =>
            new LongRunningBackgroundTaskService(
                taskFactory(sp),
                sp.GetRequiredService<ILogService>(),
                trackRuntimeStatus
                    ? sp.GetRequiredService<IBackgroundServiceRuntimeStatusWriter>()
                    : null));

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

    private static List<IEdgeProcessModule> RegisterModules(
        IServiceCollection services,
        IViewRegistry viewRegistry,
        IConfiguration configuration,
        IReadOnlyCollection<IEdgeProcessModule> modules,
        ICellDataTypeRegistry cellDataTypeRegistry,
        ICollection<ModuleCatalogIssue> issues)
    {
        var cellDataRegistry = new CellDataRegistry(cellDataTypeRegistry);
        var runtimeRegistry = new StationRuntimeRegistry();
        var integrationRegistry = new ProcessIntegrationRegistry();
        var moduleParamRegistry = new ModuleParamRegistry();

        services.AddSingleton<ICellDataRegistry>(cellDataRegistry);
        services.AddSingleton<IStationRuntimeRegistry>(runtimeRegistry);
        services.AddSingleton<IProcessIntegrationRegistry>(integrationRegistry);
        services.AddSingleton<IModuleParamRegistry>(moduleParamRegistry);

        var duplicateModuleIds = modules
            .GroupBy(static module => module.ModuleId, StringComparer.OrdinalIgnoreCase)
            .Where(static group => group.Count() > 1)
            .Select(static group => group.Key)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var transaction = new ModuleRegistrationTransaction(
            services,
            viewRegistry,
            configuration,
            cellDataRegistry,
            runtimeRegistry,
            integrationRegistry,
            moduleParamRegistry);
        var registeredModules = new List<IEdgeProcessModule>(modules.Count);

        foreach (var module in modules)
        {
            if (duplicateModuleIds.Contains(module.ModuleId))
            {
                issues.Add(new ModuleCatalogIssue(
                    "PLUGIN_IDENTITY_DUPLICATE",
                    $"插件“{module.ModuleId}”的 ModuleId 重复，已拒绝注册。",
                    module.ModuleId));
                continue;
            }

            try
            {
                transaction.Register(module);
                registeredModules.Add(module);
            }
            catch (Exception ex)
            {
                issues.Add(new ModuleCatalogIssue(
                    "PLUGIN_CONFIGURE_FAILED",
                    $"插件“{module.ModuleId}”配置失败，已丢弃该插件的全部注册：{ex.Message}",
                    module.ModuleId));
            }
        }

        return registeredModules;
    }

    private sealed class DelegatingBackgroundTask : IStartupAwareBackgroundTask
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

        public BackgroundTaskRun StartWithStartup(CancellationToken cancellationToken)
        {
            var execution = _startAsync(cancellationToken);
            return new BackgroundTaskRun(Task.CompletedTask, execution);
        }

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
