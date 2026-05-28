namespace IIoT.Edge.Application.Auth.LocalAccounts;

public interface ILocalAccountAuthService
{
    LocalAccountAuthenticationResult Authenticate(string? userName, string? password);

    LocalAccountPasswordChangeResult ChangePassword(string? userName, string? oldPassword, string? newPassword);
}
