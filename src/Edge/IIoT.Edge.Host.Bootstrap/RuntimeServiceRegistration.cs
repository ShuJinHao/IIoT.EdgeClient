using IIoT.Edge.Application;
using IIoT.Edge.Application.Abstractions.Config;
using IIoT.Edge.Application.Abstractions.Context;
using IIoT.Edge.Application.Abstractions.DataPipeline;
using IIoT.Edge.Application.Abstractions.DataPipeline.Stores;
using IIoT.Edge.Application.Abstractions.DataPipeline.SyncTask;
using IIoT.Edge.Application.Abstractions.Device;
using IIoT.Edge.Application.Abstractions.Integration;
using IIoT.Edge.Application.Abstractions.Logging;
using IIoT.Edge.Application.Abstractions.Modules;
using IIoT.Edge.Application.Abstractions.Plc;
using IIoT.Edge.Application.Abstractions.Tasks;
using IIoT.Edge.Application.Abstractions.Time;
using IIoT.Edge.Application.Common.Tasks;
using IIoT.Edge.Application.Common.Time;
using IIoT.Edge.Application.Features.Config.ModuleParameters;
using IIoT.Edge.Host.Bootstrap.Modules;
using IIoT.Edge.Infrastructure.DeviceComm;
using IIoT.Edge.Infrastructure.Integration;
using IIoT.Edge.Infrastructure.Integration.Mes;
using IIoT.Edge.Infrastructure.Integration.Recipe;
using IIoT.Edge.Infrastructure.Persistence.Dapper;
using IIoT.Edge.Infrastructure.Persistence.EfCore;
using IIoT.Edge.Runtime;
using IIoT.Edge.Runtime.DataPipeline.Tasks;
using IIoT.Edge.SharedKernel.DataPipeline.Capacity;
using IIoT.Edge.SharedKernel.DataPipeline.CellData;
using IIoT.Edge.Host.Bootstrap;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using System.IO;

namespace IIoT.Edge.Host.Bootstrap;

public static class RuntimeServiceRegistration
{
    public static IServiceCollection AddEdgeHostRuntimeServices(
        this IServiceCollection services,
        EdgeHostBootstrapOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var configuration = options.Configuration;
        var runtimePaths = options.RuntimePaths;
        var efDbPath = Path.Combine(runtimePaths.DatabaseDirectory, "edge.db");

        Directory.CreateDirectory(runtimePaths.DatabaseDirectory);
        Directory.CreateDirectory(runtimePaths.ContextDirectory);
        Directory.CreateDirectory(runtimePaths.ExcelDirectory);
        Directory.CreateDirectory(runtimePaths.LogDirectory);
        Directory.CreateDirectory(runtimePaths.RecipeDirectory);

        services.AddSingleton(configuration);
        services.AddSingleton(runtimePaths);
        services.AddSingleton<IHostEnvironment>(
            new EdgeHostEnvironment(options.EnvironmentName, runtimePaths.BaseDirectory));
        services.TryAddSingleton<ILogService, EdgeHostLogService>();
        services.TryAddSingleton<IModuleParamRegistry, ModuleParamRegistry>();
        services.TryAddSingleton<ICellDataTypeRegistry, CellDataTypeRegistry>();
        services.TryAddSingleton<ICellDataJsonSerializer, CellDataJsonSerializer>();
        services.TryAddSingleton<IProcessIntegrationRegistry, ProcessIntegrationRegistry>();
        services.TryAddSingleton<IReadOnlyCollection<ModulePluginDescriptor>>(Array.Empty<ModulePluginDescriptor>());
        services.TryAddSingleton<IReadOnlyCollection<ModuleCatalogIssue>>(Array.Empty<ModuleCatalogIssue>());
        services.TryAddSingleton<IReadOnlyCollection<string>>(Array.Empty<string>());
        services.TryAddSingleton<ICrashLogWriter, CrashLogWriter>();
        services.TryAddSingleton<IDevelopmentSampleInitializer, DevelopmentSampleInitializer>();
        services.TryAddSingleton<IStartupDiagnosticsStore, StartupDiagnosticsStore>();
        services.TryAddSingleton<ICloudUploadDiagnosticsStore, CloudUploadDiagnosticsStore>();
        services.TryAddSingleton<IMesUploadDiagnosticsStore, MesUploadDiagnosticsStore>();
        services.TryAddSingleton<IMesRetryDiagnosticsStore, MesRetryDiagnosticsStore>();
        services.TryAddSingleton<IExternalHeartbeatStateStore, ExternalHeartbeatStateStore>();
        services.TryAddSingleton<ICriticalPersistenceFallbackWriter, CriticalPersistenceFallbackWriter>();

        var productionTimeOptions =
            configuration.GetSection(ProductionTimeOptions.SectionName).Get<ProductionTimeOptions>()
            ?? new ProductionTimeOptions();
        productionTimeOptions.Validate();
        services.AddSingleton(productionTimeOptions);
        services.AddSingleton<IProductionTimeProvider, ProductionTimeProvider>();

        services.Configure<DataPipelineCapacityOptions>(configuration.GetSection(DataPipelineCapacityOptions.SectionName));
        services.AddSingleton(
            configuration.GetSection(DataPipelineRuntimeOptions.SectionName).Get<DataPipelineRuntimeOptions>()
            ?? new DataPipelineRuntimeOptions());

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

            cfg.RegisterServicesFromAssemblies(typeof(IIoT.Edge.Application.DependencyInjection).Assembly);
        });

        services.AddAutoMapper(
            _ => { },
            [
                typeof(IIoT.Edge.Application.DependencyInjection).Assembly,
                typeof(IIoT.Edge.Infrastructure.Integration.DependencyInjection).Assembly,
                typeof(IIoT.Edge.Infrastructure.DeviceComm.DependencyInjection).Assembly
            ]);

        RegisterLifecycleServices(services);

        return services;
    }

    private static void RegisterLifecycleServices(IServiceCollection services)
    {
        services.AddSingleton<IManagedBackgroundService>(sp =>
            new LongRunningBackgroundTaskService(
                new DelegatingBackgroundTask(
                    "RuntimeState.AutoSave",
                    ct => sp.GetRequiredService<IProductionContextStore>()
                        .StartAutoSaveAsync(ct, intervalSeconds: 30))));

        services.AddSingleton<IManagedBackgroundService>(sp =>
            new DelegatingBackgroundService(
                "Config.RuntimeWarmup",
                ct => sp.GetRequiredService<ILocalSystemRuntimeConfigService>().EnsureInitializedAsync(ct),
                _ => Task.CompletedTask));

        services.AddSingleton<IManagedBackgroundService>(sp =>
            new DelegatingBackgroundService(
                "Device.Heartbeat",
                ct => sp.GetRequiredService<IDeviceService>().StartAsync(ct),
                _ => sp.GetRequiredService<IDeviceService>().StopAsync()));

        services.AddSingleton<IManagedBackgroundService>(sp =>
            new DelegatingBackgroundService(
                "MES.Heartbeat",
                ct => sp.GetRequiredService<MesHeartbeatTask>().StartAsync(ct),
                _ => sp.GetRequiredService<MesHeartbeatTask>().StopAsync()));

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

        services.AddSingleton<IManagedBackgroundService>(sp =>
            new LongRunningBackgroundTaskService(
                sp.GetRequiredService<RecipeSyncTask>()));

        services.TryAddSingleton<IAppStartupInitializer, AppStartupInitializer>();
        services.TryAddSingleton<IStartupPluginLifecycleSnapshotBuilder, StartupPluginLifecycleSnapshotBuilder>();
        services.TryAddSingleton<IStartupDiagnosticsReportBuilder, StartupDiagnosticsReportBuilder>();
        services.TryAddSingleton<IPlcRuntimeTaskBinder, PlcRuntimeTaskBinder>();
        services.TryAddSingleton<IAppRuntimeStateCoordinator, AppRuntimeStateCoordinator>();
        services.TryAddSingleton<IAppLifecycleCoordinator, AppLifecycleManager>();
        services.TryAddSingleton<AppLifecycleManager>(sp =>
            (AppLifecycleManager)sp.GetRequiredService<IAppLifecycleCoordinator>());
    }

    private static string? ResolveMediatRLicenseKey(Microsoft.Extensions.Configuration.IConfiguration configuration)
        => FirstNonEmpty(
            Environment.GetEnvironmentVariable("MediatR__LicenseKey"),
            Environment.GetEnvironmentVariable("MEDIATR_LICENSE_KEY"),
            configuration["MediatR:LicenseKey"]);

    private static string? FirstNonEmpty(params string?[] values)
        => values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim();

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

        public string ApplicationName { get; set; } = "IIoT.Edge.Host";

        public string ContentRootPath { get; set; } = contentRootPath;

        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
