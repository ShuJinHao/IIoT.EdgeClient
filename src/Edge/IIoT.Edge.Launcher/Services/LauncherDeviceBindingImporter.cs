using System.ComponentModel;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using IIoT.Edge.Infrastructure.HostPersistence;
using IIoT.Edge.Launcher.Models;
using IIoT.Edge.Module.Contracts.Updates;
using IIoT.Edge.SharedKernel.Configuration;
using IIoT.Edge.SharedKernel.Security;

namespace IIoT.Edge.Launcher.Services;

public interface ILauncherDeviceBindingImporter
{
    void ApplyPendingBindings();

    void ApplyPendingBindingsOrThrow() => ApplyPendingBindings();
}

/// <summary>
/// Imports a complete Cloud installation binding. A bundle is an all-or-nothing unit: every
/// ClientCode, machine configuration and credential must validate before any runtime file is
/// switched. Secrets are moved to Windows Credential Manager and are never copied to the
/// runtime binding or machine configuration.
/// </summary>
public sealed class LauncherDeviceBindingImporter : ILauncherDeviceBindingImporter
{
    public const string BindingFileName = "iiot-binding.json";

    private readonly string _baseDirectory;
    private readonly ILauncherProfileCatalog _profileCatalog;
    private readonly IEdgeProfileModuleConfigurationStore _moduleConfiguration;
    private readonly ILauncherUpdateTargetFactory _targetFactory;
    private readonly ILauncherStartupDiagnosticWriter? _startupDiagnostics;
    private readonly IEdgeCredentialStore _credentialStore;

    public LauncherDeviceBindingImporter(
        string baseDirectory,
        ILauncherProfileCatalog profileCatalog,
        IEdgeProfileModuleConfigurationStore moduleConfiguration,
        ILauncherUpdateTargetFactory targetFactory,
        ILauncherStartupDiagnosticWriter? startupDiagnostics = null,
        IEdgeCredentialStore? credentialStore = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(baseDirectory);
        _baseDirectory = baseDirectory;
        _profileCatalog = profileCatalog ?? throw new ArgumentNullException(nameof(profileCatalog));
        _moduleConfiguration = moduleConfiguration ?? throw new ArgumentNullException(nameof(moduleConfiguration));
        _targetFactory = targetFactory ?? throw new ArgumentNullException(nameof(targetFactory));
        _startupDiagnostics = startupDiagnostics;
        _credentialStore = credentialStore ?? new WindowsCredentialManagerStore();
    }

    public void ApplyPendingBindings()
        => ApplyPendingBindingsCore(throwOnFailure: false);

    public void ApplyPendingBindingsOrThrow()
        => ApplyPendingBindingsCore(throwOnFailure: true);

    private void ApplyPendingBindingsCore(bool throwOnFailure)
    {
        var bindingPath = ResolvePendingBindingPath();
        if (bindingPath is null)
        {
            ReplaceBindingDiagnostics([]);
            return;
        }

        try
        {
            var payload = EdgeInstallerBindingCodec.ParsePayload(File.ReadAllText(bindingPath));
            if (payload.SchemaVersion != EdgeInstallerBindingCodec.LegacySchemaVersion)
            {
                throw new InvalidDataException(
                    "Launcher only imports legacy Binding v2; Binding v3 must be materialized by Installer.");
            }
            if (payload.ExpiresAtUtc <= DateTimeOffset.UtcNow)
            {
                throw new InvalidDataException("安装 Binding 已过期，必须重新下载安装包。");
            }

            var runtimeBinding = EdgeInstallerBindingCodec.ToRuntime(payload);
            var preparedFiles = PrepareAllFiles(payload, runtimeBinding);
            ApplyAtomically(payload, preparedFiles);
            WriteAppliedSummary(payload);
            File.Delete(bindingPath);
            ReplaceBindingDiagnostics([]);
        }
        catch (Exception ex) when (IsRecoverable(ex))
        {
            ReplaceBindingDiagnostics(
            [
                CreateBindingDiagnostic(
                    "LAUNCHER_DEVICE_BINDING_IMPORT_FAILED",
                    exceptionType: ex.GetType().Name)
            ]);
            if (throwOnFailure)
            {
                throw new InvalidDataException(
                    "LAUNCHER_DEVICE_BINDING_IMPORT_FAILED: legacy Binding v2 migration did not complete.",
                    ex);
            }
        }
    }

    private IReadOnlyList<PreparedFile> PrepareAllFiles(
        EdgeInstallerBindingEnvelope payload,
        EdgeRuntimeBindingEnvelope runtimeBinding)
    {
        var profiles = _profileCatalog.LoadProfiles();
        var prepared = new List<PreparedFile>(payload.Bindings.Count + 3);
        foreach (var binding in payload.Bindings)
        {
            var sourceConfigPath = ResolveUniqueLegacySourceConfig(profiles, binding);
            var targetConfigPath = EdgeClientProgramDataPaths.ResolveDevicePluginMachineConfigPath(
                binding.ClientCode,
                _baseDirectory);
            prepared.Add(new PreparedFile(
                targetConfigPath,
                Encoding.UTF8.GetBytes(BuildMachineConfiguration(payload, binding, sourceConfigPath))));
        }

        var runtimeBindingPath = EdgeClientProgramDataPaths.ResolveRuntimeBindingPath(_baseDirectory);
        var runtimeJson = EdgeInstallerBindingCodec.SerializeRuntime(runtimeBinding);
        if (runtimeJson.Contains("pendingCredentialSecret", StringComparison.OrdinalIgnoreCase)
            || payload.Bindings.Any(binding => runtimeJson.Contains(
                binding.PendingCredentialSecret,
                StringComparison.Ordinal)))
        {
            throw new InvalidDataException("运行时 Binding 不得包含原始凭证。");
        }

        prepared.Add(new PreparedFile(runtimeBindingPath, Encoding.UTF8.GetBytes(runtimeJson)));

        var hostDatabasePath = EdgeClientProgramDataPaths.ResolveHostDatabasePath(_baseDirectory);
        var hostDatabase = new LauncherHostDatabase(
            hostDatabasePath,
            EdgeClientProgramDataPaths.ResolveLauncherAccountsPath(_baseDirectory));
        var hostDatabaseSnapshot = hostDatabase.PrepareRuntimeBindingImport(runtimeBinding);
        prepared.Add(new PreparedFile(hostDatabasePath, hostDatabaseSnapshot.DatabaseBytes));
        prepared.Add(new PreparedFile(
            hostDatabasePath + ".recovery",
            hostDatabaseSnapshot.DatabaseBytes.ToArray()));
        return prepared;
    }

    private string ResolveUniqueLegacySourceConfig(
        IReadOnlyList<LauncherProfileDefinition> profiles,
        EdgeInstallerDeviceBinding binding)
    {
        var candidates = profiles.Where(profile =>
        {
            try
            {
                return _moduleConfiguration.ReadEnabledModules(_targetFactory.Create(profile))
                    .Contains(binding.ModuleId, StringComparer.OrdinalIgnoreCase);
            }
            catch (Exception ex) when (IsRecoverable(ex))
            {
                return false;
            }
        }).ToArray();
        if (candidates.Length != 1)
        {
            throw new InvalidDataException(
                $"旧 Binding 的模块 {binding.ModuleId} 必须唯一对应一个 Profile，实际为 {candidates.Length} 个。");
        }

        var profile = candidates[0];
        if (!string.IsNullOrWhiteSpace(profile.MachineConfigPath)
            && File.Exists(profile.MachineConfigPath))
        {
            return profile.MachineConfigPath;
        }

        var hostDirectory = Path.GetDirectoryName(profile.ExecutablePath) ?? _baseDirectory;
        var external = EdgeClientProgramDataPaths.ResolveMachineProfileConfigPath(
            profile.MachineProfile,
            hostDirectory);
        if (File.Exists(external))
        {
            return external;
        }

        var packaged = Path.Combine(
            hostDirectory,
            $"appsettings.machine.{profile.MachineProfile}.json");
        return File.Exists(packaged)
            ? packaged
            : throw new FileNotFoundException(
                $"旧 Binding 对应的机器配置不存在：{profile.MachineProfile}。",
                packaged);
    }

    private string BuildMachineConfiguration(
        EdgeInstallerBindingEnvelope payload,
        EdgeInstallerDeviceBinding binding,
        string sourceConfigPath)
    {
        var root = JsonNode.Parse(File.ReadAllText(sourceConfigPath))?.AsObject()
            ?? throw new JsonException("机器配置根节点不能为空。");
        var clientCode = EdgeClientIdentity.NormalizeClientCode(binding.ClientCode);
        var pluginRoot = EdgeClientProgramDataPaths.ResolveDevicePluginRoot(clientCode, _baseDirectory);
        var pluginAppDirectory = Path.Combine(pluginRoot, "app");

        root["InstanceId"] = clientCode;
        var shell = GetOrCreateObject(root, "Shell");
        shell["MachineProfile"] = clientCode;
        shell["ClientCode"] = clientCode;
        shell["RuntimeDataRoot"] = pluginRoot;

        var modules = GetOrCreateObject(root, "Modules");
        modules["Enabled"] = new JsonArray(binding.ModuleId);
        modules["PluginRoots"] = new JsonArray(pluginAppDirectory);

        var cloudApi = GetOrCreateObject(root, "CloudApi");
        cloudApi.Remove("BootstrapSecret");
        cloudApi["Enabled"] = true;
        cloudApi["ClientCode"] = clientCode;
        cloudApi["BootstrapCredentialReference"] = binding.PendingCredentialReference;
        cloudApi["BaseUrl"] = payload.BaseUrl;
        var paths = GetOrCreateObject(cloudApi, "Paths");
        paths["DeviceInstance"] = payload.Paths.DeviceInstance;
        paths["ClientReleaseCatalogTemplate"] = payload.Paths.ClientReleaseCatalogTemplate;
        paths["ClientVersionReport"] = payload.Paths.ClientVersionReport;
        paths["RuntimeHeartbeat"] = payload.Paths.RuntimeHeartbeat;
        SetOptionalPath(paths, "ActivateDevice", payload.Paths.ActivateDevice);
        SetOptionalPath(paths, "ActivateDeviceConfirm", payload.Paths.ActivateDeviceConfirm);
        SetOptionalPath(paths, "PlcSnapshot", payload.Paths.EdgeHostPlcRuntimeStates);
        SetOptionalPath(paths, "PassStationBatch", payload.Paths.PassStationBatchTemplate);

        var bindingFacts = GetOrCreateObject(root, "DevicePluginBinding");
        bindingFacts["SchemaVersion"] = EdgeInstallerBindingCodec.LegacySchemaVersion;
        bindingFacts["GenerationId"] = payload.GenerationId;
        bindingFacts["ClientCode"] = clientCode;
        bindingFacts["DeviceName"] = binding.DeviceName;
        bindingFacts["ProcessId"] = binding.ProcessId.ToString("D");
        bindingFacts["ProcessType"] = binding.ProcessType;
        bindingFacts["ModuleId"] = binding.ModuleId;
        bindingFacts["PluginVersion"] = binding.PluginVersion;
        bindingFacts["PackageSha256"] = binding.PackageSha256;

        return root.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
    }

    private void ApplyAtomically(
        EdgeInstallerBindingEnvelope payload,
        IReadOnlyList<PreparedFile> preparedFiles)
    {
        var stagedFiles = new List<StagedFile>(preparedFiles.Count);
        var credentialBackups = new List<CredentialBackup>(payload.Bindings.Count);
        try
        {
            foreach (var file in preparedFiles)
            {
                var directory = Path.GetDirectoryName(file.TargetPath)
                    ?? throw new InvalidOperationException("Binding 目标缺少目录。");
                Directory.CreateDirectory(directory);
                var temporaryPath = Path.Combine(
                    directory,
                    $".{Path.GetFileName(file.TargetPath)}.{Guid.NewGuid():N}.tmp");
                File.WriteAllBytes(
                    temporaryPath,
                    file.Content);
                using (var stream = new FileStream(
                           temporaryPath,
                           FileMode.Open,
                           FileAccess.ReadWrite,
                           FileShare.None,
                           4096,
                           FileOptions.WriteThrough))
                {
                    stream.Flush(flushToDisk: true);
                }

                stagedFiles.Add(new StagedFile(
                    file.TargetPath,
                    temporaryPath,
                    File.Exists(file.TargetPath) ? File.ReadAllBytes(file.TargetPath) : null));
            }

            foreach (var binding in payload.Bindings)
            {
                var backup = CaptureCredential(binding.PendingCredentialReference);
                credentialBackups.Add(backup);
                _credentialStore.Write(
                    binding.PendingCredentialReference,
                    binding.PendingCredentialSecret);
                var roundTrip = _credentialStore.Read(binding.PendingCredentialReference);
                if (!string.Equals(roundTrip, binding.PendingCredentialSecret, StringComparison.Ordinal))
                {
                    throw new InvalidDataException(
                        $"设备 {binding.ClientCode} 的 pending 凭证回读不一致。");
                }
            }

            foreach (var file in stagedFiles)
            {
                File.Move(file.TemporaryPath, file.TargetPath, overwrite: true);
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
            foreach (var file in stagedFiles)
            {
                TryDelete(file.TemporaryPath);
            }
        }
    }

    private CredentialBackup CaptureCredential(string reference)
    {
        try
        {
            return new CredentialBackup(reference, _credentialStore.Read(reference));
        }
        catch (Win32Exception exception) when (exception.NativeErrorCode == 1168)
        {
            return new CredentialBackup(reference, null);
        }
        catch (KeyNotFoundException)
        {
            return new CredentialBackup(reference, null);
        }
    }

    private void RestoreCredentials(IEnumerable<CredentialBackup> backups)
    {
        foreach (var backup in backups.Reverse())
        {
            try
            {
                if (backup.Secret is null)
                {
                    _credentialStore.Delete(backup.Reference);
                }
                else
                {
                    _credentialStore.Write(backup.Reference, backup.Secret);
                }
            }
            catch (Exception ex) when (IsRecoverable(ex))
            {
                // The original import exception remains authoritative. Installer recovery keeps
                // independent evidence when credential rollback itself fails.
            }
        }
    }

    private static void RestoreFiles(IEnumerable<StagedFile> stagedFiles)
    {
        foreach (var file in stagedFiles.Reverse())
        {
            try
            {
                if (file.OriginalContent is null)
                {
                    TryDelete(file.TargetPath);
                }
                else
                {
                    File.WriteAllBytes(file.TargetPath, file.OriginalContent);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
            }
        }
    }

    private void WriteAppliedSummary(EdgeInstallerBindingEnvelope payload)
    {
        var launcherDirectory = EdgeClientProgramDataPaths.ResolveLauncherDirectory(_baseDirectory);
        Directory.CreateDirectory(launcherDirectory);
        var summary = new JsonObject
        {
            ["schemaVersion"] = payload.SchemaVersion,
            ["generationId"] = payload.GenerationId,
            ["appliedAtUtc"] = DateTimeOffset.UtcNow.ToString("O"),
            ["bindings"] = new JsonArray(payload.Bindings.Select(binding => (JsonNode?)new JsonObject
            {
                ["clientCode"] = binding.ClientCode,
                ["moduleId"] = binding.ModuleId,
                ["pluginVersion"] = binding.PluginVersion,
                ["packageSha256"] = binding.PackageSha256
            }).ToArray())
        };
        var path = Path.Combine(
            launcherDirectory,
            $"iiot-binding.applied.{DateTime.UtcNow:yyyyMMddHHmmssfff}.json");
        File.WriteAllText(
            path,
            summary.ToJsonString(new JsonSerializerOptions { WriteIndented = true }),
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    }

    private string? ResolvePendingBindingPath()
    {
        var path = Path.Combine(
            EdgeClientProgramDataPaths.ResolveLauncherDirectory(_baseDirectory),
            BindingFileName);
        return File.Exists(path) ? path : null;
    }

    private void ReplaceBindingDiagnostics(IReadOnlyCollection<LauncherStartupDiagnostic> values)
        => _startupDiagnostics?.ReplaceArea(LauncherStartupDiagnosticAreas.DeviceBinding, values);

    private static LauncherStartupDiagnostic CreateBindingDiagnostic(
        string reasonCode,
        string? subject = null,
        string? exceptionType = null)
        => new(
            LauncherStartupDiagnosticAreas.DeviceBinding,
            reasonCode,
            LauncherStartupDiagnosticRepairTargets.DeviceBinding,
            subject,
            exceptionType);

    private static JsonObject GetOrCreateObject(JsonObject parent, string propertyName)
    {
        if (parent[propertyName] is JsonObject value)
        {
            return value;
        }

        value = new JsonObject();
        parent[propertyName] = value;
        return value;
    }

    private static void SetOptionalPath(JsonObject paths, string propertyName, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            paths.Remove(propertyName);
        }
        else
        {
            paths[propertyName] = value;
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    private static bool IsRecoverable(Exception exception)
        => exception is IOException
            or UnauthorizedAccessException
            or JsonException
            or InvalidDataException
            or InvalidOperationException
            or ArgumentException
            or PlatformNotSupportedException
            or Win32Exception
            or KeyNotFoundException;

    private sealed record PreparedFile(string TargetPath, byte[] Content);

    private sealed record StagedFile(
        string TargetPath,
        string TemporaryPath,
        byte[]? OriginalContent);

    private sealed record CredentialBackup(string Reference, string? Secret);
}
