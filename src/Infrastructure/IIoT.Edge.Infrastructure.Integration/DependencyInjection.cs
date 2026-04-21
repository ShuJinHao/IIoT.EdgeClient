using IIoT.Edge.Application.Abstractions.Auth;
using IIoT.Edge.Application.Abstractions.DataPipeline;
using IIoT.Edge.Application.Abstractions.DataPipeline.Consumers;
using IIoT.Edge.Application.Abstractions.DataPipeline.SyncTask;
using IIoT.Edge.Application.Abstractions.Device;
using IIoT.Edge.Application.Abstractions.Logging;
using IIoT.Edge.Application.Abstractions.Modules;
using IIoT.Edge.Application.Abstractions.Recipe;
using IIoT.Edge.Infrastructure.Integration.Auth;
using IIoT.Edge.Infrastructure.Integration.Capacity;
using IIoT.Edge.Infrastructure.Integration.Config;
using IIoT.Edge.Infrastructure.Integration.Device;
using IIoT.Edge.Infrastructure.Integration.Device.Cache;
using IIoT.Edge.Infrastructure.Integration.DeviceLog;
using IIoT.Edge.Infrastructure.Integration.Export.Excel;
using IIoT.Edge.Infrastructure.Integration.Http;
using IIoT.Edge.Infrastructure.Integration.Mes;
using IIoT.Edge.Infrastructure.Integration.PassStation;
using IIoT.Edge.Infrastructure.Integration.Recipe;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;
using Polly;
using System.Threading;

namespace IIoT.Edge.Infrastructure.Integration;

public static class DependencyInjection
{
    private static readonly TimeSpan CloudRetryDelay = TimeSpan.FromMilliseconds(500);

    public static IServiceCollection AddIntegrationInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration,
        string excelDirectory)
    {
        services.Configure<CloudApiConfig>(configuration.GetSection("CloudApi"));
        services.Configure<MesApiConfig>(configuration.GetSection("MesApi"));

        var timeoutSecs = configuration.GetValue<int?>("CloudApi:TimeoutSecs") ?? 3;
        var timeout = TimeSpan.FromSeconds(timeoutSecs);
        var mesTimeoutSecs = configuration.GetValue<int?>("MesApi:TimeoutSecs") ?? 3;
        var mesTimeout = TimeSpan.FromSeconds(mesTimeoutSecs);

        services.AddSingleton<ICloudApiEndpointProvider, CloudApiEndpointProvider>();
        services.AddSingleton<ICloudApiPathProvider>(sp =>
            sp.GetRequiredService<ICloudApiEndpointProvider>());
        services.AddSingleton<IMesEndpointProvider, MesEndpointProvider>();
        services.AddSingleton<DeviceSessionFileCacheStore>();

        services.AddSingleton(new LocalAdminConfig
        {
            PasswordHash = Environment.GetEnvironmentVariable("LocalAdmin__PasswordHash")?.Trim() ?? string.Empty
        });

        services.AddHttpClient<AuthService>(client => client.Timeout = timeout);
        services.AddSingleton<IAuthService>(sp => sp.GetRequiredService<AuthService>());

        services.AddHttpClient<DeviceService>(client => client.Timeout = timeout);
        services.AddSingleton<IDeviceService>(sp => sp.GetRequiredService<DeviceService>());
        services.AddSingleton<IDeviceAccessTokenProvider>(sp => sp.GetRequiredService<DeviceService>());

        services.AddHttpClient("CloudApi", client => client.Timeout = Timeout.InfiniteTimeSpan)
            .AddResilienceHandler("cloud-transient", builder =>
            {
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
        services.AddSingleton<ICloudHttpClient, CloudHttpClient>();
        services.AddHttpClient("MesApi", client => client.Timeout = mesTimeout);
        services.AddSingleton<IMesHttpClient, MesHttpClient>();

        services.AddSingleton<ICloudConsumer, CloudConsumer>();
        services.AddSingleton<ICloudBatchConsumer>(sp =>
            (ICloudBatchConsumer)sp.GetRequiredService<ICloudConsumer>());
        services.AddSingleton<IMesConsumer, MesConsumer>();
        services.AddSingleton<ICapacityConsumer, CapacityConsumer>();
        services.AddSingleton<ICapacitySyncTask, CapacitySyncTask>();
        services.AddSingleton<IDeviceLogSyncTask, DeviceLogSyncTask>();
        services.AddSingleton<IRecipeService, RecipeService>();
        services.AddSingleton<RecipeSyncTask>();

        services.AddSingleton<IExcelConsumer>(sp =>
            new ExcelConsumer(excelDirectory, sp.GetRequiredService<ILogService>()));
        services.AddSingleton<ICellDataConsumer>(sp =>
            sp.GetRequiredService<IExcelConsumer>());

        return services;
    }
}
