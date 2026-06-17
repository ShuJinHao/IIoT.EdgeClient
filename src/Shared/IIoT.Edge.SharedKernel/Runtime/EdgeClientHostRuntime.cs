using System.Reflection;

namespace IIoT.Edge.SharedKernel.Runtime;

public static class EdgeClientHostRuntime
{
    public const string HostApiVersion = "1.0.0";

    public static string ResolveHostVersion(Assembly assembly)
    {
        ArgumentNullException.ThrowIfNull(assembly);
        return FormatHostVersion(assembly.GetName().Version);
    }

    public static string FormatHostVersion(Version? version)
    {
        if (version is null)
        {
            return "0.0.0";
        }

        return version.Build > 0
            ? $"{version.Major}.{version.Minor}.{version.Build}"
            : $"{version.Major}.{version.Minor}.0";
    }

    public static bool TryParseVersion(string? value, out Version version)
    {
        if (!string.IsNullOrWhiteSpace(value)
            && Version.TryParse(value, out var parsedVersion))
        {
            version = parsedVersion;
            return true;
        }

        version = new Version(0, 0, 0);
        return false;
    }
}
