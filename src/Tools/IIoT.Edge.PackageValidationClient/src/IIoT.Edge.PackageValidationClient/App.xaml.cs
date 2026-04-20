using IIoT.Edge.Host.Bootstrap;
using IIoT.Edge.PackageValidationClient.Modules;
using IIoT.Edge.PackageValidationClient.ViewModels;
using IIoT.Edge.Shell.Core;
using IIoT.Edge.UI.Shared.Modularity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using WpfApplication = System.Windows.Application;

namespace IIoT.Edge.PackageValidationClient;

public partial class App : WpfApplication
{
    private ServiceProvider? _serviceProvider;
    private readonly CancellationTokenSource _appCts = new();
    private Mutex? _instanceMutex;
    private int _fatalDialogShown;

    public App()
    {
        _ = typeof(MaterialDesignThemes.Wpf.BundledTheme).Assembly;
        RegisterGlobalExceptionHandlers();
    }

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var configurationResult = ShellConfigurationLoader.Load(AppDomain.CurrentDomain.BaseDirectory);
        var configuration = configurationResult.Configuration;

        if (!TryAcquireInstanceLock(configuration))
        {
            Shutdown();
            return;
        }

        try
        {
            _serviceProvider = ConfigureServices(configuration).BuildServiceProvider();
        }
        catch (Exception ex)
        {
            ShowStartupError($"Startup service configuration failed: {ex.Message}");
            Shutdown(-1);
            return;
        }

        var lifecycle = _serviceProvider.GetRequiredService<IAppLifecycleCoordinator>();
        var startupResult = await lifecycle.StartAsync(_appCts.Token);
        if (!startupResult.Success)
        {
            ShowStartupError(startupResult.Message ?? "Application startup failed.");
            Shutdown(-1);
            return;
        }

        var mainWindow = _serviceProvider.GetRequiredService<MainWindow>();
        mainWindow.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        try
        {
            _appCts.Cancel();

            if (_serviceProvider is not null)
            {
                var lifecycle = _serviceProvider.GetRequiredService<IAppLifecycleCoordinator>();
                using var shutdownCts = new CancellationTokenSource(TimeSpan.FromSeconds(8));
                lifecycle.StopAsync(shutdownCts.Token).GetAwaiter().GetResult();
                _serviceProvider.Dispose();
                _serviceProvider = null;
            }
        }
        catch (Exception ex)
        {
            CrashLogWriter.Write("Application shutdown failed.", ex);
        }
        finally
        {
            ReleaseMutex();
            _appCts.Dispose();
            base.OnExit(e);
        }
    }

    private void RegisterGlobalExceptionHandlers()
    {
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnAppDomainUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        HandleFatalException("DispatcherUnhandledException", e.Exception, requestShutdown: true);
        e.Handled = true;
    }

    private void OnAppDomainUnhandledException(object? sender, UnhandledExceptionEventArgs e)
    {
        var exception = e.ExceptionObject as Exception
            ?? new Exception(e.ExceptionObject?.ToString() ?? "Unknown unhandled exception.");
        HandleFatalException("AppDomain.CurrentDomain.UnhandledException", exception, requestShutdown: false);
    }

    private void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        CrashLogWriter.Write("TaskScheduler.UnobservedTaskException", e.Exception);
        e.SetObserved();
    }

    private void HandleFatalException(string source, Exception exception, bool requestShutdown)
    {
        CrashLogWriter.Write(source, exception);

        if (Interlocked.Exchange(ref _fatalDialogShown, 1) != 0)
        {
            return;
        }

        try
        {
            MessageBox.Show(
                "Application encountered an unhandled exception. See crash.log for details.",
                "IIoT Edge Package Validation Tool",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        catch
        {
        }

        if (requestShutdown)
        {
            try
            {
                Shutdown(-1);
            }
            catch
            {
            }
        }
    }

    private bool TryAcquireInstanceLock(IConfiguration configuration)
    {
        var instanceId = configuration["InstanceId"] ?? "IIoT-Edge-Default";
        var mutexName = $"Global\\IIoT.Edge.PackageValidationClient_{instanceId}";

        _instanceMutex = new Mutex(true, mutexName, out var createdNew);
        if (createdNew)
        {
            return true;
        }

        MessageBox.Show(
            $"Instance [{instanceId}] is already running.",
            "IIoT Edge Package Validation Tool",
            MessageBoxButton.OK,
            MessageBoxImage.Warning);

        _instanceMutex = null;
        return false;
    }

    private void ReleaseMutex()
    {
        if (_instanceMutex is null)
        {
            return;
        }

        _instanceMutex.ReleaseMutex();
        _instanceMutex.Dispose();
        _instanceMutex = null;
    }

    private ServiceCollection ConfigureServices(IConfiguration configuration)
    {
        var services = new ServiceCollection();
        var viewRegistry = new ViewRegistry();
        var dbDir = GetDbDirectory();
        var pluginRootPath = PackageValidationModuleCatalog.GetPluginRootPath(AppDomain.CurrentDomain.BaseDirectory);
        var discoveryResult = PackageValidationModuleCatalog.DiscoverModules(pluginRootPath);
        var activationResult = PackageValidationModuleCatalog.CreateEnabledModules(configuration, discoveryResult.Modules);
        var moduleCatalogIssues = discoveryResult.Issues
            .Concat(activationResult.Issues)
            .ToArray();

        services.AddEdgeHostBootstrap(
            viewRegistry,
            configuration,
            dbDir,
            discoveryResult.Modules,
            moduleCatalogIssues,
            activationResult.EnabledModuleIds,
            activationResult.Modules);
        services.AddSingleton<MainWindowViewModel>();
        services.AddSingleton<MainWindow>();
        return services;
    }

    private static string GetDbDirectory()
    {
        var dbDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "data", "db");
        Directory.CreateDirectory(dbDir);
        return dbDir;
    }

    private static void ShowStartupError(string message)
    {
        MessageBox.Show(
            message,
            "IIoT Edge Package Validation Tool - Startup Error",
            MessageBoxButton.OK,
            MessageBoxImage.Error);
    }
}

