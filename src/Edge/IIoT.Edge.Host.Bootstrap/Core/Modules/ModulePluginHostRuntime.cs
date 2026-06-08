namespace IIoT.Edge.Host.Bootstrap.Modules;

using IIoT.Edge.SharedKernel.Runtime;

public static class ModulePluginHostRuntime
{
    public const string HostApiVersion = EdgeClientHostRuntime.HostApiVersion;

    public static string HostVersion { get; } = EdgeClientHostRuntime.ResolveHostVersion(
        typeof(ModulePluginHostRuntime).Assembly);

    public static bool TryParseVersion(string? value, out Version version)
        => EdgeClientHostRuntime.TryParseVersion(value, out version);
}
