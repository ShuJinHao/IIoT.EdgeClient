internal sealed class RuntimeLayoutSyncValidation(IRuntimeLayoutSyncFileSystem fileSystem) : IRuntimeLayoutSyncValidation
{
    public void ValidateManifest(RuntimePublishManifest manifest, string manifestPath)
    {
        if (string.IsNullOrWhiteSpace(manifest.LauncherDirectory))
        {
            throw new InvalidOperationException($"Edge runtime publish manifest '{manifestPath}' is missing launcherDirectory.");
        }

        if (string.IsNullOrWhiteSpace(manifest.HostDirectory))
        {
            throw new InvalidOperationException($"Edge runtime publish manifest '{manifestPath}' is missing hostDirectory.");
        }

        if (string.IsNullOrWhiteSpace(manifest.PluginsRoot))
        {
            throw new InvalidOperationException($"Edge runtime publish manifest '{manifestPath}' is missing pluginsRoot.");
        }

        if (manifest.Profiles.Count == 0)
        {
            throw new InvalidOperationException($"Edge runtime publish manifest '{manifestPath}' does not contain any profiles.");
        }

        var profileIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var profile in manifest.Profiles)
        {
            foreach (var (name, value) in new[]
                     {
                         ("profileId", profile.ProfileId),
                         ("machineProfile", profile.MachineProfile),
                         ("machineConfig", profile.MachineConfig)
                     })
            {
                if (string.IsNullOrWhiteSpace(value))
                {
                    throw new InvalidOperationException($"Profile entry in '{manifestPath}' is missing {name}.");
                }
            }

            if (profile.ModuleIds.Count == 0)
            {
                throw new InvalidOperationException($"Profile entry '{profile.ProfileId}' in '{manifestPath}' does not define moduleIds.");
            }

            if (!profileIds.Add(profile.ProfileId))
            {
                throw new InvalidOperationException($"Profile id '{profile.ProfileId}' is duplicated in '{manifestPath}'.");
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

        var publishProfileByProfileId = manifest.Profiles.ToDictionary(profile => profile.ProfileId, StringComparer.OrdinalIgnoreCase);
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
            if (!publishProfileByProfileId.TryGetValue(profile.ProfileId, out var publishProfile))
            {
                throw new InvalidOperationException($"Launcher profile '{profile.ProfileId}' does not match any profileId in edge-runtime.publish.json.");
            }

            if (!string.Equals(profile.MachineProfile, publishProfile.MachineProfile, StringComparison.Ordinal))
            {
                throw new InvalidOperationException($"Launcher profile '{profile.ProfileId}' machineProfile '{profile.MachineProfile}' does not match publish profile machineProfile '{publishProfile.MachineProfile}'.");
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

        foreach (var profile in manifest.Profiles)
        {
            if (!profileIds.Contains(profile.ProfileId))
            {
                throw new InvalidOperationException($"Publish profile '{profile.ProfileId}' is missing from launcher.profiles.json.");
            }
        }
    }

    public IEnumerable<string> GetShellExecutableCandidates(string runtimeRoot)
    {
        return GetExecutableCandidates(
            Path.Combine(runtimeRoot, "IIoT.Edge.Shell"),
            OperatingSystem.IsWindows());
    }

    internal static IReadOnlyList<string> GetExecutableCandidates(string configuredPath, bool isWindows)
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
