namespace IIoT.Edge.Host.Bootstrap.Modules;

public interface IModulePluginCompatibilityPolicy
{
    ModulePluginCompatibilityResult Evaluate(ModulePluginDescriptor descriptor);
}

public sealed record ModulePluginCompatibilityResult(
    bool IsCompatible,
    ModuleCatalogIssue? Issue)
{
    public static ModulePluginCompatibilityResult Compatible()
        => new(true, null);

    public static ModulePluginCompatibilityResult Incompatible(ModuleCatalogIssue issue)
        => new(false, issue);
}

public sealed class ModulePluginCompatibilityPolicy : IModulePluginCompatibilityPolicy
{
    public ModulePluginCompatibilityResult Evaluate(ModulePluginDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(descriptor);

        if (!string.Equals(
                descriptor.HostApiVersion,
                ModulePluginHostRuntime.HostApiVersion,
                StringComparison.OrdinalIgnoreCase))
        {
            return ModulePluginCompatibilityResult.Incompatible(
                CreateIssue(
                    descriptor,
                    $"插件“{descriptor.ModuleId}”要求的运行 API 版本为 {descriptor.HostApiVersion}，当前宿主 API 版本为 {ModulePluginHostRuntime.HostApiVersion}。"));
        }

        _ = ModulePluginHostRuntime.TryParseVersion(descriptor.MinHostVersion, out var minVersion);
        _ = ModulePluginHostRuntime.TryParseVersion(descriptor.MaxHostVersion, out var maxVersion);
        _ = ModulePluginHostRuntime.TryParseVersion(ModulePluginHostRuntime.HostVersion, out var hostVersion);

        if (hostVersion < minVersion || hostVersion > maxVersion)
        {
            return ModulePluginCompatibilityResult.Incompatible(
                CreateIssue(
                    descriptor,
                    $"插件“{descriptor.ModuleId}”支持的宿主版本范围为 {descriptor.MinHostVersion} - {descriptor.MaxHostVersion}，当前宿主版本为 {ModulePluginHostRuntime.HostVersion}。"));
        }

        return ModulePluginCompatibilityResult.Compatible();
    }

    private static ModuleCatalogIssue CreateIssue(
        ModulePluginDescriptor descriptor,
        string message)
        => new(
            "PLUGIN_HOST_VERSION_INCOMPATIBLE",
            message,
            descriptor.ModuleId,
            descriptor.ManifestPath,
            descriptor.EntryAssemblyPath,
            Path.GetFileName(descriptor.PluginDirectory));
}
