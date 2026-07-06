using IIoT.Edge.SharedKernel.Security;

namespace IIoT.Edge.Launcher.Services;

public static class LauncherPasswordHasher
{
    public static string HashPassword(string password)
        => EdgePasswordHasher.HashPassword(password);

    public static EdgePasswordVerificationResult Verify(string password, string expectedHash)
        => EdgePasswordHasher.Verify(password, expectedHash);
}
