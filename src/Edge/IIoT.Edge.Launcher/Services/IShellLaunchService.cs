using IIoT.Edge.Launcher.Models;

namespace IIoT.Edge.Launcher.Services;

public interface IShellLaunchService
{
    bool HasRunningShellProcess { get; }

    void Launch(LauncherProfileDefinition profile);
}
