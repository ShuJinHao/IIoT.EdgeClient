using System.Security.Principal;

namespace IIoT.Edge.SharedKernel.Security;

public interface IEdgeCredentialOwnerSidProvider
{
    string GetCurrentOwnerSid();
}

public sealed class WindowsCredentialOwnerSidProvider : IEdgeCredentialOwnerSidProvider
{
    public string GetCurrentOwnerSid()
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("Windows user SID is required for Edge credentials.");
        }

        var sid = WindowsIdentity.GetCurrent().User?.Value;
        return Validate(sid);
    }

    public static string Validate(string? value)
    {
        var sid = value?.Trim();
        if (string.IsNullOrWhiteSpace(sid)
            || sid.Length > 184
            || !sid.StartsWith("S-1-", StringComparison.Ordinal)
            || sid[4..].Any(character => !char.IsAsciiDigit(character) && character != '-'))
        {
            throw new InvalidDataException("Credential owner SID is invalid.");
        }

        return sid;
    }
}
