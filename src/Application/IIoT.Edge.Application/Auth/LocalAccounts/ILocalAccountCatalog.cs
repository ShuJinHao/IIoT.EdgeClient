namespace IIoT.Edge.Application.Auth.LocalAccounts;

public interface ILocalAccountCatalog
{
    IReadOnlyList<LocalAccountRecord> LoadAccounts();

    void UpdatePasswordHash(string userName, string passwordHash);
}
