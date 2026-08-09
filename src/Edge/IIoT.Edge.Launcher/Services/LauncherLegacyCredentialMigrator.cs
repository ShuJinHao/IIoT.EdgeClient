using System.ComponentModel;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using IIoT.Edge.SharedKernel.Configuration;
using IIoT.Edge.SharedKernel.Security;

namespace IIoT.Edge.Launcher.Services;

public interface ILauncherLegacyCredentialMigrator
{
    void Migrate();
}

/// <summary>
/// Moves legacy plaintext bootstrap and refresh credentials before any Shell can start. The
/// complete inventory is read first; credential round trips, redacted staging and file switches
/// are then treated as one rollback-capable local transaction.
/// </summary>
public sealed class LauncherLegacyCredentialMigrator(
    string baseDirectory,
    IEdgeCredentialStore credentialStore) : ILauncherLegacyCredentialMigrator
{
    private static readonly JsonSerializerOptions WriteOptions = new()
    {
        WriteIndented = true
    };

    public void Migrate()
    {
        var inventory = BuildInventory();
        if (inventory.Files.Count == 0)
        {
            return;
        }

        var credentialBackups = CaptureCredentialBackups(inventory.Credentials.Keys);
        var stagedFiles = new List<StagedFile>(inventory.Files.Count);
        try
        {
            foreach (var credential in inventory.Credentials)
            {
                credentialStore.Write(credential.Key, credential.Value);
                if (!string.Equals(
                        credentialStore.Read(credential.Key),
                        credential.Value,
                        StringComparison.Ordinal))
                {
                    throw new InvalidDataException(
                        "LAUNCHER_LEGACY_CREDENTIAL_ROUNDTRIP_FAILED: Credential Manager round trip failed.");
                }
            }

            foreach (var file in inventory.Files)
            {
                if (inventory.Credentials.Values.Any(secret => file.RedactedBytes.AsSpan().IndexOf(
                        Encoding.UTF8.GetBytes(secret)) >= 0))
                {
                    throw new InvalidDataException(
                        "LAUNCHER_LEGACY_CREDENTIAL_REDACTION_FAILED: redacted staging still contains a credential.");
                }

                var directory = Path.GetDirectoryName(file.Path)
                    ?? throw new InvalidDataException(
                        "LAUNCHER_LEGACY_CREDENTIAL_PATH_INVALID: credential source has no directory.");
                var stagingPath = Path.Combine(
                    directory,
                    $".{Path.GetFileName(file.Path)}.{Guid.NewGuid():N}.credential-staging");
                File.WriteAllBytes(stagingPath, file.RedactedBytes);
                using (var stream = new FileStream(
                           stagingPath,
                           FileMode.Open,
                           FileAccess.ReadWrite,
                           FileShare.None,
                           4096,
                           FileOptions.WriteThrough))
                {
                    stream.Flush(flushToDisk: true);
                }

                _ = JsonNode.Parse(File.ReadAllText(stagingPath))?.AsObject()
                    ?? throw new InvalidDataException(
                        "LAUNCHER_LEGACY_CREDENTIAL_STAGING_INVALID: redacted staging is not a JSON object.");
                stagedFiles.Add(new StagedFile(file.Path, stagingPath, file.OriginalBytes));
            }

            foreach (var staged in stagedFiles)
            {
                File.Move(staged.StagingPath, staged.Path, overwrite: true);
            }

            foreach (var credential in inventory.Credentials)
            {
                if (!string.Equals(
                        credentialStore.Read(credential.Key),
                        credential.Value,
                        StringComparison.Ordinal))
                {
                    throw new InvalidDataException(
                        "LAUNCHER_LEGACY_CREDENTIAL_RECONCILIATION_FAILED: migrated credential no longer matches.");
                }
            }
        }
        catch
        {
            RestoreFiles(stagedFiles);
            RestoreCredentials(credentialBackups);
            throw;
        }
        finally
        {
            foreach (var staged in stagedFiles)
            {
                TryDelete(staged.StagingPath);
            }
        }
    }

    private MigrationInventory BuildInventory()
    {
        var credentials = new Dictionary<string, string>(StringComparer.Ordinal);
        var files = new List<PreparedFile>();
        var cacheOwners = new Dictionary<string, string>(PathComparer());
        var machinePaths = EnumerateMachineConfigurationPaths().Distinct(PathComparer()).ToArray();
        foreach (var path in machinePaths)
        {
            var original = File.ReadAllBytes(path);
            var root = JsonNode.Parse(original)?.AsObject()
                ?? throw new InvalidDataException(
                    "LAUNCHER_LEGACY_MACHINE_CONFIG_INVALID: machine configuration root must be an object.");
            var clientCode = TryReadClientCode(root);
            RegisterKnownCachePaths(root, path, clientCode, cacheOwners);
            if (root["CloudApi"] is not JsonObject cloud
                || cloud["BootstrapSecret"] is not JsonValue secretValue
                || !secretValue.TryGetValue<string>(out var secret)
                || string.IsNullOrWhiteSpace(secret))
            {
                continue;
            }

            var normalizedClientCode = clientCode
                ?? throw new InvalidDataException(
                    "LAUNCHER_LEGACY_BOOTSTRAP_IDENTITY_MISSING: plaintext bootstrap credential has no ClientCode.");
            var reference = ReadString(cloud, "BootstrapCredentialReference")
                ?? WindowsCredentialManagerStore.CreateBootstrapReference(normalizedClientCode);
            AddCredential(credentials, reference, secret);
            cloud.Remove("BootstrapSecret");
            cloud["BootstrapCredentialReference"] = reference;
            files.Add(new PreparedFile(path, original, Serialize(root)));
        }

        foreach (var pluginDirectory in EnumeratePluginDirectories())
        {
            var clientCode = EdgeClientIdentity.NormalizeClientCode(Path.GetFileName(pluginDirectory));
            cacheOwners[Path.GetFullPath(Path.Combine(pluginDirectory, "device_cache.json"))] = clientCode;
        }

        foreach (var cachePath in EnumerateDeviceCachePaths().Distinct(PathComparer()))
        {
            var original = File.ReadAllBytes(cachePath);
            var root = JsonNode.Parse(original)?.AsObject()
                ?? throw new InvalidDataException(
                    "LAUNCHER_LEGACY_SESSION_CACHE_INVALID: device cache root must be an object.");
            var refreshToken = ReadString(root, "RefreshToken");
            var hasAccessToken = HasNonEmptyString(root, "UploadAccessToken")
                                 || HasNonEmptyString(root, "AccessToken");
            if (string.IsNullOrWhiteSpace(refreshToken) && !hasAccessToken)
            {
                continue;
            }

            var clientCode = TryReadClientCode(root);
            if (clientCode is null)
            {
                cacheOwners.TryGetValue(Path.GetFullPath(cachePath), out clientCode);
            }

            if (string.IsNullOrWhiteSpace(clientCode))
            {
                throw new InvalidDataException(
                    "LAUNCHER_LEGACY_SESSION_IDENTITY_MISSING: plaintext session credential has no ClientCode.");
            }

            if (!string.IsNullOrWhiteSpace(refreshToken))
            {
                var reference = ReadString(root, "RefreshCredentialReference")
                    ?? WindowsCredentialManagerStore.CreateSessionReference(clientCode);
                AddCredential(credentials, reference, refreshToken);
                root["RefreshCredentialReference"] = reference;
            }

            root["ClientCode"] = clientCode;
            root.Remove("RefreshToken");
            root.Remove("UploadAccessToken");
            root.Remove("AccessToken");
            root.Remove("UploadAccessTokenExpiresAtUtc");
            files.Add(new PreparedFile(cachePath, original, Serialize(root)));
        }

        return new MigrationInventory(credentials, files);
    }

    private IEnumerable<string> EnumerateMachineConfigurationPaths()
    {
        foreach (var root in new[]
                 {
                     Path.Combine(EdgeClientProgramDataPaths.ResolveConfigRoot(baseDirectory), "profiles"),
                     EdgeClientProgramDataPaths.ResolveApplicationPluginRoot(baseDirectory)
                 })
        {
            if (!Directory.Exists(root))
            {
                continue;
            }

            foreach (var path in Directory.EnumerateFiles(
                         root,
                         "appsettings.machine.*.json",
                         SearchOption.AllDirectories))
            {
                if (!path.Contains(
                        $"{Path.DirectorySeparatorChar}app{Path.DirectorySeparatorChar}",
                        StringComparison.OrdinalIgnoreCase))
                {
                    yield return path;
                }
            }
        }
    }

    private IEnumerable<string> EnumerateDeviceCachePaths()
    {
        foreach (var root in new[]
                 {
                     EdgeClientProgramDataPaths.ResolveApplicationPluginRoot(baseDirectory),
                     EdgeClientProgramDataPaths.ResolveDataRoot(baseDirectory)
                 })
        {
            if (!Directory.Exists(root))
            {
                continue;
            }

            foreach (var path in Directory.EnumerateFiles(
                         root,
                         "device_cache.json",
                         SearchOption.AllDirectories))
            {
                yield return path;
            }
        }
    }

    private IEnumerable<string> EnumeratePluginDirectories()
    {
        var root = EdgeClientProgramDataPaths.ResolveApplicationPluginRoot(baseDirectory);
        return Directory.Exists(root)
            ? Directory.EnumerateDirectories(root)
            : [];
    }

    private void RegisterKnownCachePaths(
        JsonObject root,
        string configPath,
        string? clientCode,
        IDictionary<string, string> cacheOwners)
    {
        if (clientCode is null)
        {
            return;
        }

        if (root["Shell"] is JsonObject shell)
        {
            var runtimeRoot = ReadString(shell, "RuntimeDataRoot");
            if (!string.IsNullOrWhiteSpace(runtimeRoot))
            {
                var expanded = EdgeClientProgramDataPaths.ExpandProgramDataTokens(
                        runtimeRoot,
                        baseDirectory)
                    .Replace('\\', Path.DirectorySeparatorChar)
                    .Replace('/', Path.DirectorySeparatorChar);
                var resolved = Path.GetFullPath(
                    Path.IsPathRooted(expanded)
                        ? expanded
                        : Path.Combine(Path.GetDirectoryName(configPath)!, expanded));
                cacheOwners[Path.Combine(resolved, "device_cache.json")] = clientCode;
            }

            var machineProfile = ReadString(shell, "MachineProfile");
            if (!string.IsNullOrWhiteSpace(machineProfile))
            {
                cacheOwners[Path.GetFullPath(Path.Combine(
                    EdgeClientProgramDataPaths.ResolveProfileDataRoot(machineProfile, baseDirectory),
                    "device_cache.json"))] = clientCode;
            }
        }
    }

    private static string? TryReadClientCode(JsonObject root)
    {
        var candidates = new[]
        {
            root["CloudApi"] is JsonObject cloud ? ReadString(cloud, "ClientCode") : null,
            root["Shell"] is JsonObject shell ? ReadString(shell, "ClientCode") : null,
            ReadString(root, "ClientCode"),
            ReadString(root, "InstanceId")
        };
        foreach (var candidate in candidates)
        {
            if (string.IsNullOrWhiteSpace(candidate))
            {
                continue;
            }

            try
            {
                return EdgeClientIdentity.NormalizeClientCode(candidate);
            }
            catch (ArgumentException)
            {
            }
        }

        return null;
    }

    private IReadOnlyList<CredentialBackup> CaptureCredentialBackups(IEnumerable<string> references)
        => references.Select(reference =>
        {
            try
            {
                return new CredentialBackup(reference, credentialStore.Read(reference));
            }
            catch (Win32Exception exception) when (exception.NativeErrorCode == 1168)
            {
                return new CredentialBackup(reference, null);
            }
            catch (KeyNotFoundException)
            {
                return new CredentialBackup(reference, null);
            }
        }).ToArray();

    private void RestoreCredentials(IEnumerable<CredentialBackup> backups)
    {
        foreach (var backup in backups.Reverse())
        {
            if (backup.Secret is null)
            {
                credentialStore.Delete(backup.Reference);
            }
            else
            {
                credentialStore.Write(backup.Reference, backup.Secret);
            }
        }
    }

    private static void RestoreFiles(IEnumerable<StagedFile> files)
    {
        foreach (var file in files.Reverse())
        {
            File.WriteAllBytes(file.Path, file.OriginalBytes);
        }
    }

    private static void AddCredential(
        IDictionary<string, string> credentials,
        string reference,
        string secret)
    {
        if (credentials.TryGetValue(reference, out var existing)
            && !string.Equals(existing, secret, StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                "LAUNCHER_LEGACY_CREDENTIAL_REFERENCE_CONFLICT: one reference maps to different credentials.");
        }

        credentials[reference] = secret;
    }

    private static byte[] Serialize(JsonObject root)
        => Encoding.UTF8.GetBytes(root.ToJsonString(WriteOptions));

    private static string? ReadString(JsonObject root, string propertyName)
        => root[propertyName] is JsonValue value
           && value.TryGetValue<string>(out var text)
           && !string.IsNullOrWhiteSpace(text)
            ? text.Trim()
            : null;

    private static bool HasNonEmptyString(JsonObject root, string propertyName)
        => ReadString(root, propertyName) is not null;

    private static StringComparer PathComparer()
        => OperatingSystem.IsWindows()
            ? StringComparer.OrdinalIgnoreCase
            : StringComparer.Ordinal;

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }
    }

    private sealed record MigrationInventory(
        IReadOnlyDictionary<string, string> Credentials,
        IReadOnlyList<PreparedFile> Files);

    private sealed record PreparedFile(string Path, byte[] OriginalBytes, byte[] RedactedBytes);

    private sealed record StagedFile(string Path, string StagingPath, byte[] OriginalBytes);

    private sealed record CredentialBackup(string Reference, string? Secret);
}
