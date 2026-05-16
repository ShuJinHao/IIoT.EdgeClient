namespace IIoT.Edge.Launcher.Services;

public interface IPasswordHasher
{
    string HashPassword(string password);

    PasswordVerificationResult VerifyPassword(string password, string storedHash);
}

public sealed record PasswordVerificationResult(bool Success, bool NeedsRehash);
