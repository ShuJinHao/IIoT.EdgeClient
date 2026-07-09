using IIoT.Edge.Launcher.Models;

namespace IIoT.Edge.Launcher.Services;

public interface ILauncherAccountCatalog
{
    LauncherAccountCatalogStatus GetCatalogStatus();

    IReadOnlyList<LauncherAccountRecord> LoadAccounts();

    void InitializeAccount(string userName, string displayName, string passwordHash);

    void UpdatePasswordHash(string userName, string passwordHash);

    void UpdateLoginSecurityState(string userName, int accessFailedCount, DateTimeOffset? lockoutUntilUtc);
}
