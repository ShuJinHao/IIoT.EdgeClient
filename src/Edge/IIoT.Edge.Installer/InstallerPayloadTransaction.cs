using System.ComponentModel;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using IIoT.Edge.SharedKernel.Configuration;
using IIoT.Edge.SharedKernel.Security;

namespace IIoT.Edge.Installer;

/// <summary>
/// Prepares and atomically switches the mutable part of an Edge installation. The independent
/// recovery journal is intentionally outside host.db so an interrupted install can be rolled
/// back before Launcher is ever started.
/// </summary>
internal sealed class InstallerPayloadTransaction : IDisposable
{
    private const string RecoveryJournalName = ".installer-recovery.json";
    private const int RecoveryJournalSchemaVersion = 2;
    private static readonly string[] PersistentPluginDirectoryNames =
        ["db", "logs", "cache", "context", "buffers", "data"];
    private readonly string _installRoot;
    private readonly string _payloadRoot;
    private readonly string _preparedRoot;
    private readonly string _backupRoot;
    private readonly string _journalPath;
    private readonly IEdgeCredentialStore _credentialStore;
    private readonly InstallerPayloadManifest _payloadManifest;
    private readonly EdgeInstallerBindingEnvelope _payloadBinding;
    private readonly string _credentialOwnerSid;
    private readonly List<SwitchEntry> _entries;
    private readonly List<string> _createdCredentialReferences = [];
    private string? _coreTargetPath;
    private string? _coreBackupPath;
    private bool _coreTargetExisted;
    private bool _coreCaptured;
    private bool _applied;
    private bool _completed;

    private InstallerPayloadTransaction(
        string installRoot,
        string payloadRoot,
        string preparedRoot,
        string backupRoot,
        IEdgeCredentialStore credentialStore,
        string credentialOwnerSid,
        InstallerPayloadManifest payloadManifest,
        EdgeInstallerBindingEnvelope payloadBinding,
        List<SwitchEntry> entries)
    {
        _installRoot = installRoot;
        _payloadRoot = payloadRoot;
        _preparedRoot = preparedRoot;
        _backupRoot = backupRoot;
        _journalPath = Path.Combine(installRoot, RecoveryJournalName);
        _credentialStore = credentialStore;
        _credentialOwnerSid = WindowsCredentialOwnerSidProvider.Validate(credentialOwnerSid);
        _payloadManifest = payloadManifest;
        _payloadBinding = payloadBinding;
        _entries = entries;
    }

    public EdgeInstallerBindingEnvelope PayloadBinding => _payloadBinding;

    /// <summary>
    /// Captures the complete Velopack current directory before the external core installer is
    /// invoked. If setup or post-install validation fails, rollback restores these exact bytes
    /// together with plugin/config/credential state.
    /// </summary>
    public void CaptureCoreState()
    {
        if (_coreCaptured)
        {
            throw new InvalidOperationException("Velopack core state has already been captured.");
        }

        _coreTargetPath = SelfExtractor.GetVelopackCurrentDirectory(_installRoot);
        _coreBackupPath = Path.Combine(_backupRoot, "velopack-current");
        _coreTargetExisted = Directory.Exists(_coreTargetPath);
        WriteJournal("CapturingCore");
        if (_coreTargetExisted)
        {
            CopyDirectory(_coreTargetPath, _coreBackupPath);
        }

        _coreCaptured = true;
        WriteJournal("CoreCaptured");
    }

    public static InstallerPayloadTransaction Prepare(
        string payloadRoot,
        string installRoot,
        IEdgeCredentialStore? credentialStore = null,
        IInstallerPayloadSignatureVerifier? signatureVerifier = null,
        IEdgeCredentialOwnerSidProvider? credentialOwnerSidProvider = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(payloadRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(installRoot);
        var resolvedPayloadRoot = Path.GetFullPath(payloadRoot);
        var resolvedInstallRoot = SelfExtractor.ResolveInstallRoot(installRoot);
        Directory.CreateDirectory(resolvedInstallRoot);
        var store = credentialStore ?? new WindowsCredentialManagerStore();
        var credentialOwnerSid = (credentialOwnerSidProvider ?? new WindowsCredentialOwnerSidProvider())
            .GetCurrentOwnerSid();
        RecoverInterrupted(resolvedInstallRoot, store);

        var manifest = SelfExtractor.ValidatePayloadManifest(resolvedPayloadRoot, signatureVerifier);
        var payloadBindingPath = Path.Combine(resolvedPayloadRoot, "launcher", "iiot-binding.json");
        if (!File.Exists(payloadBindingPath))
        {
            throw new FileNotFoundException("Installer payload binding is missing.", payloadBindingPath);
        }

        var payloadBinding = EdgeInstallerBindingCodec.ParsePayload(File.ReadAllText(payloadBindingPath));
        if (payloadBinding.SchemaVersion != EdgeInstallerBindingCodec.CurrentSchemaVersion)
        {
            throw new InvalidDataException(
                "New installations require Binding v3; Binding v2 remains read-only migration compatibility.");
        }

        if (!string.Equals(manifest.GenerationId, payloadBinding.GenerationId, StringComparison.Ordinal)
            || payloadBinding.ExpiresAtUtc <= DateTimeOffset.UtcNow)
        {
            throw new InvalidDataException("Installer manifest and Binding generation do not match or have expired.");
        }

        ValidateCompleteOfflineRuntime(resolvedPayloadRoot, payloadBinding);

        var transactionId = $"{EdgeClientProgramDataPaths.SanitizePathSegment(payloadBinding.GenerationId)}-{Guid.NewGuid():N}";
        var preparedRoot = Path.Combine(resolvedInstallRoot, ".installer-prepared", transactionId);
        var backupRoot = Path.Combine(resolvedInstallRoot, ".installer-recovery", transactionId);
        Directory.CreateDirectory(preparedRoot);
        Directory.CreateDirectory(backupRoot);

        var entries = PrepareRuntimeFiles(
            resolvedPayloadRoot,
            resolvedInstallRoot,
            preparedRoot,
            payloadBinding,
            credentialOwnerSid);
        return new InstallerPayloadTransaction(
            resolvedInstallRoot,
            resolvedPayloadRoot,
            preparedRoot,
            backupRoot,
            store,
            credentialOwnerSid,
            manifest,
            payloadBinding,
            entries);
    }

    public void Apply()
    {
        if (!_coreCaptured)
        {
            throw new InvalidOperationException(
                "Velopack core state must be captured before applying mutable installation state.");
        }

        if (_applied)
        {
            throw new InvalidOperationException("Installer transaction has already been applied.");
        }

        WriteJournal("Preparing");
        try
        {
            foreach (var binding in _payloadBinding.Bindings)
            {
                var existed = CredentialExists(binding.PendingCredentialReference, out var existingSecret);
                if (existed && !string.Equals(existingSecret, binding.PendingCredentialSecret, StringComparison.Ordinal))
                {
                    throw new InvalidDataException(
                        $"Pending credential reference collision for {binding.ClientCode}.");
                }

                if (!existed)
                {
                    _credentialStore.Write(binding.PendingCredentialReference, binding.PendingCredentialSecret);
                    _createdCredentialReferences.Add(binding.PendingCredentialReference);
                    WriteJournal("CredentialsImported");
                }

                var roundTrip = _credentialStore.Read(binding.PendingCredentialReference);
                if (!string.Equals(roundTrip, binding.PendingCredentialSecret, StringComparison.Ordinal))
                {
                    throw new InvalidDataException(
                        $"Pending credential round-trip verification failed for {binding.ClientCode}.");
                }
            }

            for (var index = 0; index < _entries.Count; index++)
            {
                var entry = _entries[index];
                entry.BackupPath = Path.Combine(_backupRoot, index.ToString("D4"));
                entry.TargetExisted = File.Exists(entry.TargetPath) || Directory.Exists(entry.TargetPath);
                entry.SwitchStarted = true;
                WriteJournal("Switching");
                if (entry.TargetExisted)
                {
                    Move(entry.TargetPath, entry.BackupPath, entry.IsDirectory);
                    WriteJournal("Switching");
                }

                Move(entry.PreparedPath, entry.TargetPath, entry.IsDirectory);
                entry.Switched = true;
                WriteJournal("Switching");
            }

            _applied = true;
            WriteJournal("Applied");
        }
        catch
        {
            Rollback();
            throw;
        }
    }

    public void ValidateInstalledRuntime()
    {
        if (!_applied)
        {
            throw new InvalidOperationException("Installer transaction is not applied.");
        }

        var current = SelfExtractor.GetVelopackCurrentDirectory(_installRoot);
        ValidateInstalledCoreManifest(current, _payloadManifest);
        RequireFile(current, "IIoT.Edge.Launcher.exe");
        RequireFile(current, "IIoT.Edge.Launcher.deps.json");
        RequireFile(current, "IIoT.Edge.Launcher.runtimeconfig.json");
        RequireFile(current, "IIoT.Edge.Shell.exe");
        RequireFile(current, "IIoT.Edge.Shell.deps.json");
        RequireFile(current, "IIoT.Edge.Shell.runtimeconfig.json");
        RequireFile(current, "coreclr.dll");
        RequireFile(current, "hostfxr.dll");
        RequireFile(current, "hostpolicy.dll");
        RequireFile(current, "System.Private.CoreLib.dll");

        var runtimeBindingPath = EdgeClientProgramDataPaths.ResolveRuntimeBindingPath(current);
        var runtimeJson = File.ReadAllText(runtimeBindingPath);
        var runtime = EdgeInstallerBindingCodec.ParseRuntime(runtimeJson);
        if (!string.Equals(runtime.GenerationId, _payloadBinding.GenerationId, StringComparison.Ordinal)
            || runtime.Bindings.Count != _payloadBinding.Bindings.Count
            || runtime.Bindings.Any(binding => !string.Equals(
                binding.CredentialOwnerSid,
                _credentialOwnerSid,
                StringComparison.Ordinal))
            || _payloadBinding.Bindings.Any(binding => runtimeJson.Contains(
                binding.PendingCredentialSecret,
                StringComparison.Ordinal)))
        {
            throw new InvalidDataException("Installed runtime Binding is incomplete or contains a raw secret.");
        }

        foreach (var binding in runtime.Bindings)
        {
            var payloadBinding = _payloadBinding.Bindings.Single(item =>
                string.Equals(item.ClientCode, binding.ClientCode, StringComparison.Ordinal));
            var machineConfigPath = EdgeClientProgramDataPaths.ResolveDevicePluginMachineConfigPath(
                binding.ClientCode,
                current);
            var materializedConfig = JsonNode.Parse(File.ReadAllText(machineConfigPath))?.AsObject()
                ?? throw new InvalidDataException(
                    $"Installed machine configuration is empty for {binding.ClientCode}.");
            EdgeBindingMaterializer.ValidateV3(
                materializedConfig,
                _payloadBinding,
                payloadBinding,
                $"plugins/{binding.ClientCode}",
                binding.PluginDirectory);

            var appRoot = EdgeClientProgramDataPaths.ResolveDevicePluginDirectory(
                binding.ClientCode,
                "app",
                current);
            ValidatePlugin(
                appRoot,
                binding.ModuleId,
                binding.PluginVersion,
                Path.Combine(current, "host"),
                EdgeClientProgramDataPaths.ResolveHostFileManifestPath(current));
        }
    }

    public void Commit()
    {
        if (!_applied)
        {
            throw new InvalidOperationException("Installer transaction cannot commit before apply.");
        }

        _completed = true;
        WriteJournal("Completed");
        TryDeleteDirectory(_backupRoot);
        TryDeleteDirectory(_preparedRoot);
        TryDeleteFile(_journalPath);
    }

    public void Rollback()
    {
        var errors = new List<Exception>();
        foreach (var entry in _entries.AsEnumerable().Reverse())
        {
            try
            {
                RestoreEntry(
                    entry.TargetPath,
                    entry.BackupPath,
                    entry.IsDirectory,
                    entry.TargetExisted,
                    entry.SwitchStarted,
                    entry.Switched,
                    legacyJournal: false);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
            {
                errors.Add(ex);
            }
        }

        foreach (var reference in _createdCredentialReferences.AsEnumerable().Reverse())
        {
            try
            {
                _credentialStore.Delete(reference);
            }
            catch (Exception ex) when (IsCredentialError(ex))
            {
                errors.Add(ex);
            }
        }

        if (_coreCaptured && !string.IsNullOrWhiteSpace(_coreTargetPath))
        {
            try
            {
                if (Directory.Exists(_coreTargetPath))
                {
                    Directory.Delete(_coreTargetPath, recursive: true);
                }

                if (_coreTargetExisted
                    && !string.IsNullOrWhiteSpace(_coreBackupPath)
                    && Directory.Exists(_coreBackupPath))
                {
                    Move(_coreBackupPath, _coreTargetPath, directory: true);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                errors.Add(ex);
            }
        }

        _applied = false;
        if (errors.Count == 0)
        {
            TryDeleteDirectory(_backupRoot);
            TryDeleteDirectory(_preparedRoot);
            TryDeleteFile(_journalPath);
            return;
        }

        WriteJournal("RollbackFailed");
        throw new AggregateException("Installer rollback did not complete; recovery evidence was preserved.", errors);
    }

    public void Dispose()
    {
        if (_applied && !_completed)
        {
            Rollback();
        }
    }

    private bool CredentialExists(string reference, out string? secret)
    {
        try
        {
            secret = _credentialStore.Read(reference);
            return true;
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1168)
        {
            secret = null;
            return false;
        }
        catch (KeyNotFoundException)
        {
            secret = null;
            return false;
        }
    }

    private void WriteJournal(string phase)
    {
        var journal = new RecoveryJournal(
            RecoveryJournalSchemaVersion,
            _payloadBinding.GenerationId,
            phase,
            DateTimeOffset.UtcNow,
            _createdCredentialReferences.ToArray(),
            _coreTargetPath,
            _coreBackupPath,
            _coreTargetExisted,
            _coreCaptured,
            _entries.Select(static entry => new RecoveryEntry(
                entry.TargetPath,
                entry.BackupPath,
                entry.IsDirectory,
                entry.TargetExisted,
                entry.Switched,
                entry.SwitchStarted)).ToArray());
        WriteAtomicJson(_journalPath, journal);
    }

    private static void RecoverInterrupted(string installRoot, IEdgeCredentialStore credentialStore)
    {
        var journalPath = Path.Combine(installRoot, RecoveryJournalName);
        if (!File.Exists(journalPath))
        {
            return;
        }

        var journal = JsonSerializer.Deserialize<RecoveryJournal>(File.ReadAllText(journalPath), JsonOptions)
            ?? throw new InvalidDataException("Installer recovery journal is empty.");
        if (journal.SchemaVersion is not (1 or RecoveryJournalSchemaVersion))
        {
            throw new InvalidDataException("Installer recovery journal version is not supported.");
        }

        var errors = new List<Exception>();
        foreach (var entry in journal.Entries.Reverse())
        {
            try
            {
                RestoreEntry(
                    entry.TargetPath,
                    entry.BackupPath,
                    entry.IsDirectory,
                    entry.TargetExisted,
                    entry.SwitchStarted,
                    entry.Switched,
                    legacyJournal: journal.SchemaVersion == 1);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
            {
                errors.Add(ex);
            }
        }

        foreach (var reference in journal.CreatedCredentialReferences.Reverse())
        {
            try
            {
                credentialStore.Delete(reference);
            }
            catch (Exception ex) when (IsCredentialError(ex))
            {
                errors.Add(ex);
            }
        }

        if (journal.CoreCaptured && !string.IsNullOrWhiteSpace(journal.CoreTargetPath))
        {
            try
            {
                if (Directory.Exists(journal.CoreTargetPath))
                {
                    Directory.Delete(journal.CoreTargetPath, recursive: true);
                }

                if (journal.CoreTargetExisted
                    && !string.IsNullOrWhiteSpace(journal.CoreBackupPath)
                    && Directory.Exists(journal.CoreBackupPath))
                {
                    Move(journal.CoreBackupPath, journal.CoreTargetPath, directory: true);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                errors.Add(ex);
            }
        }

        if (errors.Count != 0)
        {
            throw new AggregateException(
                "Previous installer transaction recovery failed; installation remains blocked.",
                errors);
        }

        TryDeleteFile(journalPath);
    }

    private static List<SwitchEntry> PrepareRuntimeFiles(
        string payloadRoot,
        string installRoot,
        string preparedRoot,
        EdgeInstallerBindingEnvelope payloadBinding,
        string credentialOwnerSid)
    {
        var runtime = EdgeInstallerBindingCodec.ToRuntime(payloadBinding, credentialOwnerSid);
        var launcherPrepared = Path.Combine(preparedRoot, "launcher");
        Directory.CreateDirectory(launcherPrepared);
        var runtimeJson = EdgeInstallerBindingCodec.SerializeRuntime(runtime);
        if (payloadBinding.Bindings.Any(binding => runtimeJson.Contains(
                binding.PendingCredentialSecret,
                StringComparison.Ordinal)))
        {
            throw new InvalidDataException("Runtime Binding contains a raw pending credential.");
        }

        var current = SelfExtractor.GetVelopackCurrentDirectory(installRoot);
        var launcherTarget = EdgeClientProgramDataPaths.ResolveLauncherDirectory(current);
        var entries = new List<SwitchEntry>();
        var plannedDirectories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var preparedRuntimeBinding = Path.Combine(launcherPrepared, EdgeClientProgramDataPaths.RuntimeBindingFileName);
        File.WriteAllText(preparedRuntimeBinding, runtimeJson, Utf8NoBom);
        entries.Add(new SwitchEntry(
            preparedRuntimeBinding,
            EdgeClientProgramDataPaths.ResolveRuntimeBindingPath(current),
            false));

        foreach (var fileName in new[] { "iiot-enabled-plugins.json", "launcher.update.json" })
        {
            var source = Path.Combine(payloadRoot, "launcher", fileName);
            if (!File.Exists(source))
            {
                throw new FileNotFoundException($"Required launcher configuration is missing: {fileName}.", source);
            }

            var prepared = Path.Combine(launcherPrepared, fileName);
            File.Copy(source, prepared, overwrite: true);
            entries.Add(new SwitchEntry(prepared, Path.Combine(launcherTarget, fileName), false));
        }

        var payloadHostManifest = Path.Combine(
            payloadRoot,
            EdgeClientProgramDataPaths.HostFileManifestFileName);
        if (!File.Exists(payloadHostManifest))
        {
            throw new FileNotFoundException(
                "Installer payload is missing the exact Host file manifest.",
                payloadHostManifest);
        }

        var preparedHostManifest = Path.Combine(
            preparedRoot,
            EdgeClientProgramDataPaths.HostFileManifestFileName);
        File.Copy(payloadHostManifest, preparedHostManifest, overwrite: false);
        entries.Add(new SwitchEntry(
            preparedHostManifest,
            EdgeClientProgramDataPaths.ResolveHostFileManifestPath(current),
            false));

        foreach (var binding in payloadBinding.Bindings)
        {
            var sourceRoot = Path.Combine(
                payloadRoot,
                "plugins",
                EdgeClientIdentity.NormalizeClientCode(binding.ClientCode));
            var sourceAppRoot = Path.Combine(sourceRoot, "app");
            var preparedPluginRoot = Path.Combine(
                preparedRoot,
                "plugins",
                EdgeClientIdentity.NormalizeClientCode(binding.ClientCode));
            var appRoot = Path.Combine(preparedPluginRoot, "app");
            CopyDirectory(sourceAppRoot, appRoot);
            var machineTemplates = Directory.EnumerateFiles(
                    appRoot,
                    "appsettings.machine.*.json",
                    SearchOption.AllDirectories)
                .ToArray();
            if (machineTemplates.Length != 1)
            {
                throw new InvalidDataException(
                    $"Plugin {binding.ClientCode} must contain exactly one machine configuration template.");
            }

            var targetPluginRoot = EdgeClientProgramDataPaths.ResolveDevicePluginRoot(
                binding.ClientCode,
                current);
            AddDirectoryCreationEntry(
                entries,
                plannedDirectories,
                preparedRoot,
                targetPluginRoot);
            var targetConfigRoot = EdgeClientProgramDataPaths.ResolveDevicePluginDirectory(
                binding.ClientCode,
                "config",
                current);
            AddDirectoryCreationEntry(
                entries,
                plannedDirectories,
                preparedRoot,
                targetConfigRoot);
            foreach (var child in PersistentPluginDirectoryNames)
            {
                AddDirectoryCreationEntry(
                    entries,
                    plannedDirectories,
                    preparedRoot,
                    EdgeClientProgramDataPaths.ResolveDevicePluginDirectory(
                        binding.ClientCode,
                        child,
                        current));
            }

            entries.Add(new SwitchEntry(
                appRoot,
                EdgeClientProgramDataPaths.ResolveDevicePluginDirectory(
                    binding.ClientCode,
                    "app",
                    current),
                true));

            var configRoot = Path.Combine(preparedPluginRoot, "config");
            Directory.CreateDirectory(configRoot);
            var preparedMachineConfig = Path.Combine(
                configRoot,
                $"appsettings.machine.{binding.ClientCode}.json");
            var machineConfig = BuildMachineConfiguration(payloadBinding, binding, machineTemplates[0]);
            File.WriteAllText(
                preparedMachineConfig,
                machineConfig,
                Utf8NoBom);
            entries.Add(new SwitchEntry(
                preparedMachineConfig,
                EdgeClientProgramDataPaths.ResolveDevicePluginMachineConfigPath(
                    binding.ClientCode,
                    current),
                false));
        }

        return entries;
    }

    private static void AddDirectoryCreationEntry(
        ICollection<SwitchEntry> entries,
        ISet<string> plannedDirectories,
        string preparedRoot,
        string targetDirectory)
    {
        var target = Path.GetFullPath(targetDirectory);
        if (Directory.Exists(target) || !plannedDirectories.Add(target))
        {
            return;
        }

        if (File.Exists(target))
        {
            throw new InvalidDataException(
                $"Installer directory target is occupied by a file: {target}.");
        }

        var prepared = Path.Combine(
            preparedRoot,
            "directory-targets",
            entries.Count.ToString("D4"));
        Directory.CreateDirectory(prepared);
        entries.Add(new SwitchEntry(prepared, target, true));
    }

    private static void RestoreEntry(
        string targetPath,
        string? backupPath,
        bool isDirectory,
        bool targetExisted,
        bool switchStarted,
        bool switched,
        bool legacyJournal)
    {
        var backupExists = !string.IsNullOrWhiteSpace(backupPath)
                           && (File.Exists(backupPath) || Directory.Exists(backupPath));
        var targetExists = File.Exists(targetPath) || Directory.Exists(targetPath);
        if (backupExists)
        {
            if (targetExists)
            {
                Delete(targetPath, isDirectory);
            }

            Move(backupPath!, targetPath, isDirectory);
            return;
        }

        var newlyCreatedTargetMayExist = !targetExisted
                                         && (switchStarted || switched || legacyJournal);
        if (newlyCreatedTargetMayExist && targetExists)
        {
            Delete(targetPath, isDirectory);
            return;
        }

        if (targetExisted && switched)
        {
            throw new InvalidDataException(
                $"Installer rollback cannot restore {targetPath}; its captured backup is missing.");
        }
    }

    private static string BuildMachineConfiguration(
        EdgeInstallerBindingEnvelope payload,
        EdgeInstallerDeviceBinding binding,
        string sourceConfigPath)
    {
        var root = JsonNode.Parse(File.ReadAllText(sourceConfigPath))?.AsObject()
            ?? throw new InvalidDataException("Machine configuration template is empty.");
        var clientCode = EdgeClientIdentity.NormalizeClientCode(binding.ClientCode);
        EdgeBindingMaterializer.MaterializeV3(
            root,
            payload,
            binding,
            $"plugins/{clientCode}",
            binding.PluginDirectory);
        return root.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
    }

    private static void ValidateCompleteOfflineRuntime(
        string payloadRoot,
        EdgeInstallerBindingEnvelope payloadBinding)
    {
        if (SelfExtractor.FindVelopackSetup(payloadRoot) is null)
        {
            throw new FileNotFoundException("Installer payload is missing Velopack Setup.exe.");
        }

        var launcherRoot = Path.Combine(payloadRoot, "launcher");
        var hostRoot = Path.Combine(payloadRoot, "host");
        var defaultCatalogPath = Path.Combine(launcherRoot, "launcher.profiles.json");
        if (File.Exists(defaultCatalogPath))
        {
            throw new InvalidDataException(
                "Production Installer payload must not contain launcher.profiles.json or a Default card.");
        }

        ValidateEnabledPluginSelection(launcherRoot, payloadBinding);
        var hostFileManifestPath = Path.Combine(
            payloadRoot,
            EdgeClientProgramDataPaths.HostFileManifestFileName);
        if (!File.Exists(hostFileManifestPath))
        {
            throw new FileNotFoundException(
                "Installer payload is missing the exact Host file manifest.",
                hostFileManifestPath);
        }
        foreach (var required in new[]
                 {
                     "IIoT.Edge.Launcher.exe",
                     "IIoT.Edge.Launcher.deps.json",
                     "IIoT.Edge.Launcher.runtimeconfig.json",
                     "coreclr.dll",
                     "hostfxr.dll",
                     "hostpolicy.dll",
                     "System.Private.CoreLib.dll"
                 })
        {
            RequireFile(launcherRoot, required);
        }

        foreach (var required in new[]
                 {
                     "IIoT.Edge.Shell.exe",
                     "IIoT.Edge.Shell.deps.json",
                     "IIoT.Edge.Shell.runtimeconfig.json",
                     "coreclr.dll",
                     "hostfxr.dll",
                     "hostpolicy.dll",
                     "System.Private.CoreLib.dll"
                 })
        {
            RequireFile(hostRoot, required);
        }

        ValidateDepsClosure(launcherRoot, "IIoT.Edge.Launcher.deps.json");
        ValidateDepsClosure(hostRoot, "IIoT.Edge.Shell.deps.json");
        foreach (var binding in payloadBinding.Bindings)
        {
            var appRoot = Path.Combine(payloadRoot, binding.PluginDirectory.Replace('/', Path.DirectorySeparatorChar));
            ValidatePlugin(
                appRoot,
                binding.ModuleId,
                binding.PluginVersion,
                hostRoot,
                hostFileManifestPath);
        }
    }

    private static void ValidateEnabledPluginSelection(
        string launcherRoot,
        EdgeInstallerBindingEnvelope payloadBinding)
    {
        var selectionPath = Path.Combine(launcherRoot, "iiot-enabled-plugins.json");
        if (!File.Exists(selectionPath))
        {
            throw new FileNotFoundException(
                "Installer payload is missing iiot-enabled-plugins.json.",
                selectionPath);
        }

        using var document = JsonDocument.Parse(File.ReadAllText(selectionPath));
        var root = document.RootElement;
        if (!root.TryGetProperty("schemaVersion", out var schemaVersion)
            || schemaVersion.ValueKind != JsonValueKind.Number
            || schemaVersion.GetInt32() != 2
            || !root.TryGetProperty("plugins", out var plugins)
            || plugins.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidDataException("Installer enabled-plugin manifest header is invalid.");
        }

        var expected = payloadBinding.Bindings.ToDictionary(
            binding => binding.ClientCode,
            StringComparer.Ordinal);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var plugin in plugins.EnumerateArray())
        {
            var clientCode = EdgeClientIdentity.NormalizeClientCode(
                plugin.GetProperty("clientCode").GetString());
            if (!seen.Add(clientCode)
                || !expected.TryGetValue(clientCode, out var binding)
                || !string.Equals(
                    plugin.GetProperty("pluginDirectory").GetString(),
                    clientCode,
                    StringComparison.OrdinalIgnoreCase)
                || !string.Equals(
                    plugin.GetProperty("moduleId").GetString(),
                    binding.ModuleId,
                    StringComparison.OrdinalIgnoreCase)
                || !string.Equals(
                    plugin.GetProperty("version").GetString(),
                    binding.PluginVersion,
                    StringComparison.Ordinal)
                || !string.Equals(
                    plugin.GetProperty("packageSha256").GetString(),
                    binding.PackageSha256,
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException(
                    $"Installer enabled-plugin facts do not match Binding v3 for {clientCode}.");
            }
        }

        if (seen.Count != expected.Count)
        {
            throw new InvalidDataException(
                "Installer enabled-plugin manifest does not match the exact Binding v3 ClientCode set.");
        }
    }

    internal static void ValidatePlugin(
        string appRoot,
        string expectedModuleId,
        string expectedVersion,
        string hostRoot,
        string hostFileManifestPath)
    {
        if (!Directory.Exists(appRoot))
        {
            throw new DirectoryNotFoundException($"Plugin app directory is missing: {appRoot}.");
        }

        var manifests = Directory.EnumerateFiles(appRoot, "plugin.json", SearchOption.AllDirectories).ToArray();
        if (manifests.Length != 1)
        {
            throw new InvalidDataException(
                $"Plugin {expectedModuleId} must contain exactly one plugin.json; found {manifests.Length}.");
        }

        using var document = JsonDocument.Parse(File.ReadAllText(manifests[0]));
        var root = document.RootElement;
        var moduleId = root.GetProperty("moduleId").GetString();
        var version = root.GetProperty("version").GetString();
        var entryAssembly = root.GetProperty("entryAssembly").GetString();
        if (!string.Equals(moduleId, expectedModuleId, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(version, expectedVersion, StringComparison.OrdinalIgnoreCase)
            || string.IsNullOrWhiteSpace(entryAssembly))
        {
            throw new InvalidDataException($"Plugin manifest facts do not match Binding for {expectedModuleId}.");
        }

        RequireFile(appRoot, Path.GetFileName(entryAssembly));
        if (Directory.EnumerateFiles(appRoot, "appsettings.machine.*.json", SearchOption.AllDirectories).Count() != 1)
        {
            throw new InvalidDataException(
                $"Plugin {expectedModuleId} must contain exactly one machine configuration template.");
        }

        ValidatePluginFileManifest(appRoot, expectedModuleId, expectedVersion);
        ValidatePluginDependencyClosure(
            appRoot,
            hostRoot,
            hostFileManifestPath,
            expectedModuleId,
            expectedVersion,
            entryAssembly);
    }

    internal static void ValidateDepsClosure(string componentRoot, string depsFileName)
    {
        var depsPath = RequireFile(componentRoot, depsFileName);
        using var document = JsonDocument.Parse(File.ReadAllText(depsPath));
        if (!document.RootElement.TryGetProperty("targets", out var targets))
        {
            throw new InvalidDataException($"Dependency file has no targets: {depsPath}.");
        }

        foreach (var target in targets.EnumerateObject())
        {
            foreach (var library in target.Value.EnumerateObject())
            {
                foreach (var assetGroupName in new[] { "runtime", "native", "resources" })
                {
                    if (!library.Value.TryGetProperty(assetGroupName, out var assets)
                        || assets.ValueKind != JsonValueKind.Object)
                    {
                        continue;
                    }

                    foreach (var asset in assets.EnumerateObject())
                    {
                        var assetPath = asset.Name.Replace('\\', '/');
                        if (assetPath.EndsWith("/_._", StringComparison.Ordinal)
                            || string.Equals(assetPath, "_._", StringComparison.Ordinal))
                        {
                            continue;
                        }

                        var publishedRelativePath = ResolvePublishedDependencyPath(
                            assetGroupName,
                            assetPath,
                            asset.Value);
                        var publishedPath = ResolveContainedPath(componentRoot, publishedRelativePath);
                        if (!File.Exists(publishedPath))
                        {
                            throw new FileNotFoundException(
                                $"Dependency closure is incomplete; {publishedRelativePath} declared by " +
                                $"{depsFileName} ({assetPath}) is missing from its exact publish path.");
                        }
                    }
                }
            }
        }
    }

    private static void ValidateInstalledCoreManifest(
        string currentRoot,
        InstallerPayloadManifest payloadManifest)
    {
        var expected = new Dictionary<string, InstallerPayloadManifestFile>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in payloadManifest.Files)
        {
            var payloadPath = file.Path.Replace('\\', '/');
            string? installedPath = null;
            if (payloadPath.StartsWith("host/", StringComparison.OrdinalIgnoreCase))
            {
                installedPath = payloadPath;
            }
            else if (payloadPath.StartsWith("launcher/", StringComparison.OrdinalIgnoreCase))
            {
                var launcherRelative = payloadPath["launcher/".Length..];
                if (launcherRelative is "iiot-binding.json"
                    or "iiot-enabled-plugins.json"
                    or "launcher.update.json")
                {
                    continue;
                }

                installedPath = launcherRelative;
            }

            if (installedPath is null || !expected.TryAdd(installedPath, file))
            {
                if (installedPath is not null)
                {
                    throw new InvalidDataException(
                        $"Installed core manifest contains a duplicate target path: {installedPath}.");
                }
                continue;
            }
        }

        if (expected.Count == 0)
        {
            throw new InvalidDataException("Installer payload declares no Launcher/Host runtime files.");
        }

        var actual = Directory.EnumerateFiles(currentRoot, "*", SearchOption.AllDirectories)
            .Select(path => new
            {
                FullPath = path,
                RelativePath = NormalizeRelativePath(Path.GetRelativePath(currentRoot, path))
            })
            .ToArray();
        var actualPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in actual)
        {
            if (!actualPaths.Add(file.RelativePath)
                || !expected.Remove(file.RelativePath, out var declared))
            {
                throw new InvalidDataException(
                    $"Installed Velopack current contains an undeclared or duplicate file: {file.RelativePath}.");
            }

            ValidateFileFacts(file.FullPath, file.RelativePath, declared.Size, declared.Sha256);
        }

        if (expected.Count != 0)
        {
            throw new FileNotFoundException(
                $"Installed Velopack current is incomplete: {expected.Keys.Order(StringComparer.Ordinal).First()}.");
        }
    }

    private static void ValidatePluginFileManifest(
        string appRoot,
        string expectedModuleId,
        string expectedVersion)
    {
        var manifestPath = Path.Combine(appRoot, "file-manifest.json");
        if (!File.Exists(manifestPath))
        {
            throw new FileNotFoundException(
                $"Plugin {expectedModuleId} file-manifest.json is missing.",
                manifestPath);
        }

        using var document = JsonDocument.Parse(File.ReadAllText(manifestPath));
        var root = document.RootElement;
        if (root.GetProperty("schemaVersion").GetInt32() != 1
            || !string.Equals(root.GetProperty("component").GetString(), expectedModuleId, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(root.GetProperty("version").GetString(), expectedVersion, StringComparison.OrdinalIgnoreCase)
            || !root.TryGetProperty("files", out var files)
            || files.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidDataException($"Plugin {expectedModuleId} file manifest header is invalid.");
        }

        var expected = new Dictionary<string, (long Size, string Sha256)>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in files.EnumerateArray())
        {
            var path = NormalizeRelativePath(item.GetProperty("path").GetString());
            var size = item.GetProperty("size").GetInt64();
            var sha256 = item.GetProperty("sha256").GetString() ?? string.Empty;
            if (string.Equals(path, "file-manifest.json", StringComparison.OrdinalIgnoreCase)
                || size < 0
                || !IsSha256(sha256)
                || !expected.TryAdd(path, (size, sha256)))
            {
                throw new InvalidDataException(
                    $"Plugin {expectedModuleId} file manifest entry is invalid or duplicated: {path}.");
            }
        }

        var actual = Directory.EnumerateFiles(appRoot, "*", SearchOption.AllDirectories)
            .Select(path => new
            {
                FullPath = path,
                RelativePath = NormalizeRelativePath(Path.GetRelativePath(appRoot, path))
            })
            .Where(static item => !string.Equals(
                item.RelativePath,
                "file-manifest.json",
                StringComparison.OrdinalIgnoreCase))
            .ToArray();
        foreach (var item in actual)
        {
            if (!expected.Remove(item.RelativePath, out var declared))
            {
                throw new InvalidDataException(
                    $"Plugin {expectedModuleId} contains an undeclared file: {item.RelativePath}.");
            }

            ValidateFileFacts(item.FullPath, item.RelativePath, declared.Size, declared.Sha256);
        }

        if (expected.Count != 0)
        {
            throw new FileNotFoundException(
                $"Plugin {expectedModuleId} is incomplete: {expected.Keys.Order(StringComparer.Ordinal).First()}.");
        }
    }

    private static void ValidatePluginDependencyClosure(
        string appRoot,
        string hostRoot,
        string hostFileManifestPath,
        string expectedModuleId,
        string expectedPluginVersion,
        string expectedEntryAssembly)
    {
        var closurePath = Path.Combine(appRoot, "dependency-closure.json");
        if (!File.Exists(closurePath))
        {
            throw new FileNotFoundException("Plugin dependency-closure.json is missing.", closurePath);
        }

        using var document = JsonDocument.Parse(File.ReadAllText(closurePath));
        var root = document.RootElement;
        if (root.GetProperty("schemaVersion").GetInt32() != 2
            || !string.Equals(
                root.GetProperty("entryAssembly").GetString(),
                expectedEntryAssembly,
                StringComparison.Ordinal)
            || !root.TryGetProperty("plugin", out var plugin)
            || !string.Equals(
                plugin.GetProperty("moduleId").GetString(),
                expectedModuleId,
                StringComparison.Ordinal)
            || !string.Equals(
                plugin.GetProperty("version").GetString(),
                expectedPluginVersion,
                StringComparison.Ordinal)
            || string.IsNullOrWhiteSpace(plugin.GetProperty("targetRuntime").GetString())
            || !root.TryGetProperty("host", out var host)
            || string.IsNullOrWhiteSpace(host.GetProperty("component").GetString())
            || string.IsNullOrWhiteSpace(host.GetProperty("version").GetString())
            || !IsSha256(host.GetProperty("fileManifestSha256").GetString() ?? string.Empty)
            || !root.TryGetProperty("dependencies", out var dependencies)
            || dependencies.ValueKind != JsonValueKind.Array
            || dependencies.GetArrayLength() == 0)
        {
            throw new InvalidDataException("Plugin dependency closure header is invalid.");
        }

        var hostComponent = host.GetProperty("component").GetString()!;
        var hostVersion = host.GetProperty("version").GetString()!;
        var hostManifestSha256 = host.GetProperty("fileManifestSha256").GetString()!;
        var hostFiles = LoadAndValidateHostFileManifest(
            hostFileManifestPath,
            hostComponent,
            hostVersion,
            hostManifestSha256);

        var unique = new HashSet<string>(StringComparer.Ordinal);
        var entryAssemblySeen = false;
        foreach (var dependency in dependencies.EnumerateArray())
        {
            var library = dependency.GetProperty("library").GetString()?.Trim();
            var libraryVersion = dependency.GetProperty("libraryVersion").GetString()?.Trim();
            var asset = dependency.GetProperty("asset").GetString()?.Replace('\\', '/');
            var kind = dependency.GetProperty("kind").GetString()?.Trim();
            var source = dependency.GetProperty("source").GetString()?.Trim();
            var publishPath = NormalizeRelativePath(
                dependency.GetProperty("publishPath").GetString());
            var owner = dependency.GetProperty("owner").GetString()?.Trim();
            var size = dependency.GetProperty("size").GetInt64();
            var sha256 = dependency.GetProperty("sha256").GetString()?.Trim() ?? string.Empty;
            var version = dependency.GetProperty("version").GetString()?.Trim();
            if (string.IsNullOrWhiteSpace(library)
                || libraryVersion is null
                || string.IsNullOrWhiteSpace(asset)
                || kind is not ("runtime" or "native" or "resources")
                || source is not ("plugin" or "host")
                || string.IsNullOrWhiteSpace(owner)
                || size < 0
                || !IsSha256(sha256)
                || string.IsNullOrWhiteSpace(version)
                || !unique.Add($"{library}\n{asset}\n{kind}\n{publishPath}"))
            {
                throw new InvalidDataException("Plugin dependency closure entry is invalid or duplicated.");
            }

            var dependencyRoot = source == "plugin" ? appRoot : hostRoot;
            var resolvedPath = ResolveContainedPath(dependencyRoot, publishPath);
            if (!File.Exists(resolvedPath))
            {
                throw new FileNotFoundException(
                    $"Plugin dependency {library}/{asset} is missing from its exact {source} path: {publishPath}.");
            }

            ValidateFileFacts(resolvedPath, publishPath, size, sha256);
            if (source == "host")
            {
                if (!string.Equals(owner, hostComponent, StringComparison.Ordinal)
                    || !string.Equals(version, hostVersion, StringComparison.Ordinal)
                    || !hostFiles.Remove(publishPath, out var declaredHost)
                    || declaredHost.Size != size
                    || !string.Equals(declaredHost.Sha256, sha256, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidDataException(
                        $"Host-owned dependency does not match the exact Host manifest: {publishPath}.");
                }
            }
            else if (!string.Equals(owner, expectedModuleId, StringComparison.Ordinal)
                     || !string.Equals(version, expectedPluginVersion, StringComparison.Ordinal))
            {
                throw new InvalidDataException(
                    $"Plugin-owned dependency has inconsistent owner/version facts: {publishPath}.");
            }

            if (string.Equals(publishPath, expectedEntryAssembly, StringComparison.Ordinal))
            {
                if (source != "plugin" || entryAssemblySeen)
                {
                    throw new InvalidDataException(
                        "Plugin entry assembly must be one exact plugin-owned dependency.");
                }

                entryAssemblySeen = true;
            }
        }

        if (!entryAssemblySeen)
        {
            throw new InvalidDataException(
                "Plugin dependency closure does not own the exact entry assembly.");
        }
    }

    private static Dictionary<string, (long Size, string Sha256)> LoadAndValidateHostFileManifest(
        string manifestPath,
        string expectedComponent,
        string expectedVersion,
        string expectedManifestSha256)
    {
        if (!File.Exists(manifestPath))
        {
            throw new FileNotFoundException("Exact Host file manifest is missing.", manifestPath);
        }

        using (var stream = File.OpenRead(manifestPath))
        {
            var actualHash = Convert.ToHexString(SHA256.HashData(stream));
            if (!string.Equals(actualHash, expectedManifestSha256, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException("Exact Host file manifest hash does not match plugin closure.");
            }
        }

        using var document = JsonDocument.Parse(File.ReadAllText(manifestPath));
        var root = document.RootElement;
        if (root.GetProperty("schemaVersion").GetInt32() != 1
            || !string.Equals(root.GetProperty("component").GetString(), expectedComponent, StringComparison.Ordinal)
            || !string.Equals(root.GetProperty("version").GetString(), expectedVersion, StringComparison.Ordinal)
            || !root.TryGetProperty("files", out var files)
            || files.ValueKind != JsonValueKind.Array
            || files.GetArrayLength() == 0)
        {
            throw new InvalidDataException("Exact Host file manifest header is invalid.");
        }

        var result = new Dictionary<string, (long Size, string Sha256)>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in files.EnumerateArray())
        {
            var path = NormalizeRelativePath(item.GetProperty("path").GetString());
            var size = item.GetProperty("size").GetInt64();
            var sha256 = item.GetProperty("sha256").GetString() ?? string.Empty;
            if (size < 0
                || !IsSha256(sha256)
                || string.IsNullOrWhiteSpace(item.GetProperty("type").GetString())
                || !string.Equals(item.GetProperty("component").GetString(), expectedComponent, StringComparison.Ordinal)
                || !string.Equals(item.GetProperty("version").GetString(), expectedVersion, StringComparison.Ordinal)
                || !result.TryAdd(path, (size, sha256)))
            {
                throw new InvalidDataException(
                    $"Exact Host file manifest entry is invalid or duplicated: {path}.");
            }
        }

        return result;
    }

    private static string ResolvePublishedDependencyPath(
        string kind,
        string assetPath,
        JsonElement metadata)
    {
        var normalized = NormalizeRelativePath(assetPath);
        var fileName = Path.GetFileName(normalized);
        if (kind == "resources")
        {
            if (metadata.ValueKind == JsonValueKind.Object
                && metadata.TryGetProperty("locale", out var localeElement)
                && !string.IsNullOrWhiteSpace(localeElement.GetString()))
            {
                return NormalizeRelativePath($"{localeElement.GetString()}/{fileName}");
            }

            return normalized;
        }

        // dotnet publish flattens selected runtime/native assets from lib/ and runtimes/
        // into the application root. Only that deterministic root location is accepted;
        // an arbitrary subdirectory containing a same-named decoy cannot satisfy closure.
        return NormalizeRelativePath(fileName);
    }

    private static string ResolveContainedPath(string root, string relativePath)
    {
        var fullRoot = Path.GetFullPath(root)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var resolved = Path.GetFullPath(Path.Combine(
            fullRoot,
            relativePath.Replace('/', Path.DirectorySeparatorChar)));
        if (!resolved.StartsWith(fullRoot + Path.DirectorySeparatorChar, StringComparison.Ordinal))
        {
            throw new InvalidDataException($"Dependency path escapes component root: {relativePath}.");
        }

        return resolved;
    }

    private static string NormalizeRelativePath(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Contains('\0'))
        {
            throw new InvalidDataException("Relative file path is empty or invalid.");
        }

        var normalized = value.Replace('\\', '/').TrimStart('/');
        if (Path.IsPathRooted(value)
            || normalized.Split('/', StringSplitOptions.RemoveEmptyEntries)
                .Any(static segment => segment is "." or ".."))
        {
            throw new InvalidDataException($"Relative file path is unsafe: {value}.");
        }

        return normalized;
    }

    private static void ValidateFileFacts(
        string fullPath,
        string displayPath,
        long expectedSize,
        string expectedSha256)
    {
        var info = new FileInfo(fullPath);
        if (info.Length != expectedSize)
        {
            throw new InvalidDataException($"File size does not match manifest: {displayPath}.");
        }

        using var stream = File.OpenRead(fullPath);
        var actualHash = Convert.ToHexString(SHA256.HashData(stream));
        if (!string.Equals(actualHash, expectedSha256, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException($"File hash does not match manifest: {displayPath}.");
        }
    }

    private static bool IsSha256(string value)
        => value.Length == 64 && value.All(Uri.IsHexDigit);

    private static string RequireFile(string root, string fileName)
    {
        if (!Directory.Exists(root))
        {
            throw new DirectoryNotFoundException($"Required component directory is missing: {root}.");
        }

        var matches = Directory.EnumerateFiles(root, fileName, SearchOption.AllDirectories).ToArray();
        return matches.Length == 1
            ? matches[0]
            : throw new FileNotFoundException(
                $"Required file {fileName} must exist exactly once under {root}; found {matches.Length}.");
    }

    private static void CopyDirectory(string source, string target)
    {
        if (!Directory.Exists(source))
        {
            throw new DirectoryNotFoundException($"Installer plugin directory is missing: {source}.");
        }

        Directory.CreateDirectory(target);
        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            var destination = Path.Combine(target, Path.GetRelativePath(source, file));
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(file, destination, overwrite: false);
        }
    }

    private static void Move(string source, string target, bool directory)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        if (directory)
        {
            Directory.Move(source, target);
        }
        else
        {
            File.Move(source, target);
        }
    }

    private static void Delete(string path, bool directory)
    {
        if (directory)
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        else if (File.Exists(path))
        {
            File.Delete(path);
        }
    }

    private static void WriteAtomicJson<T>(string path, T value)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporaryPath = $"{path}.{Guid.NewGuid():N}.tmp";
        File.WriteAllText(temporaryPath, JsonSerializer.Serialize(value, JsonOptions), Utf8NoBom);
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

        File.Move(temporaryPath, path, overwrite: true);
    }

    private static bool IsCredentialError(Exception ex)
        => ex is Win32Exception
            or PlatformNotSupportedException
            or UnauthorizedAccessException
            or InvalidDataException
            or ArgumentException
            or KeyNotFoundException;

    private static void TryDeleteFile(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    private static void TryDeleteDirectory(string path)
    {
        try { if (Directory.Exists(path)) Directory.Delete(path, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    private static readonly UTF8Encoding Utf8NoBom = new(false);
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    private sealed class SwitchEntry(string preparedPath, string targetPath, bool isDirectory)
    {
        public string PreparedPath { get; } = preparedPath;
        public string TargetPath { get; } = targetPath;
        public bool IsDirectory { get; } = isDirectory;
        public string? BackupPath { get; set; }
        public bool TargetExisted { get; set; }
        public bool SwitchStarted { get; set; }
        public bool Switched { get; set; }
    }

    private sealed record RecoveryJournal(
        int SchemaVersion,
        string GenerationId,
        string Phase,
        DateTimeOffset UpdatedAtUtc,
        IReadOnlyList<string> CreatedCredentialReferences,
        string? CoreTargetPath,
        string? CoreBackupPath,
        bool CoreTargetExisted,
        bool CoreCaptured,
        IReadOnlyList<RecoveryEntry> Entries);

    private sealed record RecoveryEntry(
        string TargetPath,
        string? BackupPath,
        bool IsDirectory,
        bool TargetExisted,
        bool Switched,
        bool SwitchStarted = false);
}
