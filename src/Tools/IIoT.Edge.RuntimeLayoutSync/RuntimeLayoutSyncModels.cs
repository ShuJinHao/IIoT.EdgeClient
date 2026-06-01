internal sealed record RuntimePublishManifest(
    string LauncherDirectory,
    List<RuntimeDefinition> Runtimes);

internal sealed record RuntimeDefinition(
    string RuntimeId,
    string ProfileId,
    string MachineProfile,
    string OutputDirectory,
    string MachineConfig,
    List<string> ModuleIds);

internal sealed record LauncherProfileEntry(
    string ProfileId,
    string DisplayName,
    string MachineProfile,
    string ExecutablePath);

internal sealed record ModuleProject(
    string ModuleId,
    string ProjectPath,
    string ProjectDirectory,
    string TargetFramework);

internal sealed record CommandLineOptions(
    string Configuration,
    string RepoRoot,
    string ManifestPath,
    string LauncherProfileCatalogPath,
    string LayoutRoot,
    string LauncherRuntimeRoot,
    string ShellRuntimeRoot)
{
    public static CommandLineOptions Parse(string[] args)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < args.Length; index++)
        {
            var key = args[index];
            if (!key.StartsWith("--", StringComparison.Ordinal))
            {
                throw new ArgumentException($"Unexpected argument: {key}");
            }

            if (index + 1 >= args.Length)
            {
                throw new ArgumentException($"Missing value for argument: {key}");
            }

            values[key[2..]] = args[++index];
        }

        return new CommandLineOptions(
            Get(values, "configuration", "Debug"),
            Get(values, "repo-root", Directory.GetCurrentDirectory()),
            Get(values, "manifest-path", "scripts/edge-runtime.publish.json"),
            Get(values, "launcher-profile-catalog-path", "src/Edge/IIoT.Edge.Launcher/launcher.profiles.json"),
            Get(values, "layout-root", "../publish/Debug"),
            Get(values, "launcher-runtime-root", "../publish/Debug/launcher"),
            Get(values, "shell-runtime-root", "../publish/Debug/shell"));
    }

    private static string Get(IReadOnlyDictionary<string, string> values, string key, string defaultValue)
        => values.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value)
            ? value
            : defaultValue;
}
