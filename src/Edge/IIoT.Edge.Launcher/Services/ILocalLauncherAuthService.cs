using IIoT.Edge.Launcher.Models;

namespace IIoT.Edge.Launcher.Services;

public interface ILocalLauncherAuthService
{
    LauncherAuthenticationResult Authenticate(string? userName, string? password);

    LauncherPasswordChangeResult ChangePassword(string? userName, string? oldPassword, string? newPassword);
}
