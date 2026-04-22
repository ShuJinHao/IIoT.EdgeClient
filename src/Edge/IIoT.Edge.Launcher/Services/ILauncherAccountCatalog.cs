using IIoT.Edge.Launcher.Models;

namespace IIoT.Edge.Launcher.Services;

public interface ILauncherAccountCatalog
{
    IReadOnlyList<LauncherAccountRecord> LoadAccounts();
}
