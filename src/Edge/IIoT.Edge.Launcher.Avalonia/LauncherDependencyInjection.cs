using IIoT.Edge.Launcher.Services;
using IIoT.Edge.Launcher.ViewModels;
using IIoT.Edge.UI.Avalonia.Localization;
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

        services.AddSingleton<ILauncherAccountCatalogInitializer>(
            provider => ActivatorUtilities.CreateInstance<LauncherAccountCatalogInitializer>(provider, baseDirectory));
        services.AddSingleton<ILauncherAccountCatalog>(
            provider => ActivatorUtilities.CreateInstance<LauncherAccountCatalog>(provider, baseDirectory));
        services.AddSingleton<ILauncherProfileCatalog>(
            provider => ActivatorUtilities.CreateInstance<LauncherProfileCatalog>(provider, baseDirectory));
        services.AddSingleton<IPasswordHasher, Pbkdf2PasswordHasher>();
        services.AddSingleton<ILocalLauncherAuthService, LocalLauncherAuthService>();
        services.AddSingleton<IProcessStarter, ProcessStarter>();
        services.AddSingleton<IShellLaunchService, ShellLaunchService>();
        services.AddSingleton<IAvaloniaXamlStringResourceLoader, AvaloniaXamlStringResourceLoader>();
        services.AddSingleton<IAvaloniaLanguageService>(provider =>
        {
            var loader = provider.GetRequiredService<IAvaloniaXamlStringResourceLoader>();
            var resources = loader.Load([
                typeof(LauncherDependencyInjection).Assembly,
                typeof(IAvaloniaLanguageService).Assembly
            ]);
            return new AvaloniaResourceLanguageService(
                resources,
                defaultCulture: "zh-CN",
                toggleResourceKey: "Launcher_Action_Language");
        });
        services.AddSingleton<LauncherLoginViewModel>();
        services.AddSingleton<LauncherProfileViewModel>();
        services.AddSingleton<LauncherMainViewModel>();

        return services;
    }
}
