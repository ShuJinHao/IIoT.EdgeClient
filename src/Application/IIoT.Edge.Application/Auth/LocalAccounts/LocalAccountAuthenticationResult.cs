namespace IIoT.Edge.Application.Auth.LocalAccounts;

public sealed record LocalAccountAuthenticationResult(
    bool Success,
    string? UserName,
    string? DisplayName,
    string? ErrorMessage)
{
    public static LocalAccountAuthenticationResult Passed(LocalAccountRecord account)
        => new(true, account.UserName, account.DisplayName, null);

    public static LocalAccountAuthenticationResult Failed(string errorMessage)
        => new(false, null, null, errorMessage);
}

public sealed record LocalAccountPasswordChangeResult(
    bool Success,
    string? ErrorMessage)
{
    public static LocalAccountPasswordChangeResult Passed()
        => new(true, null);

    public static LocalAccountPasswordChangeResult Failed(string errorMessage)
        => new(false, errorMessage);
}
