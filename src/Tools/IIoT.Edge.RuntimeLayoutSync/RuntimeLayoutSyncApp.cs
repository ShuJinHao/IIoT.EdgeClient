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

        foreach (var runtime in manifest.Runtimes)
        {
            SyncProcessRuntime(repoRoot, options.Configuration, shellRuntimeRoot, runtime, layoutRoot);
        }

        validation.ValidateProfilesMatchManifest(manifest, profiles, launcherRuntimeRoot, checkExecutablePath: true);
        return layoutRoot;
    }

    private void SyncProcessRuntime(
        string repoRoot,
        string configuration,
        string shellRuntimeSource,
        RuntimeDefinition runtime,
        string layoutRoot)
    {
        var runtimeRoot = Path.Combine(layoutRoot, fileSystem.NormalizePathSeparators(runtime.OutputDirectory));
        fileSystem.CleanDirectory(runtimeRoot);
        fileSystem.CopyDirectoryContent(shellRuntimeSource, runtimeRoot);

        fileSystem.DeleteFiles(runtimeRoot, "appsettings.machine.*.json", SearchOption.TopDirectoryOnly);

        var machineConfigSource = fileSystem.ResolvePath(repoRoot, runtime.MachineConfig);
        if (!fileSystem.FileExists(machineConfigSource))
        {
            throw new FileNotFoundException($"Machine profile config was not found for runtime '{runtime.RuntimeId}': {machineConfigSource}", machineConfigSource);
        }

        fileSystem.CopyFile(machineConfigSource, Path.Combine(runtimeRoot, Path.GetFileName(machineConfigSource)));

        var modulesRoot = Path.Combine(runtimeRoot, "Modules");
        modulePublisher.PublishModulesToRuntimeRoot(repoRoot, configuration, runtime.ModuleIds, modulesRoot);

        var shellCandidates = validation.GetShellExecutableCandidates(runtimeRoot).ToArray();
        if (!shellCandidates.Any(fileSystem.FileExists))
        {
            throw new FileNotFoundException(
                $"Shell executable was not found in runtime directory: {runtimeRoot}. Candidates: {string.Join(", ", shellCandidates)}",
                runtimeRoot);
        }
    }
}
