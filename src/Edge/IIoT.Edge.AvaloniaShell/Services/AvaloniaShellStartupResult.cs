namespace IIoT.Edge.AvaloniaShell.Services;

public sealed record AvaloniaShellStartupResult(bool Success, bool RuntimeStarted, string? Message = null)
{
    public static AvaloniaShellStartupResult UiOnly()
        => new(Success: true, RuntimeStarted: false);

    public static AvaloniaShellStartupResult RuntimeStartedOk()
        => new(Success: true, RuntimeStarted: true);

    public static AvaloniaShellStartupResult Failure(string message)
        => new(Success: false, RuntimeStarted: false, Message: message);
}

