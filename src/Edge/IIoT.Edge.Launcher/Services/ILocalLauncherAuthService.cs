using IIoT.Edge.Launcher.Models;

namespace IIoT.Edge.Launcher.Services;

public interface ILocalLauncherAuthService
{
    LauncherAuthenticationResult Authenticate(string? userName, string? password);
}
