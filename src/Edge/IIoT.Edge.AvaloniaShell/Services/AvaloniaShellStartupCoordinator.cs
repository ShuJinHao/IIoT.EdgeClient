using IIoT.Edge.Application.Abstractions.Config;
using IIoT.Edge.Application.Abstractions.Modules;
using IIoT.Edge.Application.Modules.Diagnostics;
using IIoT.Edge.Host.Bootstrap;
using IIoT.Edge.UI.Avalonia.Services;

namespace IIoT.Edge.AvaloniaShell.Services;

public sealed class AvaloniaShellStartupCoordinator : IAvaloniaShellStartupCoordinator
{
    public const string StartRuntimeArgument = "--start-runtime";

    private readonly IAppLifecycleCoordinator _lifecycleCoordinator;
    private readonly IAvaloniaRuntimeState _runtimeState;
    private readonly IStartupDiagnosticsStore? _diagnosticsStore;
    private readonly EdgeRuntimePaths? _runtimePaths;
    private bool _runtimeStarted;

    public AvaloniaShellStartupCoordinator(
        IAppLifecycleCoordinator lifecycleCoordinator,
        IAvaloniaRuntimeState runtimeState,
        IStartupDiagnosticsStore? diagnosticsStore = null,
        EdgeRuntimePaths? runtimePaths = null)
    {
        _lifecycleCoordinator = lifecycleCoordinator ?? throw new ArgumentNullException(nameof(lifecycleCoordinator));
        _runtimeState = runtimeState ?? throw new ArgumentNullException(nameof(runtimeState));
        _diagnosticsStore = diagnosticsStore;
        _runtimePaths = runtimePaths;
    }

    public bool ShouldStartRuntime(IEnumerable<string>? arguments)
        => arguments?.Any(argument => string.Equals(argument, StartRuntimeArgument, StringComparison.OrdinalIgnoreCase)) == true;

    public async Task<AvaloniaShellStartupResult> StartAsync(
        IEnumerable<string>? arguments,
        CancellationToken cancellationToken = default)
    {
        if (!ShouldStartRuntime(arguments))
        {
            _runtimeState.SetStatus(
                AvaloniaRuntimeStatus.UiOnly,
                "默认 UI-only 模式，运行链路未启动。");
            return AvaloniaShellStartupResult.UiOnly();
        }

        try
        {
            _runtimeState.SetStatus(
                AvaloniaRuntimeStatus.Starting,
                "正在启动运行链路，请等待启动诊断生成。");

            var startupResult = await _lifecycleCoordinator.StartAsync(cancellationToken).ConfigureAwait(false);
            if (!startupResult.Success)
            {
                var failureMessage = startupResult.Message ?? "AvaloniaShell 启动失败。";
                var diagnosticsSummary = BuildDiagnosticsSummary();
                var diagnosticsLogPath = ResolveDiagnosticsLogPath();
                _runtimeState.SetStatus(
                    AvaloniaRuntimeStatus.StartFailed,
                    failureMessage,
                    diagnosticsSummary,
                    diagnosticsLogPath);
                return AvaloniaShellStartupResult.Failure(failureMessage, diagnosticsSummary, diagnosticsLogPath);
            }

            _runtimeStarted = true;
            var summary = BuildDiagnosticsSummary();
            var logPath = ResolveDiagnosticsLogPath();
            _runtimeState.SetStatus(
                AvaloniaRuntimeStatus.Running,
                "运行链路已启动，可进行现场联调。",
                summary,
                logPath);
            return AvaloniaShellStartupResult.RuntimeStartedOk(summary, logPath);
        }
        catch (Exception ex)
        {
            var message = $"AvaloniaShell 启动失败：{ex.Message}";
            var diagnosticsSummary = BuildDiagnosticsSummary();
            var diagnosticsLogPath = ResolveDiagnosticsLogPath();
            _runtimeState.SetStatus(
                AvaloniaRuntimeStatus.StartFailed,
                message,
                diagnosticsSummary,
                diagnosticsLogPath);
            return AvaloniaShellStartupResult.Failure(message, diagnosticsSummary, diagnosticsLogPath);
        }
    }

    public async Task<bool> StopAsync(TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        if (!_runtimeStarted)
        {
            _runtimeState.SetStatus(AvaloniaRuntimeStatus.UiOnly, "运行链路未启动，无需停机。");
            return true;
        }

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(timeout);

        try
        {
            _runtimeState.SetStatus(AvaloniaRuntimeStatus.Stopping, "正在停止运行链路。");
            await _lifecycleCoordinator.StopAsync(timeoutCts.Token).ConfigureAwait(false);
            _runtimeStarted = false;
            _runtimeState.SetStatus(AvaloniaRuntimeStatus.UiOnly, "运行链路已停止。");
            return true;
        }
        catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested)
        {
            _runtimeState.SetStatus(AvaloniaRuntimeStatus.Stopping, "运行链路停机超时，请查看诊断日志。");
            return false;
        }
        catch
        {
            _runtimeState.SetStatus(AvaloniaRuntimeStatus.Stopping, "运行链路停机失败，请查看诊断日志。");
            return false;
        }
    }

    private string BuildDiagnosticsSummary()
    {
        var report = _diagnosticsStore?.Current ?? StartupDiagnosticsReport.Empty();
        var runtimeRoot = _runtimePaths?.RuntimeDataRoot ?? report.ConfigurationProfile.RuntimeDataRoot;
        return
            $"模块数：{report.ModuleRegistrations.Count}；PLC 设备数：{report.DeviceBindings.Count}；阻断问题数：{report.Issues.Count}；运行目录：{runtimeRoot}";
    }

    private string ResolveDiagnosticsLogPath()
    {
        if (_runtimePaths is null)
        {
            return string.Empty;
        }

        if (Directory.Exists(_runtimePaths.LogDirectory))
        {
            var latestLog = Directory
                .EnumerateFiles(_runtimePaths.LogDirectory, "*.log", SearchOption.TopDirectoryOnly)
                .OrderByDescending(File.GetLastWriteTimeUtc)
                .FirstOrDefault();

            if (!string.IsNullOrWhiteSpace(latestLog))
            {
                return latestLog;
            }
        }

        return _runtimePaths.LogDirectory;
    }
}
