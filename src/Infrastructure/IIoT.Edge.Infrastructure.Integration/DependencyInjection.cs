using IIoT.Edge.Module.Contracts.Auth;
using IIoT.Edge.Module.Contracts.Config;
using IIoT.Edge.Module.Contracts.DataPipeline;
using IIoT.Edge.Module.Contracts.DataPipeline.Consumers;
using IIoT.Edge.Module.Contracts.Cloud;
using IIoT.Edge.Module.Contracts.Device;
using IIoT.Edge.Module.Contracts.Logging;
using IIoT.Edge.Module.Contracts.Modules;
using IIoT.Edge.Module.Contracts.Plc;
using IIoT.Edge.Module.Contracts.Recipe;
using IIoT.Edge.Module.Contracts.Time;
using IIoT.Edge.Infrastructure.Integration.Auth;
using IIoT.Edge.Infrastructure.Integration.Capacity;
using IIoT.Edge.Infrastructure.Integration.Config;
using IIoT.Edge.Infrastructure.Integration.Device;
using IIoT.Edge.Infrastructure.Integration.Device.Cache;
using IIoT.Edge.Infrastructure.Integration.DeviceLog;
using IIoT.Edge.Infrastructure.Integration.EdgeHost;
using IIoT.Edge.Infrastructure.Integration.Http;
using IIoT.Edge.Infrastructure.Integration.Mes;
using IIoT.Edge.Infrastructure.Integration.PassStation;
using IIoT.Edge.Infrastructure.Integration.Recipe;
using IIoT.Edge.Application.Common.Plc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Options;
using Polly;
using System.Threading;

using IIoT.Edge.Module.Contracts.Mes;
using IIoT.Edge.SharedKernel.Security;
namespace IIoT.Edge.Infrastructure.Integration;

public static class DependencyInjection
{
    private static readonly TimeSpan CloudRetryDelay = TimeSpan.FromMilliseconds(500);

    public static IServiceCollection AddIntegrationInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration,
        EdgeRuntimePaths runtimePaths)
    {
        services.Configure<CloudApiConfig>(configuration.GetSection("CloudApi"));
        services.Configure<MesApiConfig>(configuration.GetSection("MesApi"));

        var timeoutSecs = configuration.GetValue<int?>("CloudApi:TimeoutSecs") ?? 3;
        var timeout = TimeSpan.FromSeconds(timeoutSecs);
        var mesTimeoutSecs = configuration.GetValue<int?>("MesApi:TimeoutSecs") ?? 3;
        var mesTimeout = TimeSpan.FromSeconds(mesTimeoutSecs);

        services.AddSingleton<ICloudApiConfigSnapshotProvider, CloudApiConfigSnapshotProvider>();
        services.AddSingleton<ICloudProfileSwitchProjectionWriter, FileCloudProfileSwitchProjectionWriter>();
        services.AddSingleton<ICloudApiEndpointProvider>(sp => new CloudApiEndpointProvider(
            sp.GetRequiredService<IOptionsMonitor<CloudApiConfig>>(),
            sp.GetService<ILocalSystemConfigSnapshotReader>()));
        services.AddSingleton<ICloudApiPathProvider>(sp =>
            sp.GetRequiredService<ICloudApiEndpointProvider>());
        services.AddSingleton<IMesEndpointProvider, MesEndpointProvider>();
        services.TryAddSingleton<IEdgeCredentialStore, WindowsCredentialManagerStore>();
        services.AddSingleton(sp => new DeviceSessionFileCacheStore(
            runtimePaths.DeviceCacheFilePath,
            sp.GetRequiredService<IEdgeCredentialStore>()));
        services.AddSingleton<IDeviceSessionCacheStore>(sp =>
            sp.GetRequiredService<DeviceSessionFileCacheStore>());
        services.AddSingleton<IDeviceSessionCacheCoordinator, DeviceSessionCacheCoordinator>();

        services.AddSingleton(new LocalAdminConfig
        {
            PasswordHash =
                Environment.GetEnvironmentVariable("LocalAdmin__PasswordHash")?.Trim()
                ?? configuration["LocalAdmin:PasswordHash"]?.Trim()
                ?? string.Empty
        });
        services.AddSingleton<ILocalAdminCredentialStore>(_ =>
            new FileLocalAdminCredentialStore(Path.Combine(
                runtimePaths.RuntimeDataRoot,
                "security",
                "local-admin.json")));
        services.AddTransient<CloudExecutionPolicyHandler>();

        services.AddHttpClient(AuthService.HttpClientName, client => client.Timeout = timeout)
            .AddHttpMessageHandler<CloudExecutionPolicyHandler>();
        services.AddSingleton<IAuthService, AuthService>();

        services.AddHttpClient(DeviceService.HttpClientName, client => client.Timeout = timeout)
            .AddHttpMessageHandler<CloudExecutionPolicyHandler>();
        services.AddSingleton<ICloudDeviceBootstrapClient, CloudDeviceBootstrapClient>();
        services.AddSingleton<IDeviceUploadGatePolicy, DeviceUploadGatePolicy>();
        services.AddSingleton<IDeviceBootstrapEventLogger, DeviceBootstrapEventLogger>();
        services.AddSingleton<ICloudDeviceActivationClient, CloudDeviceActivationClient>();
        services.AddSingleton<IDeviceActivationStateStore>(sp =>
            new RuntimeBindingActivationStateStore(
                runtimePaths.BaseDirectory,
                sp.GetRequiredService<IEdgeCredentialStore>()));
        services.AddSingleton<DeviceService>();
        services.AddSingleton<IDeviceService>(sp => sp.GetRequiredService<DeviceService>());
        services.AddSingleton<IDeviceAccessTokenProvider>(sp => sp.GetRequiredService<DeviceService>());
        services.AddSingleton<IDeviceActivationCoordinator>(sp => sp.GetRequiredService<DeviceService>());

        services.AddHttpClient("CloudApi", client => client.Timeout = Timeout.InfiniteTimeSpan)
            .AddHttpMessageHandler<CloudExecutionPolicyHandler>()
            .AddResilienceHandler("cloud-transient", (builder, context) =>
            {
                builder.TimeProvider = context.ServiceProvider.GetService<TimeProvider>();
                var retryOptions = new HttpRetryStrategyOptions
                {
                    MaxRetryAttempts = 3,
                    BackoffType = DelayBackoffType.Exponential,
                    UseJitter = true,
                    Delay = CloudRetryDelay,
                    ShouldRetryAfterHeader = true
                };
                retryOptions.DisableForUnsafeHttpMethods();
                builder.AddRetry(retryOptions);
                builder.AddTimeout(timeout);
            });
        services.AddSingleton<ICloudPayloadSanitizer, CloudPayloadSanitizer>();
        services.AddSingleton<ICloudHttpClient>(sp =>
            new CloudHttpClient(
                sp.GetRequiredService<IHttpClientFactory>(),
                sp.GetRequiredService<IDeviceAccessTokenProvider>(),
                sp.GetRequiredService<IDeviceService>(),
                sp.GetRequiredService<ICloudApiEndpointProvider>(),
                sp.GetRequiredService<ILogService>(),
                sp.GetRequiredService<ICloudPayloadSanitizer>()));
        services.AddHttpClient("MesApi", client => client.Timeout = mesTimeout);
        services.AddSingleton<IMesHttpClient, MesHttpClient>();
        services.AddSingleton<IMesHeartbeatProbe, MesHeartbeatProbe>();
        services.AddSingleton<ICloudUploadGate, CloudUploadGate>();
        services.AddSingleton<IMesUploadGate, MesUploadGate>();
        services.AddSingleton<MesHeartbeatTask>();
        services.AddSingleton<IPlcConfigurationVersionStore>(_ =>
            new FilePlcConfigurationVersionStore(runtimePaths.BaseDirectory));
        services.AddSingleton<EdgeHostPlcRuntimeStateSnapshotProvider>();
        services.AddSingleton<IEdgeHostPlcRuntimeStateSnapshotProvider>(sp =>
            sp.GetRequiredService<EdgeHostPlcRuntimeStateSnapshotProvider>());
        services.AddSingleton<IAuthoritativePlcSnapshotProvider>(sp =>
            sp.GetRequiredService<EdgeHostPlcRuntimeStateSnapshotProvider>());
        services.AddSingleton<IPlcConfigurationSnapshotInvalidator>(sp =>
            sp.GetRequiredService<EdgeHostPlcRuntimeStateSnapshotProvider>());
        services.AddSingleton<IEdgeHostPlcRuntimeStateReporter, EdgeHostPlcRuntimeStateReporter>();
        services.AddSingleton<EdgeHostPlcRuntimeStateReportTask>();

        services.AddSingleton<StandardPassStationCloudUploader>();
        services.AddSingleton<ICloudConsumer, CloudConsumer>();
        services.AddSingleton<ICloudBatchConsumer>(sp =>
            (ICloudBatchConsumer)sp.GetRequiredService<ICloudConsumer>());
        services.AddSingleton<IMesConsumer, MesConsumer>();
        services.AddSingleton<ICapacityConsumer, CapacityConsumer>();
        services.AddSingleton<ICapacitySyncTask, CapacitySyncTask>();
        services.AddSingleton<IDeviceLogSyncTask, DeviceLogSyncTask>();
        services.AddSingleton<IRecipeService>(sp =>
            new RecipeService(
                sp.GetRequiredService<ICloudHttpClient>(),
                sp.GetRequiredService<ICloudApiEndpointProvider>(),
                sp.GetRequiredService<IDeviceService>(),
                sp.GetRequiredService<ILogService>(),
                runtimePaths.RecipeDirectory));
        services.AddSingleton<RecipeSyncTask>();

        // 生产完成事实不再注册 Excel 长期历史消费者。既有 Excel 读取/对账类型保留，
        // 仅供用户另行授权的历史核对工具使用。

        return services;
    }
}
