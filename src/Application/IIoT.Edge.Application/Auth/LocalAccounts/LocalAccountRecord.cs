namespace IIoT.Edge.Application.Auth.LocalAccounts;

public sealed record LocalAccountRecord(
    string UserName,
    string DisplayName,
    string PasswordHash,
    bool IsEnabled);
