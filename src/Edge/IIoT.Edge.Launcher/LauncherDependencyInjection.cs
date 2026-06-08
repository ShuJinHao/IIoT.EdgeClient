using IIoT.Edge.Launcher.Services;
using IIoT.Edge.Launcher.ViewModels;
using IIoT.Edge.Infrastructure.CloudClient;
using IIoT.Edge.SharedKernel.Configuration;
using IIoT.Edge.UI.Shared.Localization;
using Microsoft.Extensions.DependencyInjection;

namespace IIoT.Edge.Launcher;

public static class LauncherDependencyInjection
{
    public static IServiceCollection AddLauncherServices(
        this IServiceCollection services,
        string baseDirectory)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(baseDirectory);

        var accountPaths = new LauncherAccountCatalogPaths(
            EdgeClientProgramDataPaths.ResolveLauncherAccountsPath(baseDirectory),
            Path.Combine(baseDirectory, LauncherAccountCatalog.SampleCatalogFileName));
        var updateConfigPaths = new LauncherUpdateConfigPaths(
            EdgeClientProgramDataPaths.ResolveLauncherUpdateConfigPath(baseDirectory),
            Path.Combine(baseDirectory, LauncherUpdateConfigInitializer.SampleConfigFileName));

        services.AddSingleton(accountPaths);
        services.AddSingleton(updateConfigPaths);
        services.AddSingleton<IAppLanguageService>(
            _ => new LauncherLanguageService(EdgeClientProgramDataPaths.ResolveLauncherLanguagePath(baseDirectory)));
        services.AddSingleton<ILauncherAccountCatalogInitializer>(
            provider => ActivatorUtilities.CreateInstance<LauncherAccountCatalogInitializer>(provider));
        services.AddSingleton<ILauncherUpdateConfigInitializer>(
            provider => ActivatorUtilities.CreateInstance<LauncherUpdateConfigInitializer>(provider));
        services.AddSingleton<ILauncherAccountCatalog>(
            provider => ActivatorUtilities.CreateInstance<LauncherAccountCatalog>(provider));
        services.AddSingleton<ILocalLauncherAuthService, LocalLauncherAuthService>();
        services.AddSingleton<ILauncherProfileCatalog>(
            provider => ActivatorUtilities.CreateInstance<LauncherProfileCatalog>(provider, baseDirectory));
        services.AddSingleton<ILauncherUpdateService, LauncherUpdateService>();
        services.AddSingleton<ILauncherCloudApiConfigurationResolver>(
            _ => new LauncherCloudApiConfigurationResolver(baseDirectory));
        services.AddSingleton(_ => new HttpClient(new SocketsHttpHandler
        {
            PooledConnectionLifetime = TimeSpan.FromMinutes(5)
        })
        {
            Timeout = Timeout.InfiniteTimeSpan
        });
        services.AddSingleton<ICloudClientHttpTransport>(
            provider => new CloudClientHttpTransport(provider.GetRequiredService<HttpClient>()));
        services.AddSingleton<IEdgeCloudDeviceBootstrapClient, EdgeCloudDeviceBootstrapClient>();
        services.AddSingleton<ILauncherEdgeReleaseCloudClient, LauncherEdgeReleaseCloudClient>();
        services.AddSingleton<ILauncherInstalledPluginCatalog, LauncherInstalledPluginCatalog>();
        services.AddSingleton<ILauncherProfileModuleConfiguration, LauncherProfileModuleConfiguration>();
        services.AddSingleton<ILauncherPluginPackageInstaller>(
            provider => new LauncherPluginPackageInstaller(provider.GetRequiredService<HttpClient>()));
        services.AddSingleton<ILauncherClientReleaseService, LauncherClientReleaseService>();
        services.AddSingleton<IProcessStarter, ProcessStarter>();
        services.AddSingleton<IShellLaunchService, ShellLaunchService>();
        services.AddSingleton<LauncherMainViewModel>();
        services.AddSingleton<MainWindow>();

        return services;
    }
}
