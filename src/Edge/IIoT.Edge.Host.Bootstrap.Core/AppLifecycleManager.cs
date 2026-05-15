using IIoT.Edge.Application.Abstractions.Logging;
using IIoT.Edge.Application.Abstractions.Modules;
using IIoT.Edge.Application.Abstractions.Tasks;
using IIoT.Edge.Application.Modules.Diagnostics;

namespace IIoT.Edge.Host.Bootstrap.Core;

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

            var diagnosticsReport = await BuildAndStoreDiagnosticsAsync(cancellationToken).ConfigureAwait(false);
            if (_diagnosticsReportBuilder.HasBlockingIssues(diagnosticsReport.Issues))
            {
                var message = _diagnosticsReportBuilder.BuildValidationMessage(diagnosticsReport.Issues);
                _logger.Error($"[生命周期] 启动校验失败。{Environment.NewLine}{message}");
                return AppStartupResult.Failure(message);
            }

            await _plcRuntimeTaskBinder.BindAsync(cancellationToken).ConfigureAwait(false);
            _logger.Info("[生命周期] PLC 模块绑定完成。");

            await _runtimeStateCoordinator.RestoreAsync(cancellationToken).ConfigureAwait(false);

            await _backgroundServices.StartAsync(cancellationToken).ConfigureAwait(false);
            _logger.Info("[生命周期] 后台服务已启动。");

            await BuildAndStoreDiagnosticsAsync(cancellationToken).ConfigureAwait(false);
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

    private async Task<StartupDiagnosticsReport> BuildAndStoreDiagnosticsAsync(CancellationToken cancellationToken)
    {
        var diagnosticsReport = await _diagnosticsReportBuilder.BuildAsync(cancellationToken).ConfigureAwait(false);
        _startupDiagnosticsStore.Update(diagnosticsReport);
        return diagnosticsReport;
    }
}
