using IIoT.Edge.Launcher.Models;

namespace IIoT.Edge.Launcher.Services;

public interface ILocalLauncherAuthService
{
    LauncherAccountCatalogStatus AccountCatalogStatus { get; }

    LauncherAuthenticationResult Authenticate(string? userName, string? password);

    LauncherAccountSetupResult InitializeLocalAccount(
        string? userName,
        string? displayName,
        string? newPassword,
        string? confirmPassword);

    LauncherPasswordChangeResult ChangePassword(string? userName, string? oldPassword, string? newPassword);
}
