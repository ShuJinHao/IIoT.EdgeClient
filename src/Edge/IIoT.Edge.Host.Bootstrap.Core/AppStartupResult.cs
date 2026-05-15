namespace IIoT.Edge.Shell.Core;

public sealed record AppStartupResult(bool Success, string? Message = null)
{
    public static AppStartupResult Ok() => new(true);

    public static AppStartupResult Failure(string message) => new(false, message);
}
