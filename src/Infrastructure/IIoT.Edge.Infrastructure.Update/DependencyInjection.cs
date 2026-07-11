using IIoT.Edge.Application.Abstractions.Updates;
using IIoT.Edge.Application.Features.Updates;
using IIoT.Edge.Infrastructure.CloudClient;
using IIoT.Edge.Infrastructure.Update.Cloud;
using IIoT.Edge.Infrastructure.Update.Configuration;
using IIoT.Edge.Infrastructure.Update.Host;
using IIoT.Edge.Infrastructure.Update.Packages;
using IIoT.Edge.Infrastructure.Update.Plugins;
using IIoT.Edge.Infrastructure.Update.Profiles;
using IIoT.Edge.SharedKernel.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace IIoT.Edge.Infrastructure.Update;

public static class DependencyInjection
{
    public static IServiceCollection AddEdgeUpdateInfrastructure(
        this IServiceCollection services,
        string baseDirectory)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(baseDirectory);

        var updateConfigPaths = new EdgeUpdateConfigPaths(
            EdgeClientProgramDataPaths.ResolveLauncherUpdateConfigPath(baseDirectory),
            Path.Combine(baseDirectory, FileEdgeUpdateConfigInitializer.SampleConfigFileName));

        services.TryAddSingleton<IEdgeVersionCompatibilityPolicy, EdgeVersionCompatibilityPolicy>();
        services.TryAddSingleton<IEdgeReleaseService, EdgeReleaseService>();
        services.AddSingleton(updateConfigPaths);
        services.AddSingleton<IEdgeUpdateConfigInitializer, FileEdgeUpdateConfigInitializer>();
        services.AddSingleton<IEdgeProfileCloudSwitchReader>(
            _ => new FileProfileCloudSwitchReader(baseDirectory));
        services.AddSingleton<IEdgeUpdateConfigurationProvider>(
            provider => new FileEdgeUpdateConfigurationProvider(
                baseDirectory,
                provider.GetRequiredService<IEdgeProfileCloudSwitchReader>()));
        services.TryAddSingleton(_ => new HttpClient(new SocketsHttpHandler
        {
            PooledConnectionLifetime = TimeSpan.FromMinutes(5)
        })
        {
            Timeout = Timeout.InfiniteTimeSpan
        });
        services.TryAddSingleton<ICloudClientHttpTransport>(
            provider => new CloudClientHttpTransport(provider.GetRequiredService<HttpClient>()));
        services.TryAddSingleton<IEdgeCloudDeviceBootstrapClient, EdgeCloudDeviceBootstrapClient>();
        services.AddSingleton<IEdgeUpdateDeviceSessionClient, HttpEdgeUpdateDeviceSessionClient>();
        services.AddSingleton<IEdgeUpdateCatalogClient, HttpEdgeUpdateCatalogClient>();
        services.AddSingleton<IEdgeVersionReporter, HttpEdgeVersionReporter>();
        services.AddSingleton<IEdgeRuntimeHeartbeatReporter, HttpEdgeRuntimeHeartbeatReporter>();
        services.AddSingleton<IEdgeInstalledPluginCatalog, FileInstalledPluginCatalog>();
        services.AddSingleton<IEdgeProfileModuleConfigurationStore, FileEdgeProfileModuleConfigurationStore>();
        services.AddSingleton<IEdgePluginPackageInstaller, EdgePluginPackageInstaller>();
        services.AddSingleton<IEdgeHostUpdateService>(_ => new VelopackHostUpdateService(baseDirectory));

        return services;
    }
}
