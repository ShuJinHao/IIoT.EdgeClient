using IIoT.Edge.Module.Contracts.Logging;
using IIoT.Edge.Module.Contracts.Modules;
using IIoT.Edge.Module.Contracts.Tasks;
using IIoT.Edge.Application.Common.Tasks;
using IIoT.Edge.Module.Contracts.Diagnostics;

namespace IIoT.Edge.Shell.Core;

public class AppLifecycleManager : IAppLifecycleCoordinator
{
    private readonly IAppStartupInitializer _startupInitializer;
    private readonly IStartupDiagnosticsReportBuilder _diagnosticsReportBuilder;
    private readonly IStartupDiagnosticsStore _startupDiagnosticsStore;
    private readonly IPlcRuntimeTaskBinder _plcRuntimeTaskBinder;
    private readonly IAppRuntimeStateCoordinator _runtimeStateCoordinator;
    private readonly IBackgroundServiceCoordinator _backgroundServices;
    private readonly ILogService _logger;
    private readonly SemaphoreSlim _lifecycleGate = new(1, 1);
    private AppLifecycleState _state;
    private AppStartupResult? _lastSuccessfulStart;

    public AppLifecycleManager(
        IAppStartupInitializer startupInitializer,
        IStartupDiagnosticsReportBuilder diagnosticsReportBuilder,
        IStartupDiagnosticsStore startupDiagnosticsStore,
        IPlcRuntimeTaskBinder plcRuntimeTaskBinder,
        IAppRuntimeStateCoordinator runtimeStateCoordinator,
        IBackgroundServiceCoordinator backgroundServices,
        ILogService logger)
    {
        _startupInitializer = startupInitializer;
        _diagnosticsReportBuilder = diagnosticsReportBuilder;
        _startupDiagnosticsStore = startupDiagnosticsStore;
        _plcRuntimeTaskBinder = plcRuntimeTaskBinder;
        _runtimeStateCoordinator = runtimeStateCoordinator;
        _backgroundServices = backgroundServices;
        _logger = logger;
    }

    public async Task<AppStartupResult> StartAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await _lifecycleGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            _logger.Warn("[生命周期] 应用启动已取消。");
            throw;
        }

        try
        {
            if (_state == AppLifecycleState.Started && _lastSuccessfulStart is not null)
                return _lastSuccessfulStart;

            _state = AppLifecycleState.Starting;
            var result = await StartCoreAsync(cancellationToken).ConfigureAwait(false);
            if (result.Success)
            {
                _lastSuccessfulStart = result;
                _state = AppLifecycleState.Started;
            }
            else
            {
                _state = AppLifecycleState.Stopped;
            }

            return result;
        }
        catch
        {
            _state = AppLifecycleState.Stopped;
            throw;
        }
        finally
        {
            _lifecycleGate.Release();
        }
    }

    private async Task<AppStartupResult> StartCoreAsync(CancellationToken cancellationToken)
    {
        var nonBlockingIssues = new List<StartupDiagnosticIssue>();
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            _logger.Info("[生命周期] 开始应用启动。");

            var initializationIssues = await _startupInitializer
                .InitializeAsync(cancellationToken)
                .ConfigureAwait(false);
            foreach (var issue in initializationIssues)
                nonBlockingIssues.Add(issue);

            await BuildStoreAndLogDiagnosticsAsync(cancellationToken, nonBlockingIssues).ConfigureAwait(false);

            await RunNonBlockingStartupStepAsync(
                "PLC 模块绑定",
                () => _plcRuntimeTaskBinder.BindAsync(cancellationToken),
                cancellationToken,
                nonBlockingIssues,
                _ => "STARTUP_PLC_BINDING_FAILED").ConfigureAwait(false);

            await RunNonBlockingStartupStepAsync(
                "运行时持久化状态恢复",
                () => _runtimeStateCoordinator.RestoreAsync(cancellationToken),
                cancellationToken,
                nonBlockingIssues,
                _ => "STARTUP_RUNTIME_STATE_RESTORE_FAILED").ConfigureAwait(false);

            await RunNonBlockingStartupStepAsync(
                "后台服务启动",
                () => _backgroundServices.StartAsync(cancellationToken),
                cancellationToken,
                nonBlockingIssues,
                ResolveBackgroundStartupFailureCode).ConfigureAwait(false);

            await BuildStoreAndLogDiagnosticsAsync(cancellationToken, nonBlockingIssues).ConfigureAwait(false);
            return AppStartupResult.Ok();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            _logger.Warn("[生命周期] 应用启动已取消。");
            throw;
        }
        catch (DevicePluginDatabaseStartupException exception)
        {
            _logger.Error($"[生命周期] 插件数据库启动失败：{exception.ReasonCode}。");
            return AppStartupResult.Failure(exception.ReasonCode);
        }
        catch (Exception ex)
        {
            _logger.Error($"[生命周期] 启动失败（{ex.GetType().Name}）。");
            return AppStartupResult.Failure("应用启动失败，详细信息已写入诊断日志。");
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        await _lifecycleGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_state == AppLifecycleState.Stopped)
                return;

            _state = AppLifecycleState.Stopping;
            List<Exception>? failures = null;
            var backgroundStopped = false;
            try
            {
                await _backgroundServices.StopAsync(cancellationToken).ConfigureAwait(false);
                backgroundStopped = true;
                _logger.Info("[生命周期] 后台服务已停止。");
            }
            catch (Exception ex)
            {
                (failures ??= []).Add(ex);
                _logger.Error($"[生命周期] 后台服务停止失败（{ex.GetType().Name}）。");
            }

            try
            {
                await _runtimeStateCoordinator.SaveAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                (failures ??= []).Add(ex);
                _logger.Error($"[生命周期] 最终运行时状态保存失败（{ex.GetType().Name}）。");
            }

            _state = backgroundStopped ? AppLifecycleState.Stopped : AppLifecycleState.Started;
            ThrowStopFailures(failures);
        }
        finally
        {
            _lifecycleGate.Release();
        }
    }

    private static void ThrowStopFailures(List<Exception>? failures)
    {
        if (failures is null or { Count: 0 })
            return;

        if (failures.Count == 1)
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failures[0]).Throw();

        throw new AggregateException(failures);
    }

    private async Task BuildStoreAndLogDiagnosticsAsync(
        CancellationToken cancellationToken,
        IReadOnlyCollection<StartupDiagnosticIssue>? additionalIssues = null)
    {
        try
        {
            var diagnosticsReport = await BuildAndStoreDiagnosticsAsync(
                cancellationToken,
                additionalIssues).ConfigureAwait(false);
            if (diagnosticsReport.Issues.Count > 0)
            {
                var message = _diagnosticsReportBuilder.BuildValidationMessage(diagnosticsReport.Issues);
                _logger.Warn($"[生命周期] 启动诊断发现问题，已按非阻断处理。{Environment.NewLine}{message}");
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.Warn($"[生命周期] 启动诊断生成失败，已跳过诊断并继续启动（{ex.GetType().Name}）。");
        }
    }

    private async Task RunNonBlockingStartupStepAsync(
        string stepName,
        Func<Task> action,
        CancellationToken cancellationToken,
        ICollection<StartupDiagnosticIssue> issues,
        Func<Exception, string> failureCodeResolver)
    {
        try
        {
            await action().ConfigureAwait(false);
            _logger.Info($"[生命周期] {stepName}完成。");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            if (ex is BackgroundServiceStartException { Failures.Count: > 0 } backgroundFailure)
            {
                foreach (var failure in backgroundFailure.Failures)
                {
                    var serviceFailure = new BackgroundServiceStartException([failure]);
                    issues.Add(StartupDiagnosticIssueFactory.Create(
                        failureCodeResolver(serviceFailure),
                        $"{stepName}失败，已按非阻断处理：{failure.ServiceName}（{failure.Exception.GetType().Name}）。"));
                }
            }
            else
            {
                issues.Add(StartupDiagnosticIssueFactory.Create(
                    failureCodeResolver(ex),
                    $"{stepName}失败，已按非阻断处理（{ex.GetType().Name}）。"));
            }

            _logger.Warn($"[生命周期] {stepName}失败，已按非阻断处理（{ex.GetType().Name}）。");
        }
    }

    private async Task<StartupDiagnosticsReport> BuildAndStoreDiagnosticsAsync(
        CancellationToken cancellationToken,
        IReadOnlyCollection<StartupDiagnosticIssue>? additionalIssues = null)
    {
        var diagnosticsReport = await _diagnosticsReportBuilder.BuildAsync(cancellationToken).ConfigureAwait(false);
        if (additionalIssues is { Count: > 0 })
        {
            diagnosticsReport = diagnosticsReport with
            {
                Issues = diagnosticsReport.Issues.Concat(additionalIssues).ToArray()
            };
        }

        _startupDiagnosticsStore.Update(diagnosticsReport);
        return diagnosticsReport;
    }

    private static string ResolveBackgroundStartupFailureCode(Exception exception)
    {
        if (exception is not BackgroundServiceStartException backgroundFailure)
        {
            return "STARTUP_BACKGROUND_SERVICE_FAILED";
        }

        if (string.Equals(
                backgroundFailure.ServiceName,
                "ProcessQueueTask",
                StringComparison.OrdinalIgnoreCase))
        {
            return "STARTUP_PROCESS_QUEUE_TASK_FAILED";
        }

        if (string.Equals(
                backgroundFailure.ServiceName,
                "CloudRetryTask",
                StringComparison.OrdinalIgnoreCase))
        {
            return "STARTUP_CLOUD_RETRY_TASK_FAILED";
        }

        if (string.Equals(
                backgroundFailure.ServiceName,
                "MesRetryTask",
                StringComparison.OrdinalIgnoreCase))
        {
            return "STARTUP_MES_RETRY_TASK_FAILED";
        }

        if (backgroundFailure.ServiceName.StartsWith("PLC.", StringComparison.OrdinalIgnoreCase))
        {
            return "STARTUP_PLC_UNREACHABLE";
        }

        if (backgroundFailure.ServiceName.StartsWith("MES.", StringComparison.OrdinalIgnoreCase))
        {
            return "STARTUP_MES_UNREACHABLE";
        }

        if (backgroundFailure.ServiceName.StartsWith("Cloud.", StringComparison.OrdinalIgnoreCase))
        {
            return "STARTUP_CLOUD_UNREACHABLE";
        }

        return "STARTUP_BACKGROUND_SERVICE_FAILED";
    }

    private enum AppLifecycleState
    {
        Stopped,
        Starting,
        Started,
        Stopping
    }
}
