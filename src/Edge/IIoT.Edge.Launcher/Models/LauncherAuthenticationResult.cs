namespace IIoT.Edge.Launcher.Models;

public sealed record LauncherAuthenticationResult(
    bool Success,
    string? DisplayName,
    string? ErrorMessage)
{
    public static LauncherAuthenticationResult Passed(string displayName)
        => new(true, displayName, null);

    public static LauncherAuthenticationResult Failed(string errorMessage)
        => new(false, null, errorMessage);
}
