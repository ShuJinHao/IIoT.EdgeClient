using IIoT.Edge.Application.Abstractions.Updates;
using IIoT.Edge.Infrastructure.Update;
using IIoT.Edge.Launcher.Services;
using IIoT.Edge.Launcher.ViewModels;
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

        services.AddSingleton(accountPaths);
        services.AddEdgeUpdateInfrastructure(baseDirectory);
        services.AddSingleton<IAppLanguageService>(
            _ => new LauncherLanguageService(EdgeClientProgramDataPaths.ResolveLauncherLanguagePath(baseDirectory)));
        services.AddSingleton<ILauncherAccountCatalogInitializer>(
            provider => ActivatorUtilities.CreateInstance<LauncherAccountCatalogInitializer>(provider));
        services.AddSingleton<ILauncherAccountCatalog>(
            provider => ActivatorUtilities.CreateInstance<LauncherAccountCatalog>(provider));
        services.AddSingleton<ILocalLauncherAuthService, LocalLauncherAuthService>();
        services.AddSingleton<ILauncherProfileCatalog>(
            provider => ActivatorUtilities.CreateInstance<LauncherProfileCatalog>(provider, baseDirectory));
        services.AddSingleton<ILauncherUpdateTargetFactory, LauncherUpdateTargetFactory>();
        services.AddSingleton<ILauncherDeviceBindingImporter>(
            provider => new LauncherDeviceBindingImporter(
                baseDirectory,
                provider.GetRequiredService<ILauncherProfileCatalog>(),
                provider.GetRequiredService<IEdgeProfileModuleConfigurationStore>(),
                provider.GetRequiredService<ILauncherUpdateTargetFactory>()));
        services.AddSingleton<IProcessStarter, ProcessStarter>();
        services.AddSingleton<IShellLaunchService, ShellLaunchService>();
        services.AddSingleton<LauncherMainViewModel>();
        services.AddSingleton<MainWindow>();

        return services;
    }
}
