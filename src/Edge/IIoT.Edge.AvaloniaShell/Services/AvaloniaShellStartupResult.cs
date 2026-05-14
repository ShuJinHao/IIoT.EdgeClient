namespace IIoT.Edge.AvaloniaShell.Services;

public sealed record AvaloniaShellStartupResult(
    bool Success,
    bool RuntimeStarted,
    string? Message = null,
    string? DiagnosticsSummary = null,
    string? DiagnosticsLogPath = null)
{
    public static AvaloniaShellStartupResult UiOnly()
        => new(Success: true, RuntimeStarted: false);

    public static AvaloniaShellStartupResult RuntimeStartedOk(
        string? diagnosticsSummary = null,
        string? diagnosticsLogPath = null)
        => new(Success: true, RuntimeStarted: true, DiagnosticsSummary: diagnosticsSummary, DiagnosticsLogPath: diagnosticsLogPath);

    public static AvaloniaShellStartupResult Failure(
        string message,
        string? diagnosticsSummary = null,
        string? diagnosticsLogPath = null)
        => new(
            Success: false,
            RuntimeStarted: false,
            Message: message,
            DiagnosticsSummary: diagnosticsSummary,
            DiagnosticsLogPath: diagnosticsLogPath);
}
