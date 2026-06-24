using IIoT.Edge.Launcher.Models;

namespace IIoT.Edge.Launcher.Services;

public interface IShellInstanceIdResolver
{
    string? ResolveInstanceId(LauncherProfileDefinition profile);
}
