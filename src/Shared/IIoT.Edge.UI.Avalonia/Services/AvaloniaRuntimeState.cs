namespace IIoT.Edge.UI.Avalonia.Services;

public sealed class AvaloniaRuntimeState : IAvaloniaRuntimeState
{
    private AvaloniaRuntimeStateSnapshot _snapshot = CreateSnapshot(
        AvaloniaRuntimeStatus.UiOnly,
        "默认 UI-only 模式，运行链路未启动。",
        string.Empty,
        string.Empty);

    public event EventHandler? StateChanged;

    public bool IsRuntimeStarted
        => Snapshot.Status is AvaloniaRuntimeStatus.Running or AvaloniaRuntimeStatus.Stopping;

    public AvaloniaRuntimeStateSnapshot Snapshot => _snapshot;

    public void SetStatus(
        AvaloniaRuntimeStatus status,
        string? detailText = null,
        string? diagnosticsSummary = null,
        string? diagnosticsLogPath = null)
    {
        var next = CreateSnapshot(
            status,
            detailText ?? GetDefaultDetail(status),
            diagnosticsSummary ?? Snapshot.DiagnosticsSummary,
            diagnosticsLogPath ?? Snapshot.DiagnosticsLogPath);

        if (Snapshot.Status == next.Status &&
            string.Equals(Snapshot.DetailText, next.DetailText, StringComparison.Ordinal) &&
            string.Equals(Snapshot.DiagnosticsSummary, next.DiagnosticsSummary, StringComparison.Ordinal) &&
            string.Equals(Snapshot.DiagnosticsLogPath, next.DiagnosticsLogPath, StringComparison.Ordinal))
        {
            return;
        }

        _snapshot = next;
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    public void SetRuntimeStarted(bool isRuntimeStarted)
    {
        SetStatus(isRuntimeStarted ? AvaloniaRuntimeStatus.Running : AvaloniaRuntimeStatus.UiOnly);
    }

    private static AvaloniaRuntimeStateSnapshot CreateSnapshot(
        AvaloniaRuntimeStatus status,
        string detailText,
        string diagnosticsSummary,
        string diagnosticsLogPath)
        => new(
            status,
            GetStatusText(status),
            detailText,
            diagnosticsSummary,
            diagnosticsLogPath,
            DateTimeOffset.Now);

    private static string GetStatusText(AvaloniaRuntimeStatus status)
        => status switch
        {
            AvaloniaRuntimeStatus.UiOnly => "UI-only",
            AvaloniaRuntimeStatus.Starting => "启动中",
            AvaloniaRuntimeStatus.Running => "运行中",
            AvaloniaRuntimeStatus.StartFailed => "启动失败",
            AvaloniaRuntimeStatus.Stopping => "停机中",
            _ => "未知"
        };

    private static string GetDefaultDetail(AvaloniaRuntimeStatus status)
        => status switch
        {
            AvaloniaRuntimeStatus.UiOnly => "默认 UI-only 模式，运行链路未启动。",
            AvaloniaRuntimeStatus.Starting => "正在启动运行链路。",
            AvaloniaRuntimeStatus.Running => "运行链路已启动。",
            AvaloniaRuntimeStatus.StartFailed => "运行链路启动失败。",
            AvaloniaRuntimeStatus.Stopping => "正在停止运行链路。",
            _ => "运行链路状态未知。"
        };
}
