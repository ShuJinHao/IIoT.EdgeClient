using IIoT.Edge.Launcher.Models;

namespace IIoT.Edge.Launcher.Services;

public interface IShellLaunchService
{
    bool HasAnyRunningShellProcess();

    bool IsProfileRunning(LauncherProfileDefinition profile);

    void Launch(LauncherProfileDefinition profile);
}
