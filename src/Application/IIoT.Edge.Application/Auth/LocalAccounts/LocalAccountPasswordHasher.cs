using System.Security.Cryptography;
using System.Text;

namespace IIoT.Edge.Application.Auth.LocalAccounts;

public static class LocalAccountPasswordHasher
{
    public static string ComputeSha256(string password)
    {
        ArgumentNullException.ThrowIfNull(password);

        var bytes = Encoding.UTF8.GetBytes(password);
        var hash = SHA256.HashData(bytes);
        return Convert.ToHexString(hash);
    }

    public static bool Verify(string password, string expectedHash)
    {
        ArgumentNullException.ThrowIfNull(password);
        ArgumentException.ThrowIfNullOrWhiteSpace(expectedHash);

        return string.Equals(
            ComputeSha256(password),
            expectedHash.Trim(),
            StringComparison.OrdinalIgnoreCase);
    }
}
