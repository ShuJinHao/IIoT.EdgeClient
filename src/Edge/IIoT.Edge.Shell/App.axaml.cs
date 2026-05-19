using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using IIoT.Edge.Application.Abstractions.Config;
using IIoT.Edge.Host.Bootstrap;
using IIoT.Edge.Presentation.Navigation.Features.Shell;
using IIoT.Edge.Presentation.Panels.Features.Equipment;
using IIoT.Edge.Presentation.Panels.Features.SysLog;
using IIoT.Edge.Presentation.Shell;
using IIoT.Edge.Shell.Core;
using IIoT.Edge.Shell.Modules;
using IIoT.Edge.Shell.ViewModels;
using IIoT.Edge.UI.Shared;
using IIoT.Edge.UI.Shared.Localization;
using IIoT.Edge.UI.Shared.Modularity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace IIoT.Edge.Shell;

public partial class App : global::Avalonia.Application
{
    private const int ShutdownTimeoutSeconds = 8;

    private ServiceProvider? _serviceProvider;
    private ServiceProvider? _startupServiceProvider;
    private ICrashLogWriter? _crashLogWriter;
    private IShellConfigurationLoader? _configurationLoader;
    private IShellRuntimePathResolver? _runtimePathResolver;
    private IShellModuleCatalog? _moduleCatalog;
    private readonly CancellationTokenSource _appCts = new();
    private readonly SingleInstanceMutexHandle _instanceLock = new();
    private int _fatalDialogShown;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        RegisterGlobalExceptionHandlers();

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.ShutdownRequested += OnShutdownRequested;
            _ = StartShellAsync(desktop);
        }

        base.OnFrameworkInitializationCompleted();
    }

    private async Task StartShellAsync(IClassicDesktopStyleApplicationLifetime desktop)
    {
        _startupServiceProvider = new ServiceCollection()
            .AddShellStartupServices()
            .BuildServiceProvider();
        _crashLogWriter = _startupServiceProvider.GetRequiredService<ICrashLogWriter>();
        _configurationLoader = _startupServiceProvider.GetRequiredService<IShellConfigurationLoader>();
        _runtimePathResolver = _startupServiceProvider.GetRequiredService<IShellRuntimePathResolver>();
        _moduleCatalog = _startupServiceProvider.GetRequiredService<IShellModuleCatalog>();

        var configurationResult = _configurationLoader.Load(AppDomain.CurrentDomain.BaseDirectory);
        var configuration = configurationResult.Configuration;
        var runtimePaths = _runtimePathResolver.Resolve(AppDomain.CurrentDomain.BaseDirectory, configuration);
        ConfigureCrashLogging(runtimePaths);

        if (!TryAcquireInstanceLock(configuration, desktop))
        {
            return;
        }

        try
        {
            _serviceProvider = ConfigureServices(
                configuration,
                runtimePaths,
                configurationResult.EnvironmentName).BuildServiceProvider();
            _serviceProvider.GetRequiredService<IAppLanguageService>().Initialize();
        }
        catch (Exception ex)
        {
            ShowStartupError(desktop, $"启动服务配置失败：{ex.Message}");
            return;
        }

        var lifecycle = _serviceProvider.GetRequiredService<IAppLifecycleCoordinator>();
        var startupResult = await lifecycle.StartAsync(_appCts.Token).ConfigureAwait(true);
        if (!startupResult.Success)
        {
            ShowStartupError(desktop, startupResult.Message ?? "应用启动失败。");
            return;
        }

        var mainWindow = _serviceProvider.GetRequiredService<MainWindow>();
        desktop.MainWindow = mainWindow;
        mainWindow.Show();
    }

    private void OnShutdownRequested(object? sender, ShutdownRequestedEventArgs e)
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
            _crashLogWriter?.Write("应用关闭失败。", ex);
            forceKill = true;
        }
        finally
        {
            ReleaseMutex();
            _appCts.Dispose();
            _startupServiceProvider?.Dispose();

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
            _crashLogWriter?.Write(
                "应用关闭超时。",
                details: $"生命周期停机超过 {ShutdownTimeoutSeconds} 秒，已准备强制结束残留进程。");
            return false;
        }

        if (shutdownTask.IsFaulted)
        {
            _crashLogWriter?.Write(
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
        Dispatcher.UIThread.UnhandledException += OnDispatcherUnhandledException;
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
        _crashLogWriter?.Write("后台任务未观察异常", e.Exception);
        e.SetObserved();
    }

    private void HandleFatalException(string source, Exception exception, bool requestShutdown)
    {
        _crashLogWriter?.Write(source, exception);

        if (Interlocked.Exchange(ref _fatalDialogShown, 1) != 0)
        {
            return;
        }

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            ShowStartupError(desktop, "程序发生未处理异常，详细信息已写入 crash.log，应用将退出。");
            if (requestShutdown)
            {
                desktop.Shutdown(-1);
            }
        }
    }

    private bool TryAcquireInstanceLock(IConfiguration configuration, IClassicDesktopStyleApplicationLifetime desktop)
    {
        var instanceId = configuration["InstanceId"] ?? "IIoT-Edge-Default";
        var mutexName = $"Global\\IIoT.EdgeClient_{instanceId}";

        if (_instanceLock.TryAcquire(mutexName))
        {
            return true;
        }

        _crashLogWriter?.Write(
            "单实例锁已占用。",
            details: $"实例 [{instanceId}] 已在运行，当前进程退出。");
        desktop.Shutdown(0);
        Environment.Exit(0);
        return false;
    }

    private void ReleaseMutex() => _instanceLock.Release();

    private ServiceCollection ConfigureServices(
        IConfiguration configuration,
        EdgeRuntimePaths runtimePaths,
        string environmentName)
    {
        var services = new ServiceCollection();
        var viewRegistry = new ViewRegistry();
        var pluginRootPath = _moduleCatalog!.GetPluginRootPath(AppDomain.CurrentDomain.BaseDirectory);
        var discoveryResult = _moduleCatalog.DiscoverModules(pluginRootPath);
        var activationResult = _moduleCatalog.CreateEnabledModules(configuration, discoveryResult.Modules);
        var moduleCatalogIssues = discoveryResult.Issues
            .Concat(activationResult.Issues)
            .ToArray();

        services.AddUiShared();
        services.AddSingleton(_crashLogWriter!);
        services.AddEdgeHostBootstrap(
            viewRegistry,
            configuration,
            runtimePaths,
            environmentName,
            discoveryResult.Modules,
            moduleCatalogIssues,
            activationResult.EnabledModuleIds,
            activationResult.Modules);
        services.AddSingleton<MainWindowViewModel>();
        services.AddSingleton(sp => new MainWindow(
            sp.GetRequiredService<MainWindowViewModel>(),
            sp.GetRequiredService<NavigationRailView>(),
            sp.GetRequiredService<NavigationHostView>(),
            sp.GetRequiredService<EquipmentView>(),
            sp.GetRequiredService<LogView>()));
        return services;
    }

    private void ConfigureCrashLogging(EdgeRuntimePaths runtimePaths)
    {
        _crashLogWriter?.ConfigurePaths(
            runtimePaths.PrimaryCrashLogPath,
            runtimePaths.FallbackCrashLogPath);
    }

    private static void ShowStartupError(IClassicDesktopStyleApplicationLifetime desktop, string message)
    {
        var dialog = new ShellCrashDialog(message);
        desktop.MainWindow = dialog;
        dialog.Closed += (_, _) => desktop.Shutdown(-1);
        dialog.Show();
    }
}
