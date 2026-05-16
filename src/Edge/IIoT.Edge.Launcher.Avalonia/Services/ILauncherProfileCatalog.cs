using IIoT.Edge.Launcher.Models;

namespace IIoT.Edge.Launcher.Services;

public interface ILauncherProfileCatalog
{
    IReadOnlyList<LauncherProfileDefinition> LoadProfiles();
}
