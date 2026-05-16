using System.Security.Cryptography;
using System.Text;

namespace IIoT.Edge.Launcher.Services;

public sealed class Pbkdf2PasswordHasher : IPasswordHasher
{
    public const string AlgorithmName = "PBKDF2-SHA256";
    public const int IterationCount = 600_000;
    private const int SaltSize = 16;
    private const int HashSize = 32;

    public string HashPassword(string password)
    {
        ArgumentNullException.ThrowIfNull(password);

        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        var hash = DeriveHash(password, salt, IterationCount, HashSize);
        return string.Join(
            '$',
            AlgorithmName,
            IterationCount.ToString(),
            Convert.ToBase64String(salt),
            Convert.ToBase64String(hash));
    }

    public PasswordVerificationResult VerifyPassword(string password, string storedHash)
    {
        ArgumentNullException.ThrowIfNull(password);
        ArgumentException.ThrowIfNullOrWhiteSpace(storedHash);

        var normalizedHash = storedHash.Trim();
        if (IsLegacySha256Hash(normalizedHash))
        {
            return new PasswordVerificationResult(
                VerifyLegacySha256(password, normalizedHash),
                NeedsRehash: true);
        }

        return VerifyPbkdf2(password, normalizedHash);
    }

    private static PasswordVerificationResult VerifyPbkdf2(string password, string storedHash)
    {
        var parts = storedHash.Split('$');
        if (parts.Length != 4
            || !string.Equals(parts[0], AlgorithmName, StringComparison.Ordinal)
            || !int.TryParse(parts[1], out var iterations)
            || iterations <= 0)
        {
            return new PasswordVerificationResult(false, false);
        }

        try
        {
            var salt = Convert.FromBase64String(parts[2]);
            var expectedHash = Convert.FromBase64String(parts[3]);
            if (salt.Length != SaltSize || expectedHash.Length == 0)
            {
                return new PasswordVerificationResult(false, false);
            }

            var actualHash = DeriveHash(password, salt, iterations, expectedHash.Length);
            var verified = CryptographicOperations.FixedTimeEquals(actualHash, expectedHash);
            return new PasswordVerificationResult(verified, verified && iterations < IterationCount);
        }
        catch (FormatException)
        {
            return new PasswordVerificationResult(false, false);
        }
    }

    private static byte[] DeriveHash(string password, byte[] salt, int iterations, int hashSize)
        => Rfc2898DeriveBytes.Pbkdf2(
            password,
            salt,
            iterations,
            HashAlgorithmName.SHA256,
            hashSize);

    private static bool VerifyLegacySha256(string password, string expectedHash)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(password));
        var actualHash = Convert.ToHexString(bytes);
        return string.Equals(actualHash, expectedHash, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsLegacySha256Hash(string value)
        => value.Length == 64
            && value.IndexOf('$') < 0
            && value.All(static ch => Uri.IsHexDigit(ch));
}
