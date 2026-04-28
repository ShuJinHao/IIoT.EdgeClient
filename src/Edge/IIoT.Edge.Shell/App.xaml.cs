using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using IIoT.Edge.Application.Abstractions.Config;
using IIoT.Edge.Host.Bootstrap;
using IIoT.Edge.UI.Shared.Localization;
using IIoT.Edge.Shell.Core;
using IIoT.Edge.Shell.Modules;
using IIoT.Edge.Shell.ViewModels;
using IIoT.Edge.UI.Shared.Modularity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using WpfApplication = System.Windows.Application;

namespace IIoT.Edge.Shell;

public partial class App : WpfApplication
{
    private const int ShutdownTimeoutSeconds = 8;

    private ServiceProvider? _serviceProvider;
    private readonly CancellationTokenSource _appCts = new();
    private readonly SingleInstanceMutexHandle _instanceLock = new();
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
        var runtimePaths = ShellRuntimePathResolver.Resolve(AppDomain.CurrentDomain.BaseDirectory, configuration);
        ConfigureCrashLogging(runtimePaths);

        if (!TryAcquireInstanceLock(configuration))
        {
            Shutdown();
            return;
        }

        try
        {
            _serviceProvider = ConfigureServices(configuration, runtimePaths).BuildServiceProvider();
            _serviceProvider.GetRequiredService<IAppLanguageService>().Initialize();
        }
        catch (Exception ex)
        {
            ShowStartupError($"启动服务配置失败：{ex.Message}");
            Shutdown(-1);
            return;
        }

        var lifecycle = _serviceProvider.GetRequiredService<IAppLifecycleCoordinator>();
        var startupResult = await lifecycle.StartAsync(_appCts.Token);
        if (!startupResult.Success)
        {
            ShowStartupError(startupResult.Message ?? "应用启动失败。");
            Shutdown(-1);
            return;
        }

        var mainWindow = _serviceProvider.GetRequiredService<MainWindow>();
        mainWindow.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        var forceKill = false;

        try
        {
            _appCts.Cancel();

            if (_serviceProvider is not null)
            {
                forceKill = !StopServicesWithinTimeout();
            }
        }
        catch (Exception ex)
        {
            CrashLogWriter.Write("应用关闭失败。", ex);
            forceKill = true;
        }
        finally
        {
            ReleaseMutex();
            _appCts.Dispose();
            base.OnExit(e);

            if (forceKill)
            {
                ForceKillCurrentProcess();
            }
        }
    }

    private bool StopServicesWithinTimeout()
    {
        var provider = _serviceProvider;
        if (provider is null)
        {
            return true;
        }

        using var shutdownCts = new CancellationTokenSource(TimeSpan.FromSeconds(ShutdownTimeoutSeconds));

        // 关闭先走生命周期保存和断开逻辑；超时才强制结束，避免后台任务把进程长期挂住。
        var shutdownTask = Task.Run(async () =>
        {
            var lifecycle = provider.GetRequiredService<IAppLifecycleCoordinator>();
            await lifecycle.StopAsync(shutdownCts.Token).ConfigureAwait(false);
            await provider.DisposeAsync().AsTask().ConfigureAwait(false);
            _serviceProvider = null;
        });

        if (!shutdownTask.Wait(TimeSpan.FromSeconds(ShutdownTimeoutSeconds)))
        {
            CrashLogWriter.Write(
                "应用关闭超时。",
                details: $"生命周期停机超过 {ShutdownTimeoutSeconds} 秒，已准备强制结束残留进程。");
            return false;
        }

        if (shutdownTask.IsFaulted)
        {
            CrashLogWriter.Write(
                "应用关闭失败。",
                shutdownTask.Exception?.GetBaseException() ?? new InvalidOperationException("关闭任务失败但未返回异常明细。"));
            return false;
        }

        return true;
    }

    private static void ForceKillCurrentProcess()
    {
        try
        {
            Process.GetCurrentProcess().Kill(entireProcessTree: true);
        }
        catch
        {
            Environment.Exit(-1);
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
        HandleFatalException("UI 线程未处理异常", e.Exception, requestShutdown: true);
        e.Handled = true;
    }

    private void OnAppDomainUnhandledException(object? sender, UnhandledExceptionEventArgs e)
    {
        var exception = e.ExceptionObject as Exception
            ?? new Exception(e.ExceptionObject?.ToString() ?? "未知未处理异常。");
        HandleFatalException("应用域未处理异常", exception, requestShutdown: false);
    }

    private void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        CrashLogWriter.Write("后台任务未观察异常", e.Exception);
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
                "程序发生未处理异常，详细信息已写入 crash.log，应用将退出。",
                "IIoT Edge Client",
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
        var mutexName = $"Global\\IIoT.EdgeClient_{instanceId}";

        if (_instanceLock.TryAcquire(mutexName))
        {
            return true;
        }

        MessageBox.Show(
            $"实例 [{instanceId}] 已在运行。",
            "IIoT Edge Client",
            MessageBoxButton.OK,
            MessageBoxImage.Warning);

        return false;
    }

    private void ReleaseMutex() => _instanceLock.Release();

    private ServiceCollection ConfigureServices(IConfiguration configuration, EdgeRuntimePaths runtimePaths)
    {
        var services = new ServiceCollection();
        var viewRegistry = new ViewRegistry();
        var pluginRootPath = ShellModuleCatalog.GetPluginRootPath(AppDomain.CurrentDomain.BaseDirectory);
        var discoveryResult = ShellModuleCatalog.DiscoverModules(pluginRootPath);
        var activationResult = ShellModuleCatalog.CreateEnabledModules(configuration, discoveryResult.Modules);
        var moduleCatalogIssues = discoveryResult.Issues
            .Concat(activationResult.Issues)
            .ToArray();

        services.AddEdgeHostBootstrap(
            viewRegistry,
            configuration,
            runtimePaths,
            discoveryResult.Modules,
            moduleCatalogIssues,
            activationResult.EnabledModuleIds,
            activationResult.Modules);
        services.AddSingleton<MainWindowViewModel>();
        services.AddSingleton<MainWindow>();
        return services;
    }

    private static void ConfigureCrashLogging(EdgeRuntimePaths runtimePaths)
    {
        CrashLogWriter.ConfigurePaths(
            () => runtimePaths.PrimaryCrashLogPath,
            () => runtimePaths.FallbackCrashLogPath);
    }

    private static void ShowStartupError(string message)
    {
        MessageBox.Show(
            message,
            "IIoT Edge Client - 启动失败",
            MessageBoxButton.OK,
            MessageBoxImage.Error);
    }
}
