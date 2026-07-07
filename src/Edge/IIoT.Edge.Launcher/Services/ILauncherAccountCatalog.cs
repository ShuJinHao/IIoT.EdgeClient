using IIoT.Edge.Launcher.Models;

namespace IIoT.Edge.Launcher.Services;

public interface ILauncherAccountCatalog
{
    IReadOnlyList<LauncherAccountRecord> LoadAccounts();

    void UpdatePasswordHash(string userName, string passwordHash);

    void UpdateLoginSecurityState(string userName, int accessFailedCount, DateTimeOffset? lockoutUntilUtc);
}
