using IIoT.Edge.Module.Contracts.Updates;
using IIoT.Edge.Infrastructure.Update;
using IIoT.Edge.Launcher.Services;
using IIoT.Edge.Launcher.ViewModels;
using IIoT.Edge.SharedKernel.Configuration;
using IIoT.Edge.SharedKernel.Security;
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
            Path.Combine(baseDirectory, LauncherAccountCatalog.SampleCatalogFileName),
            EdgeClientProgramDataPaths.ResolveHostDatabasePath(baseDirectory));

        services.AddSingleton(accountPaths);
        services.AddEdgeUpdateInfrastructure(baseDirectory);
        services.AddSingleton<IAppLanguageService>(
            _ => new LauncherLanguageService(EdgeClientProgramDataPaths.ResolveLauncherLanguagePath(baseDirectory)));
        services.AddSingleton<ILauncherAccountCatalogInitializer>(
            provider => ActivatorUtilities.CreateInstance<LauncherAccountCatalogInitializer>(provider));
        services.AddSingleton<ILauncherAccountCatalog>(
            provider => ActivatorUtilities.CreateInstance<LauncherAccountCatalog>(provider));
        services.AddSingleton<ILocalLauncherAuthService, LocalLauncherAuthService>();
        services.AddSingleton<LauncherStartupDiagnosticStore>();
        services.AddSingleton<ILauncherStartupDiagnosticReader>(provider =>
            provider.GetRequiredService<LauncherStartupDiagnosticStore>());
        services.AddSingleton<ILauncherStartupDiagnosticWriter>(provider =>
            provider.GetRequiredService<LauncherStartupDiagnosticStore>());
        services.AddSingleton<ILauncherEnabledPluginSelectionSource>(provider =>
            new LauncherEnabledPluginSelectionSource(
                baseDirectory,
                provider.GetRequiredService<ILauncherStartupDiagnosticWriter>()));
        services.AddSingleton<ILauncherPluginActivationSource>(
            provider => new LauncherPluginActivationSource(
                baseDirectory,
                provider.GetRequiredService<ILauncherEnabledPluginSelectionSource>(),
                provider.GetRequiredService<ILauncherStartupDiagnosticWriter>()));
        services.AddSingleton<ILauncherPluginActivationReconciler>(
            provider => new LauncherPluginActivationReconciler(
                baseDirectory,
                provider.GetRequiredService<ILauncherPluginActivationSource>(),
                provider.GetRequiredService<ILauncherStartupDiagnosticWriter>()));
        services.AddSingleton<ILauncherProfileCatalog>(
            provider => new LauncherProfileCatalog(
                baseDirectory,
                activationSource: provider.GetRequiredService<ILauncherPluginActivationSource>(),
                activationReconciler: provider.GetRequiredService<ILauncherPluginActivationReconciler>()));
        services.AddSingleton<ILauncherUpdateTargetFactory, LauncherUpdateTargetFactory>();
        services.AddSingleton<ILauncherProfileVisibilityService>(
            provider => new LauncherProfileVisibilityService(
                baseDirectory,
                provider.GetRequiredService<IEdgeProfileModuleConfigurationStore>(),
                provider.GetRequiredService<ILauncherUpdateTargetFactory>(),
                provider.GetRequiredService<ILauncherEnabledPluginSelectionSource>()));
        services.AddSingleton<ILauncherDeviceBindingImporter>(
            provider => new LauncherDeviceBindingImporter(
                baseDirectory,
                provider.GetRequiredService<ILauncherProfileCatalog>(),
                provider.GetRequiredService<IEdgeProfileModuleConfigurationStore>(),
                provider.GetRequiredService<ILauncherUpdateTargetFactory>(),
                provider.GetRequiredService<ILauncherStartupDiagnosticWriter>(),
                provider.GetRequiredService<IEdgeCredentialStore>()));
        services.AddSingleton<IEdgeCredentialOwnerSidProvider, WindowsCredentialOwnerSidProvider>();
        services.AddSingleton<ILauncherLegacyCredentialMigrator>(provider =>
            new LauncherLegacyCredentialMigrator(
                baseDirectory,
                provider.GetRequiredService<IEdgeCredentialStore>()));
        services.AddSingleton<ILauncherRuntimePreflight>(provider =>
            new LauncherRuntimePreflight(
                baseDirectory,
                provider.GetRequiredService<ILauncherProfileCatalog>(),
                provider.GetRequiredService<IEdgeCredentialStore>(),
                provider.GetRequiredService<IEdgeCredentialOwnerSidProvider>()));
        services.AddSingleton<IProcessStarter, ProcessStarter>();
        services.AddSingleton<IShellInstanceIdResolver, ShellInstanceIdResolver>();
        services.AddSingleton<IShellInstanceProbe, NamedMutexShellInstanceProbe>();
        services.AddSingleton<ILauncherUpdateOperationGate>(
            _ => new FileLauncherUpdateOperationGate(baseDirectory));
        services.AddSingleton<IShellLaunchService, ShellLaunchService>();
        services.AddSingleton<ILauncherStartupCoordinator, LauncherStartupCoordinator>();
        services.AddSingleton<LauncherMainViewModel>();
        services.AddSingleton<MainWindow>();

        return services;
    }
}
