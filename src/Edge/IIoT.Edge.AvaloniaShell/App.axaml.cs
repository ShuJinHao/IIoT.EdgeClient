using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using IIoT.Edge.Application.Abstractions.Config;
using IIoT.Edge.AvaloniaShell.ViewModels;
using IIoT.Edge.AvaloniaShell.Views;
using IIoT.Edge.Host.Bootstrap.Avalonia;
using IIoT.Edge.UI.Avalonia.Localization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using System.IO;

namespace IIoT.Edge.AvaloniaShell;

public partial class App : Avalonia.Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var services = new ServiceCollection()
                .AddEdgeHostAvaloniaBootstrap(CreateBootstrapOptions(AppDomain.CurrentDomain.BaseDirectory))
                .AddSingleton<MainWindowViewModel>()
                .AddSingleton<MainWindow>()
                .BuildServiceProvider();
            DependencyInjection.RegisterAvaloniaViews(services);

            var languageService = services.GetRequiredService<IAvaloniaLanguageService>();
            languageService.Apply("zh-CN");

            var mainWindow = services.GetRequiredService<MainWindow>();
            mainWindow.DataContext = services.GetRequiredService<MainWindowViewModel>();
            desktop.MainWindow = mainWindow;
        }

        base.OnFrameworkInitializationCompleted();
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
            [new IIoT.Edge.Module.Homogenization.Avalonia.DependencyInjection()]);
    }
}
