internal sealed class RuntimeLayoutSyncApp(
    IRuntimeLayoutSyncFileSystem fileSystem,
    IRuntimeLayoutSyncValidation validation,
    IRuntimeLayoutSyncModulePublisher modulePublisher) : IRuntimeLayoutSyncApp
{
    public string Run(CommandLineOptions options)
    {
        var repoRoot = fileSystem.ResolvePath(Environment.CurrentDirectory, options.RepoRoot);
        var manifestPath = fileSystem.ResolvePath(repoRoot, options.ManifestPath);
        var profileCatalogPath = fileSystem.ResolvePath(repoRoot, options.LauncherProfileCatalogPath);
        var layoutRoot = fileSystem.ResolvePath(repoRoot, options.LayoutRoot);
        var launcherRuntimeRoot = fileSystem.ResolvePath(repoRoot, options.LauncherRuntimeRoot);
        var shellRuntimeRoot = fileSystem.ResolvePath(repoRoot, options.ShellRuntimeRoot);

        var manifest = fileSystem.LoadJson<RuntimePublishManifest>(manifestPath)
            ?? throw new InvalidOperationException($"Edge runtime publish manifest '{manifestPath}' could not be parsed.");
        var profiles = fileSystem.LoadJson<List<LauncherProfileEntry>>(profileCatalogPath)
            ?? throw new InvalidOperationException($"Launcher profile catalog '{profileCatalogPath}' could not be parsed.");

        validation.ValidateManifest(manifest, manifestPath);
        validation.ValidateProfilesMatchManifest(manifest, profiles, launcherRuntimeRoot, checkExecutablePath: false);

        fileSystem.EnsureDirectoryExists(launcherRuntimeRoot, "Launcher build output was not found");
        fileSystem.EnsureDirectoryExists(shellRuntimeRoot, "Shell build output was not found");

        fileSystem.CopyFile(profileCatalogPath, Path.Combine(launcherRuntimeRoot, "launcher.profiles.json"));
        fileSystem.RemoveLauncherShellArtifacts(launcherRuntimeRoot);

        SyncHostLayout(repoRoot, shellRuntimeRoot, manifest, layoutRoot);
        SyncPluginsLayout(repoRoot, options.Configuration, manifest, layoutRoot);
        fileSystem.CreateDirectory(Path.Combine(layoutRoot, "data"));

        validation.ValidateProfilesMatchManifest(manifest, profiles, launcherRuntimeRoot, checkExecutablePath: true);
        return layoutRoot;
    }

    private void SyncHostLayout(
        string repoRoot,
        string shellRuntimeSource,
        RuntimePublishManifest manifest,
        string layoutRoot)
    {
        var hostRoot = Path.Combine(layoutRoot, fileSystem.NormalizePathSeparators(manifest.HostDirectory));
        if (!IsSameDirectory(shellRuntimeSource, hostRoot))
        {
            fileSystem.CleanDirectory(hostRoot);
            fileSystem.CopyDirectoryContent(shellRuntimeSource, hostRoot);
        }

        fileSystem.DeleteFiles(hostRoot, "appsettings.machine.*.json", SearchOption.TopDirectoryOnly);

        foreach (var profile in manifest.Profiles)
        {
            var machineConfigSource = fileSystem.ResolvePath(repoRoot, profile.MachineConfig);
            if (!fileSystem.FileExists(machineConfigSource))
            {
                throw new FileNotFoundException($"Machine profile config was not found for profile '{profile.ProfileId}': {machineConfigSource}", machineConfigSource);
            }

            fileSystem.CopyFile(machineConfigSource, Path.Combine(hostRoot, Path.GetFileName(machineConfigSource)));
        }

        fileSystem.RemoveDirectoryIfExists(Path.Combine(hostRoot, "Modules"));

        var shellCandidates = validation.GetShellExecutableCandidates(hostRoot).ToArray();
        if (!shellCandidates.Any(fileSystem.FileExists))
        {
            throw new FileNotFoundException(
                $"Shell executable was not found in host directory: {hostRoot}. Candidates: {string.Join(", ", shellCandidates)}",
                hostRoot);
        }
    }

    private static bool IsSameDirectory(string left, string right)
        => string.Equals(
            NormalizeDirectoryPath(left),
            NormalizeDirectoryPath(right),
            StringComparison.OrdinalIgnoreCase);

    private static string NormalizeDirectoryPath(string path)
        => Path.GetFullPath(path)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

    private void SyncPluginsLayout(
        string repoRoot,
        string configuration,
        RuntimePublishManifest manifest,
        string layoutRoot)
    {
        var pluginsRoot = Path.Combine(layoutRoot, fileSystem.NormalizePathSeparators(manifest.PluginsRoot));
        var moduleIds = manifest.Profiles
            .SelectMany(static profile => profile.ModuleIds)
            .Where(static moduleId => !string.IsNullOrWhiteSpace(moduleId))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        modulePublisher.PublishModulesToPluginsRoot(repoRoot, configuration, moduleIds, pluginsRoot);
    }
}
