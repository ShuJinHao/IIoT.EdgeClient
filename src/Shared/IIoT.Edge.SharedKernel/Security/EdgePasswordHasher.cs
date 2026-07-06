using System.Security.Cryptography;
using System.Text;

namespace IIoT.Edge.SharedKernel.Security;

public enum EdgePasswordVerificationResult
{
    Verified,
    Failed,
    LegacySha256Verified,
    LegacySha256Mismatch,
    InvalidHash
}

public static class EdgePasswordHasher
{
    private const string Algorithm = "pbkdf2-sha256";
    private const string Version = "v1";
    private const int SaltByteLength = 16;
    private const int HashByteLength = 32;
    private const int Iterations = 100_000;

    public static string HashPassword(string password)
    {
        ArgumentNullException.ThrowIfNull(password);

        var salt = RandomNumberGenerator.GetBytes(SaltByteLength);
        var hash = DeriveHash(password, salt, Iterations);
        return string.Join(
            '$',
            Algorithm,
            Version,
            Iterations.ToString(System.Globalization.CultureInfo.InvariantCulture),
            Convert.ToBase64String(salt),
            Convert.ToBase64String(hash));
    }

    public static EdgePasswordVerificationResult Verify(string password, string? storedHash)
    {
        ArgumentNullException.ThrowIfNull(password);

        var hash = storedHash?.Trim();
        if (string.IsNullOrWhiteSpace(hash))
        {
            return EdgePasswordVerificationResult.InvalidHash;
        }

        if (IsLegacySha256Hash(hash))
        {
            return VerifyLegacySha256(password, hash)
                ? EdgePasswordVerificationResult.LegacySha256Verified
                : EdgePasswordVerificationResult.LegacySha256Mismatch;
        }

        var parts = hash.Split('$');
        if (parts.Length != 5
            || !string.Equals(parts[0], Algorithm, StringComparison.Ordinal)
            || !string.Equals(parts[1], Version, StringComparison.Ordinal)
            || !int.TryParse(
                parts[2],
                System.Globalization.NumberStyles.None,
                System.Globalization.CultureInfo.InvariantCulture,
                out var iterations)
            || iterations <= 0)
        {
            return EdgePasswordVerificationResult.InvalidHash;
        }

        try
        {
            var salt = Convert.FromBase64String(parts[3]);
            var expected = Convert.FromBase64String(parts[4]);
            if (salt.Length < SaltByteLength || expected.Length != HashByteLength)
            {
                return EdgePasswordVerificationResult.InvalidHash;
            }

            var actual = DeriveHash(password, salt, iterations);
            return CryptographicOperations.FixedTimeEquals(actual, expected)
                ? EdgePasswordVerificationResult.Verified
                : EdgePasswordVerificationResult.Failed;
        }
        catch (FormatException)
        {
            return EdgePasswordVerificationResult.InvalidHash;
        }
    }

    public static bool IsLegacySha256Hash(string? storedHash)
    {
        var hash = storedHash?.Trim();
        return hash is { Length: 64 } && hash.All(Uri.IsHexDigit);
    }

    private static byte[] DeriveHash(string password, byte[] salt, int iterations)
        => Rfc2898DeriveBytes.Pbkdf2(
            Encoding.UTF8.GetBytes(password),
            salt,
            iterations,
            HashAlgorithmName.SHA256,
            HashByteLength);

    private static bool VerifyLegacySha256(string password, string expectedHash)
    {
        var actual = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(password)));
        return string.Equals(actual, expectedHash, StringComparison.OrdinalIgnoreCase);
    }
}
