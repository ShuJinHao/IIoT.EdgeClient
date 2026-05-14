namespace IIoT.Edge.UI.Avalonia.Services;

public enum AvaloniaRuntimeStatus
{
    UiOnly = 0,
    Starting = 1,
    Running = 2,
    StartFailed = 3,
    Stopping = 4
}

public sealed record AvaloniaRuntimeStateSnapshot(
    AvaloniaRuntimeStatus Status,
    string StatusText,
    string DetailText,
    string DiagnosticsSummary,
    string DiagnosticsLogPath,
    DateTimeOffset UpdatedAt);
