using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using IIoT.Edge.Application.Abstractions.Config;
using IIoT.Edge.AvaloniaShell.Services;
using IIoT.Edge.AvaloniaShell.ViewModels;
using IIoT.Edge.AvaloniaShell.Views;
using IIoT.Edge.Host.Bootstrap;
using IIoT.Edge.UI.Avalonia.Localization;
using IIoT.Edge.UI.Avalonia.Services;
using Microsoft.Extensions.DependencyInjection;

namespace IIoT.Edge.AvaloniaShell;

public partial class App : Avalonia.Application
{
    private static readonly TimeSpan ShutdownTimeout = TimeSpan.FromSeconds(8);

    private ServiceProvider? _serviceProvider;
    private ServiceProvider? _startupServiceProvider;
    private IAvaloniaShellStartupCoordinator? _startupCoordinator;
    private readonly CancellationTokenSource _appCts = new();
    private bool _shutdownRequested;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override async void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            _startupServiceProvider = new ServiceCollection()
                .AddAvaloniaShellStartupServices()
                .BuildServiceProvider();
            var bootstrapOptions = _startupServiceProvider
                .GetRequiredService<IAvaloniaShellBootstrapOptionsFactory>()
                .Create(AppDomain.CurrentDomain.BaseDirectory);

            var services = new ServiceCollection()
                .AddEdgeHostAvaloniaBootstrap(bootstrapOptions)
                .AddSingleton<IAvaloniaShellStartupCoordinator, AvaloniaShellStartupCoordinator>()
                .AddSingleton<MainWindowViewModel>()
                .AddSingleton<MainWindow>()
                .BuildServiceProvider();
            _serviceProvider = services;

            var themeService = services.GetRequiredService<IAvaloniaThemeService>();
            themeService.Apply();

            var languageService = services.GetRequiredService<IAvaloniaLanguageService>();
            languageService.Apply(languageService.CultureName);

            var mainWindow = services.GetRequiredService<MainWindow>();
            mainWindow.DataContext = services.GetRequiredService<MainWindowViewModel>();
            desktop.MainWindow = mainWindow;
            desktop.ShutdownRequested += Desktop_ShutdownRequested;

            base.OnFrameworkInitializationCompleted();

            _startupCoordinator = services.GetRequiredService<IAvaloniaShellStartupCoordinator>();
            var startupResult = await _startupCoordinator.StartAsync(desktop.Args, _appCts.Token);
            if (!startupResult.Success)
            {
                await StartupErrorWindow.ShowStartupFailureAsync(
                    mainWindow,
                    startupResult.Message ?? "AvaloniaShell 启动失败。",
                    startupResult.DiagnosticsSummary,
                    startupResult.DiagnosticsLogPath);
                desktop.Shutdown(-1);
            }

            return;
        }

        base.OnFrameworkInitializationCompleted();
    }

    private async void Desktop_ShutdownRequested(object? sender, ShutdownRequestedEventArgs e)
    {
        if (_shutdownRequested)
        {
            return;
        }

        _shutdownRequested = true;
        e.Cancel = true;
        _appCts.Cancel();

        if (_startupCoordinator is not null)
        {
            await _startupCoordinator.StopAsync(ShutdownTimeout);
        }

        _serviceProvider?.Dispose();
        _serviceProvider = null;
        _startupServiceProvider?.Dispose();
        _startupServiceProvider = null;

        if (sender is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.Shutdown();
        }
    }

}
