using IIoT.Edge.Application.Abstractions.Logging;
using IIoT.Edge.Application.Abstractions.Modules;
using IIoT.Edge.Application.Abstractions.Tasks;
using IIoT.Edge.Application.Modules.Diagnostics;

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
            _logger.Info("[生命周期] 开始应用启动。");

            await _startupInitializer.InitializeAsync(cancellationToken).ConfigureAwait(false);

            await BuildStoreAndLogDiagnosticsAsync(cancellationToken).ConfigureAwait(false);

            await RunNonBlockingStartupStepAsync(
                "PLC 模块绑定",
                () => _plcRuntimeTaskBinder.BindAsync(cancellationToken)).ConfigureAwait(false);

            await RunNonBlockingStartupStepAsync(
                "运行时持久化状态恢复",
                () => _runtimeStateCoordinator.RestoreAsync(cancellationToken)).ConfigureAwait(false);

            await RunNonBlockingStartupStepAsync(
                "后台服务启动",
                () => _backgroundServices.StartAsync(cancellationToken)).ConfigureAwait(false);

            await BuildStoreAndLogDiagnosticsAsync(cancellationToken).ConfigureAwait(false);
            return AppStartupResult.Ok();
        }
        catch (Exception ex)
        {
            _logger.Error($"[生命周期] 启动失败：{ex.Message}");
            return AppStartupResult.Failure($"应用启动失败：{ex.Message}");
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        await _runtimeStateCoordinator.SaveAsync(cancellationToken).ConfigureAwait(false);

        await _backgroundServices.StopAsync(cancellationToken).ConfigureAwait(false);
        _logger.Info("[生命周期] 后台服务已停止。");
    }

    private async Task BuildStoreAndLogDiagnosticsAsync(CancellationToken cancellationToken)
    {
        try
        {
            var diagnosticsReport = await BuildAndStoreDiagnosticsAsync(cancellationToken).ConfigureAwait(false);
            if (diagnosticsReport.Issues.Count > 0)
            {
                var message = _diagnosticsReportBuilder.BuildValidationMessage(diagnosticsReport.Issues);
                _logger.Warn($"[生命周期] 启动诊断发现问题，已按非阻断处理。{Environment.NewLine}{message}");
            }
        }
        catch (Exception ex)
        {
            _logger.Warn($"[生命周期] 启动诊断生成失败，已跳过诊断并继续启动：{ex.Message}");
        }
    }

    private async Task RunNonBlockingStartupStepAsync(string stepName, Func<Task> action)
    {
        try
        {
            await action().ConfigureAwait(false);
            _logger.Info($"[生命周期] {stepName}完成。");
        }
        catch (Exception ex)
        {
            _logger.Warn($"[生命周期] {stepName}失败，已按非阻断处理：{ex.Message}");
        }
    }

    private async Task<StartupDiagnosticsReport> BuildAndStoreDiagnosticsAsync(CancellationToken cancellationToken)
    {
        var diagnosticsReport = await _diagnosticsReportBuilder.BuildAsync(cancellationToken).ConfigureAwait(false);
        _startupDiagnosticsStore.Update(diagnosticsReport);
        return diagnosticsReport;
    }
}
