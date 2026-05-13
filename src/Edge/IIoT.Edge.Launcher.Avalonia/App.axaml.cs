using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using IIoT.Edge.Launcher.Services;
using IIoT.Edge.Launcher.Avalonia.Views;
using Microsoft.Extensions.DependencyInjection;

namespace IIoT.Edge.Launcher.Avalonia;

public sealed partial class App : global::Avalonia.Application
{
    private ServiceProvider? _serviceProvider;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            _serviceProvider = ConfigureServices(AppContext.BaseDirectory).BuildServiceProvider();
            _serviceProvider.GetRequiredService<ILauncherAccountCatalogInitializer>().EnsureCatalogExists();

            var mainWindow = _serviceProvider.GetRequiredService<MainWindow>();
            desktop.MainWindow = mainWindow;
            desktop.Exit += OnDesktopExit;
        }

        base.OnFrameworkInitializationCompleted();
    }

    private void OnDesktopExit(object? sender, ControlledApplicationLifetimeExitEventArgs e)
    {
        _serviceProvider?.Dispose();
        _serviceProvider = null;
    }

    private static IServiceCollection ConfigureServices(string baseDirectory)
    {
        var services = new ServiceCollection();
        services.AddLauncherCore(baseDirectory);
        services.AddSingleton<MainWindow>();
        return services;
    }
}
