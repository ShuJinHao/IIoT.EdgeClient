namespace IIoT.Edge.Module.Abstractions;

public static class ModulePluginHostRuntime
{
    public const string HostApiVersion = "1.0.0";
    public const string HostVersion = "1.0.0";

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
