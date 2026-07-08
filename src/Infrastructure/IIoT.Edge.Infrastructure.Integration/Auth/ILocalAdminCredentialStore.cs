namespace IIoT.Edge.Infrastructure.Integration.Auth;

public interface ILocalAdminCredentialStore
{
    string? ReadPasswordHash();

    void WritePasswordHash(string passwordHash);
}
