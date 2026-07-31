using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using IIoT.Edge.Module.Contracts.Config;
using IIoT.Edge.Module.Contracts.Diagnostics;
using IIoT.Edge.Module.Contracts.Modules;
using IIoT.Edge.Host.Bootstrap;
using IIoT.Edge.Presentation.Navigation.Features.Shell;
using IIoT.Edge.Presentation.Panels.Features.Equipment;
using IIoT.Edge.Presentation.Panels.Features.SysLog;
using IIoT.Edge.Presentation.Shell;
using IIoT.Edge.Shell.Core;
using IIoT.Edge.Shell.Modules;
using IIoT.Edge.Shell.ViewModels;
using IIoT.Edge.SharedKernel.Configuration;
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
    private IDisposable? _updatePresenceLease;
    private int _fatalDialogShown;
    private int _shutdownStarted;
    private int _mainWindowReady;
    private bool _shutdownCompleted;

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
        var machineProfile = ResolveRequestedMachineProfile();
        IReadOnlyList<string> activeModuleIds = [];
        try
        {
            _updatePresenceLease =
                EdgeClientUpdateCoordination.TryAcquireShellPresence(
                    AppDomain.CurrentDomain.BaseDirectory);
            if (_updatePresenceLease is null)
            {
                ShowStartupError(
                    desktop,
                    "客户端更新正在进行，当前工序暂时不能启动。");
                _ = EdgeClientUpdateCoordination.TrySignalShellLaunchFailure(
                    machineProfile,
                    activeModuleIds,
                    "客户端更新正在进行，当前工序暂时不能启动。",
                    AppDomain.CurrentDomain.BaseDirectory);
                return;
            }

            _startupServiceProvider = new ServiceCollection()
                .AddShellStartupServices()
                .BuildServiceProvider();
            _crashLogWriter = _startupServiceProvider.GetRequiredService<ICrashLogWriter>();
            _configurationLoader = _startupServiceProvider.GetRequiredService<IShellConfigurationLoader>();
            _runtimePathResolver = _startupServiceProvider.GetRequiredService<IShellRuntimePathResolver>();
            _moduleCatalog = _startupServiceProvider.GetRequiredService<IShellModuleCatalog>();

            var configurationResult = _configurationLoader.Load(AppDomain.CurrentDomain.BaseDirectory);
            var configuration = configurationResult.Configuration;
            machineProfile = string.IsNullOrWhiteSpace(configurationResult.MachineProfile)
                ? machineProfile
                : configurationResult.MachineProfile;
            var runtimePathResolution = _runtimePathResolver.ResolveWithDiagnostics(
                AppDomain.CurrentDomain.BaseDirectory,
                configuration);
            var runtimePaths = runtimePathResolution.RuntimePaths;
            var runtimePathPreflight = EdgeRuntimePathPreflight.EnsureWritable(runtimePaths);
            runtimePaths = runtimePathPreflight.RuntimePaths;
            var bootstrapDiagnosticIssues = configurationResult.Issues
                .Concat(runtimePathResolution.Issues)
                .Concat(runtimePathPreflight.Issues)
                .ToArray();
            ConfigureCrashLogging(runtimePaths);
            WriteBootstrapDiagnosticIssues(bootstrapDiagnosticIssues);

            if (!TryAcquireInstanceLock(configuration, desktop))
            {
                return;
            }

            var serviceConfiguration = ConfigureServices(
                configuration,
                runtimePaths,
                configurationResult.EnvironmentName,
                bootstrapDiagnosticIssues);
            activeModuleIds = serviceConfiguration.ActiveModuleIds;
            _serviceProvider = serviceConfiguration.Services.BuildServiceProvider();
            _serviceProvider.GetRequiredService<IAppLanguageService>().Initialize();

            var lifecycle = _serviceProvider.GetRequiredService<IAppLifecycleCoordinator>();
            var startupResult = await lifecycle.StartAsync(_appCts.Token).ConfigureAwait(true);
            if (!startupResult.Success)
            {
                const string message = "应用启动失败，详细信息已写入诊断日志。";
                ShowStartupError(desktop, message);
                _ = EdgeClientUpdateCoordination.TrySignalShellLaunchFailure(
                    machineProfile,
                    activeModuleIds,
                    message,
                    AppDomain.CurrentDomain.BaseDirectory);
                return;
            }

            var mainWindow = _serviceProvider.GetRequiredService<MainWindow>();
            mainWindow.Closing += OnMainWindowClosing;
            desktop.MainWindow = mainWindow;
            mainWindow.Show();
            Volatile.Write(ref _mainWindowReady, 1);
            var moduleReadiness = ShellModuleLaunchReadiness.Evaluate(
                serviceConfiguration.ConfiguredModuleIds,
                activeModuleIds);
            if (!moduleReadiness.Success)
            {
                _ = EdgeClientUpdateCoordination.TrySignalShellLaunchFailure(
                    machineProfile,
                    activeModuleIds,
                    moduleReadiness.ErrorMessage!,
                    AppDomain.CurrentDomain.BaseDirectory);
                return;
            }

            var launchDiagnostics = BuildShellLaunchDiagnostics(
                _serviceProvider
                    .GetRequiredService<IStartupDiagnosticsStore>()
                    .Current
                    .Issues);
            _ = launchDiagnostics.Count > 0
                ? EdgeClientUpdateCoordination.TrySignalShellLaunchReadyWithDiagnostics(
                    machineProfile,
                    activeModuleIds,
                    launchDiagnostics,
                    AppDomain.CurrentDomain.BaseDirectory)
                : EdgeClientUpdateCoordination.TrySignalShellLaunchReady(
                    machineProfile,
                    activeModuleIds,
                    AppDomain.CurrentDomain.BaseDirectory);
        }
        catch (Exception ex)
        {
            TryWriteCrashLog("Shell 启动失败。", ex);
            const string message = "Shell 启动失败，详细信息已写入 crash.log。";
            ShowStartupError(desktop, message);
            _ = EdgeClientUpdateCoordination.TrySignalShellLaunchFailure(
                machineProfile,
                activeModuleIds,
                message,
                AppDomain.CurrentDomain.BaseDirectory);
        }
    }

    internal static IReadOnlyList<EdgeClientShellLaunchDiagnostic> BuildShellLaunchDiagnostics(
        IReadOnlyCollection<StartupDiagnosticIssue> issues)
    {
        ArgumentNullException.ThrowIfNull(issues);
        return issues
            .Select(static issue => new EdgeClientShellLaunchDiagnostic(
                NormalizeLaunchDiagnosticToken(issue.Code, "STARTUP_DIAGNOSTIC_PRESENT", 128)
                    ?? "STARTUP_DIAGNOSTIC_PRESENT",
                "System.Diagnostics",
                NormalizeLaunchDiagnosticToken(issue.ModuleId, fallback: null, 256)))
            .Distinct()
            .OrderBy(static diagnostic => diagnostic.ReasonCode, StringComparer.Ordinal)
            .ThenBy(static diagnostic => diagnostic.ModuleId, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static string? NormalizeLaunchDiagnosticToken(
        string? value,
        string? fallback,
        int maximumLength)
    {
        var normalized = value?.Trim();
        return string.IsNullOrWhiteSpace(normalized)
               || normalized.Length > maximumLength
               || normalized.Any(char.IsControl)
            ? fallback
            : normalized;
    }

    private void OnShutdownRequested(object? sender, ShutdownRequestedEventArgs e)
    {
        if (_shutdownCompleted)
        {
            return;
        }

        e.Cancel = true;

        if (Interlocked.Exchange(ref _shutdownStarted, 1) != 0)
        {
            return;
        }

        var desktop = sender as IClassicDesktopStyleApplicationLifetime
            ?? ApplicationLifetime as IClassicDesktopStyleApplicationLifetime;
        StartShutdownWatchdog();
        _ = ShutdownGracefullyAsync(desktop);
    }

    private void OnMainWindowClosing(object? sender, WindowClosingEventArgs e)
    {
        if (_shutdownCompleted)
        {
            return;
        }

        e.Cancel = true;

        if (Interlocked.Exchange(ref _shutdownStarted, 1) != 0)
        {
            return;
        }

        if (sender is Window window)
        {
            window.Hide();
        }

        var desktop = ApplicationLifetime as IClassicDesktopStyleApplicationLifetime;
        StartShutdownWatchdog();
        _ = ShutdownGracefullyAsync(desktop);
    }

    private void StartShutdownWatchdog()
    {
        _ = Task.Run(async () =>
        {
            await Task.Delay(TimeSpan.FromSeconds(ShutdownTimeoutSeconds)).ConfigureAwait(false);
            if (Environment.HasShutdownStarted)
            {
                return;
            }

            try
            {
                _crashLogWriter?.Write(
                    "应用关闭超时。",
                    details: $"关闭请求超过 {ShutdownTimeoutSeconds} 秒仍未退出，已强制结束残留进程。");
            }
            catch
            {
                // 关闭兜底阶段不能因为日志失败而留下后台进程。
            }

            ForceKillCurrentProcess();
        });
    }

    private async Task ShutdownGracefullyAsync(IClassicDesktopStyleApplicationLifetime? desktop)
    {
        var forceKill = false;

        try
        {
            desktop?.MainWindow?.Hide();
            _appCts.Cancel();

            if (_serviceProvider is not null)
            {
                forceKill = !await StopServicesWithinTimeoutAsync().ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            _crashLogWriter?.Write("应用关闭失败。", ex);
            forceKill = true;
        }
        finally
        {
            ReleaseRuntimeLocks();
            _appCts.Dispose();
            _startupServiceProvider?.Dispose();

            if (forceKill)
            {
                ForceKillCurrentProcess();
            }
        }

        if (forceKill)
        {
            return;
        }

        _shutdownCompleted = true;
        if (desktop is null)
        {
            Environment.Exit(0);
            return;
        }

        await Dispatcher.UIThread.InvokeAsync(() => desktop.Shutdown(0));
    }

    private async Task<bool> StopServicesWithinTimeoutAsync()
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
        var timeoutTask = Task.Delay(TimeSpan.FromSeconds(ShutdownTimeoutSeconds));

        if (await Task.WhenAny(shutdownTask, timeoutTask).ConfigureAwait(false) != shutdownTask)
        {
            _crashLogWriter?.Write(
                "应用关闭超时。",
                details: $"生命周期停机超过 {ShutdownTimeoutSeconds} 秒，已准备强制结束残留进程。");
            return false;
        }

        try
        {
            await shutdownTask.ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _crashLogWriter?.Write(
                "应用关闭失败。",
                ex);
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
        var disposition = ShellDispatcherExceptionPolicy.Resolve(
            Volatile.Read(ref _mainWindowReady) == 1);
        if (disposition == ShellDispatcherExceptionDisposition.RecoverRuntime)
        {
            TryWriteCrashLog(
                "Shell 运行期 UI 未处理异常，已保留主窗口和后台服务。",
                e.Exception);
            e.Handled = true;
            return;
        }

        HandleFatalException("Shell 启动期 UI 未处理异常", e.Exception, requestShutdown: true);
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
        TryWriteCrashLog(source, exception);

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
        var instanceId = EdgeClientInstanceMutexName.NormalizeInstanceId(configuration["InstanceId"]);
        var mutexName = EdgeClientInstanceMutexName.Create(instanceId);

        var acquireResult = _instanceLock.TryAcquireNonBlocking(mutexName, out var lockFailure);
        if (acquireResult == SingleInstanceMutexAcquireResult.Acquired)
        {
            return true;
        }

        if (acquireResult == SingleInstanceMutexAcquireResult.Unavailable)
        {
            TryWriteCrashLog(
                "单实例锁不可用，已按非阻断启动。",
                lockFailure,
                $"实例 [{instanceId}] 无法创建或访问命名互斥量。");
            return true;
        }

        TryWriteCrashLog(
            "单实例锁已占用。",
            exception: null,
            details: $"实例 [{instanceId}] 已在运行，当前进程退出。");
        desktop.Shutdown(0);
        Environment.Exit(0);
        return false;
    }

    private void ReleaseRuntimeLocks()
    {
        _instanceLock.Release();
        Interlocked.Exchange(ref _updatePresenceLease, null)?.Dispose();
    }

    private ShellServiceConfiguration ConfigureServices(
        IConfiguration configuration,
        EdgeRuntimePaths runtimePaths,
        string environmentName,
        IReadOnlyCollection<StartupDiagnosticIssue> bootstrapDiagnosticIssues)
    {
        var services = new ServiceCollection();
        var viewRegistry = new ViewRegistry();
        var pluginRootPaths = _moduleCatalog!.GetPluginRootPaths(
            AppDomain.CurrentDomain.BaseDirectory,
            configuration);
        var discoveryResult = _moduleCatalog.DiscoverModules(pluginRootPaths);
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
            activationResult.Modules,
            bootstrapDiagnosticIssues);
        services.AddSingleton<MainWindowViewModel>();
        services.AddSingleton(sp => new MainWindow(
            sp.GetRequiredService<MainWindowViewModel>(),
            sp.GetRequiredService<NavigationRailView>(),
            sp.GetRequiredService<NavigationHostView>(),
            sp.GetRequiredService<EquipmentView>(),
            sp.GetRequiredService<LogView>()));
        return new ShellServiceConfiguration(
            services,
            activationResult.EnabledModuleIds,
            activationResult.Modules
                .Select(static module => module.ModuleId)
                .ToArray());
    }

    private static string ResolveRequestedMachineProfile()
    {
        var candidate = Environment
            .GetEnvironmentVariable("Shell__MachineProfile")
            ?.Trim();
        return string.IsNullOrWhiteSpace(candidate)
               || candidate.Length > 128
               || candidate.Any(char.IsControl)
            ? "Default"
            : candidate;
    }

    private void ConfigureCrashLogging(EdgeRuntimePaths runtimePaths)
    {
        _crashLogWriter?.ConfigurePaths(
            runtimePaths.PrimaryCrashLogPath,
            runtimePaths.FallbackCrashLogPath);
    }

    private void WriteBootstrapDiagnosticIssues(IReadOnlyCollection<StartupDiagnosticIssue> issues)
    {
        if (issues.Count == 0)
        {
            return;
        }

        TryWriteCrashLog(
            "Shell 启动预检发现问题。",
            exception: null,
            details: string.Join(Environment.NewLine, issues.Select(issue => $"- [{issue.Code}] {issue.Message}")));
    }

    private void TryWriteCrashLog(string source, Exception? exception = null, string? details = null)
    {
        try
        {
            _crashLogWriter?.Write(source, exception, details);
        }
        catch
        {
            // 日志通道失效不得阻断 UI 启动或异常收口。
        }
    }

    private static void ShowStartupError(IClassicDesktopStyleApplicationLifetime desktop, string message)
    {
        var dialog = new ShellCrashDialog(message);
        desktop.MainWindow = dialog;
        dialog.Closed += (_, _) => desktop.Shutdown(-1);
        dialog.Show();
    }

    private sealed record ShellServiceConfiguration(
        ServiceCollection Services,
        IReadOnlyList<string> ConfiguredModuleIds,
        IReadOnlyList<string> ActiveModuleIds);
}
