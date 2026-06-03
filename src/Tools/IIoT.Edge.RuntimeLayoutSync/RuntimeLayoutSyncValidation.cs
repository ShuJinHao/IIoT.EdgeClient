internal sealed class RuntimeLayoutSyncValidation(IRuntimeLayoutSyncFileSystem fileSystem) : IRuntimeLayoutSyncValidation
{
    public void ValidateManifest(RuntimePublishManifest manifest, string manifestPath)
    {
        if (string.IsNullOrWhiteSpace(manifest.LauncherDirectory))
        {
            throw new InvalidOperationException($"Edge runtime publish manifest '{manifestPath}' is missing launcherDirectory.");
        }

        if (manifest.Runtimes.Count == 0)
        {
            throw new InvalidOperationException($"Edge runtime publish manifest '{manifestPath}' does not contain any runtimes.");
        }

        var runtimeIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var outputDirectories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var profileIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var runtime in manifest.Runtimes)
        {
            foreach (var (name, value) in new[]
                     {
                         ("runtimeId", runtime.RuntimeId),
                         ("profileId", runtime.ProfileId),
                         ("machineProfile", runtime.MachineProfile),
                         ("outputDirectory", runtime.OutputDirectory),
                         ("machineConfig", runtime.MachineConfig)
                     })
            {
                if (string.IsNullOrWhiteSpace(value))
                {
                    throw new InvalidOperationException($"Runtime entry in '{manifestPath}' is missing {name}.");
                }
            }

            if (runtime.ModuleIds.Count == 0)
            {
                throw new InvalidOperationException($"Runtime entry '{runtime.RuntimeId}' in '{manifestPath}' does not define moduleIds.");
            }

            if (!runtimeIds.Add(runtime.RuntimeId))
            {
                throw new InvalidOperationException($"Runtime id '{runtime.RuntimeId}' is duplicated in '{manifestPath}'.");
            }

            if (!outputDirectories.Add(runtime.OutputDirectory))
            {
                throw new InvalidOperationException($"Runtime outputDirectory '{runtime.OutputDirectory}' is duplicated in '{manifestPath}'.");
            }

            if (!profileIds.Add(runtime.ProfileId))
            {
                throw new InvalidOperationException($"Runtime profileId '{runtime.ProfileId}' is duplicated in '{manifestPath}'.");
            }
        }
    }

    public void ValidateProfilesMatchManifest(
        RuntimePublishManifest manifest,
        IReadOnlyList<LauncherProfileEntry> profiles,
        string launcherRuntimeRoot,
        bool checkExecutablePath)
    {
        if (profiles.Count == 0)
        {
            throw new InvalidOperationException("Launcher profile catalog is empty.");
        }

        var runtimeByProfileId = manifest.Runtimes.ToDictionary(runtime => runtime.ProfileId, StringComparer.OrdinalIgnoreCase);
        var profileIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var profile in profiles)
        {
            foreach (var (name, value) in new[]
                     {
                         ("ProfileId", profile.ProfileId),
                         ("DisplayName", profile.DisplayName),
                         ("MachineProfile", profile.MachineProfile),
                         ("ExecutablePath", profile.ExecutablePath)
                     })
            {
                if (string.IsNullOrWhiteSpace(value))
                {
                    throw new InvalidOperationException($"Launcher profile catalog contains a profile missing {name}.");
                }
            }

            profileIds.Add(profile.ProfileId);
            if (!runtimeByProfileId.TryGetValue(profile.ProfileId, out var runtime))
            {
                throw new InvalidOperationException($"Launcher profile '{profile.ProfileId}' does not match any runtime profileId in edge-runtime.publish.json.");
            }

            if (!string.Equals(profile.MachineProfile, runtime.MachineProfile, StringComparison.Ordinal))
            {
                throw new InvalidOperationException($"Launcher profile '{profile.ProfileId}' machineProfile '{profile.MachineProfile}' does not match runtime machineProfile '{runtime.MachineProfile}'.");
            }

            if (checkExecutablePath)
            {
                var configuredPath = fileSystem.ResolvePath(launcherRuntimeRoot, profile.ExecutablePath);
                var candidates = GetExecutableCandidates(configuredPath, OperatingSystem.IsWindows()).ToArray();
                if (!candidates.Any(fileSystem.FileExists))
                {
                    throw new FileNotFoundException(
                        $"Launcher profile '{profile.ProfileId}' points to a missing executable: {configuredPath}. Candidates: {string.Join(", ", candidates)}",
                        configuredPath);
                }
            }
        }

        foreach (var runtime in manifest.Runtimes)
        {
            if (!profileIds.Contains(runtime.ProfileId))
            {
                throw new InvalidOperationException($"Runtime '{runtime.RuntimeId}' profileId '{runtime.ProfileId}' is missing from launcher.profiles.json.");
            }
        }
    }

    public IEnumerable<string> GetShellExecutableCandidates(string runtimeRoot)
    {
        return GetExecutableCandidates(
            Path.Combine(runtimeRoot, "IIoT.Edge.Shell"),
            OperatingSystem.IsWindows());
    }

    private static IReadOnlyList<string> GetExecutableCandidates(string configuredPath, bool isWindows)
    {
        var candidates = new List<string>();
        var hasExeExtension = HasKnownExtension(configuredPath, ".exe");
        var hasDllExtension = HasKnownExtension(configuredPath, ".dll");

        if (isWindows && !hasExeExtension && !hasDllExtension)
        {
            AddDistinct(candidates, configuredPath + ".exe");
        }
        else if (!isWindows && hasExeExtension)
        {
            AddDistinct(candidates, RemoveKnownExtension(configuredPath, ".exe"));
        }

        if (!hasDllExtension)
        {
            AddDistinct(candidates, configuredPath);
        }

        AddDistinct(candidates, GetDllFallbackPath(configuredPath));
        return candidates;
    }

    private static string GetDllFallbackPath(string configuredPath)
    {
        if (HasKnownExtension(configuredPath, ".dll"))
        {
            return configuredPath;
        }

        if (HasKnownExtension(configuredPath, ".exe"))
        {
            return RemoveKnownExtension(configuredPath, ".exe") + ".dll";
        }

        return configuredPath + ".dll";
    }

    private static bool HasKnownExtension(string path, string extension)
        => path.EndsWith(extension, StringComparison.OrdinalIgnoreCase);

    private static string RemoveKnownExtension(string path, string extension)
    {
        var directory = Path.GetDirectoryName(path) ?? string.Empty;
        var fileName = Path.GetFileName(path);
        var trimmedFileName = fileName.EndsWith(extension, StringComparison.OrdinalIgnoreCase)
            ? fileName[..^extension.Length]
            : fileName;
        return Path.Combine(directory, trimmedFileName);
    }

    private static void AddDistinct(List<string> candidates, string path)
    {
        if (!candidates.Contains(path, StringComparer.OrdinalIgnoreCase))
        {
            candidates.Add(path);
        }
    }
}
