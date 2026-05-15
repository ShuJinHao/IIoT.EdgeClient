using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using IIoT.Edge.Application.Abstractions.Config;
using IIoT.Edge.AvaloniaShell.Services;
using IIoT.Edge.AvaloniaShell.ViewModels;
using IIoT.Edge.AvaloniaShell.Views;
using IIoT.Edge.Host.Bootstrap;
using IIoT.Edge.UI.Avalonia.Localization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using System.IO;

namespace IIoT.Edge.AvaloniaShell;

public partial class App : Avalonia.Application
{
    private static readonly TimeSpan ShutdownTimeout = TimeSpan.FromSeconds(8);

    private ServiceProvider? _serviceProvider;
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
            var services = new ServiceCollection()
                .AddEdgeHostAvaloniaBootstrap(CreateBootstrapOptions(AppDomain.CurrentDomain.BaseDirectory))
                .AddSingleton<IAvaloniaShellStartupCoordinator, AvaloniaShellStartupCoordinator>()
                .AddSingleton<MainWindowViewModel>()
                .AddSingleton<MainWindow>()
                .BuildServiceProvider();
            _serviceProvider = services;
            DependencyInjection.RegisterAvaloniaViews(services);

            var languageService = services.GetRequiredService<IAvaloniaLanguageService>();
            languageService.Apply("zh-CN");

            var mainWindow = services.GetRequiredService<MainWindow>();
            mainWindow.DataContext = services.GetRequiredService<MainWindowViewModel>();
            desktop.MainWindow = mainWindow;
            desktop.ShutdownRequested += Desktop_ShutdownRequested;

            base.OnFrameworkInitializationCompleted();

            _startupCoordinator = services.GetRequiredService<IAvaloniaShellStartupCoordinator>();
            var startupResult = await _startupCoordinator.StartAsync(desktop.Args, _appCts.Token);
            if (!startupResult.Success)
            {
                await ShowStartupErrorAsync(
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

        if (sender is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.Shutdown();
        }
    }

    private static async Task ShowStartupErrorAsync(
        MainWindow owner,
        string message,
        string? diagnosticsSummary,
        string? diagnosticsLogPath)
    {
        var dialog = new StartupErrorWindow(message, diagnosticsSummary, diagnosticsLogPath);
        await dialog.ShowDialog(owner);
    }

    private static AvaloniaHostBootstrapOptions CreateBootstrapOptions(string baseDirectory)
    {
        var configuration = new ConfigurationBuilder()
            .SetBasePath(baseDirectory)
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Shell:Environment"] = "AvaloniaMigration",
                ["LocalAdmin:PasswordHash"] = string.Empty,
                ["CloudApi:BaseUrl"] = "http://127.0.0.1",
                ["MesApi:BaseUrl"] = "http://127.0.0.1"
            })
            .Build();

        var runtimeRoot = Path.Combine(baseDirectory, "data", "avalonia-migration");
        var diagnosticsDirectory = Path.Combine(runtimeRoot, "diagnostics");
        var runtimePaths = new EdgeRuntimePaths(
            BaseDirectory: baseDirectory,
            ProfileName: "AvaloniaMigration",
            RuntimeDataRoot: runtimeRoot,
            DatabaseDirectory: Path.Combine(runtimeRoot, "db"),
            ContextDirectory: Path.Combine(runtimeRoot, "context"),
            RecipeDirectory: Path.Combine(runtimeRoot, "recipe"),
            ExcelDirectory: Path.Combine(runtimeRoot, "excel"),
            DiagnosticsDirectory: diagnosticsDirectory,
            LogDirectory: Path.Combine(diagnosticsDirectory, "logs"),
            DeviceCacheFilePath: Path.Combine(runtimeRoot, "device_cache.json"),
            PrimaryCrashLogPath: Path.Combine(diagnosticsDirectory, "crash.log"),
            FallbackCrashLogPath: Path.Combine(diagnosticsDirectory, "crash.fallback.log"));

        return new AvaloniaHostBootstrapOptions(
            configuration,
            runtimePaths,
            "AvaloniaMigration",
            ["Homogenization"],
            PluginDirectories: [baseDirectory]);
    }
}
