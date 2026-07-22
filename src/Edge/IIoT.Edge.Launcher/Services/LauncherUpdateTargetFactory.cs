using IIoT.Edge.Module.Contracts.Updates;
using IIoT.Edge.Launcher.Models;

namespace IIoT.Edge.Launcher.Services;

public interface ILauncherUpdateTargetFactory
{
    EdgeUpdateTarget Create(LauncherProfileDefinition profile);
}

public sealed class LauncherUpdateTargetFactory : ILauncherUpdateTargetFactory
{
    public EdgeUpdateTarget Create(LauncherProfileDefinition profile)
    {
        ArgumentNullException.ThrowIfNull(profile);

        var hostDirectory = Path.GetDirectoryName(profile.ExecutablePath) ?? AppContext.BaseDirectory;
        return new EdgeUpdateTarget(
            profile.MachineProfile,
            hostDirectory,
            profile.ExecutablePath);
    }
}
