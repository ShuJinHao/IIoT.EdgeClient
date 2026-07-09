namespace IIoT.Edge.Launcher.Models;

public sealed record LauncherAuthenticationResult(
    bool Success,
    string? UserName,
    string? DisplayName,
    string? ErrorMessage)
{
    public static LauncherAuthenticationResult Passed(LauncherAccountRecord account)
        => new(true, account.UserName, account.DisplayName, null);

    public static LauncherAuthenticationResult Failed(string errorMessage)
        => new(false, null, null, errorMessage);
}

public sealed record LauncherPasswordChangeResult(
    bool Success,
    string? ErrorMessage)
{
    public static LauncherPasswordChangeResult Passed()
        => new(true, null);

    public static LauncherPasswordChangeResult Failed(string errorMessage)
        => new(false, errorMessage);
}

public sealed record LauncherAccountSetupResult(
    bool Success,
    LauncherAccountRecord? Account,
    string? ErrorMessage)
{
    public static LauncherAccountSetupResult Passed(LauncherAccountRecord account)
        => new(true, account, null);

    public static LauncherAccountSetupResult Failed(string errorMessage)
        => new(false, null, errorMessage);
}
