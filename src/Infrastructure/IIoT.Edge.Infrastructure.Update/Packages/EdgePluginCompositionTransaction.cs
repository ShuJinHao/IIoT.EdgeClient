using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using IIoT.Edge.Application.Features.Updates;
using IIoT.Edge.Module.Contracts.Updates;
using IIoT.Edge.SharedKernel.Configuration;
using IIoT.Edge.SharedKernel.Runtime;

namespace IIoT.Edge.Infrastructure.Update.Packages;

public enum EdgePluginTransactionStage
{
    InstallRecordWritten,
    PackagePrepared,
    ActivationProfileStaged,
    DirectoryMovedBeforeJournal,
    DirectoryReplaced,
    ProfileWritten,
    HostHandoffPending,
    Cleanup,
    JournalRemoval,
    Rollback,
    RollbackPluginRestored
}

public sealed class EdgePluginCompositionTransaction
    : IEdgePluginCompositionTransaction,
      IEdgeUpdateTransactionRecovery
{
    public const string JournalFileName = "update-transaction.json";

    private const int JournalSchemaVersion = 1;
    private const string TransactionsDirectoryName = ".transactions";
    private const string StatePrepared = "prepared";
    private const string StateCommitting = "committing";
    private const string StateHostHandoffPending = "hostHandoffPending";
    private const string StateCleanupPending = "cleanupPending";
    private const string StateRollbackCleanupPending = "rollbackCleanupPending";
    private const string StateRollbackFailed = "rollbackFailed";

    private static readonly JsonSerializerOptions JournalJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    private readonly string _baseDirectory;
    private readonly string _journalPath;
    private readonly EdgePluginPackageInstaller _packageInstaller;
    private readonly IEdgeProfileModuleConfigurationStore _profileStore;
    private readonly Action<EdgePluginTransactionStage>? _faultInjector;
    private bool _blockAllProfiles;

    public EdgePluginCompositionTransaction(
        string baseDirectory,
        EdgePluginPackageInstaller packageInstaller,
        IEdgeProfileModuleConfigurationStore profileStore)
        : this(baseDirectory, packageInstaller, profileStore, faultInjector: null)
    {
    }

    public EdgePluginCompositionTransaction(
        string baseDirectory,
        EdgePluginPackageInstaller packageInstaller,
        IEdgeProfileModuleConfigurationStore profileStore,
        Action<EdgePluginTransactionStage>? faultInjector)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(baseDirectory);
        _baseDirectory = Path.GetFullPath(baseDirectory);
        _packageInstaller = packageInstaller ?? throw new ArgumentNullException(nameof(packageInstaller));
        _profileStore = profileStore ?? throw new ArgumentNullException(nameof(profileStore));
        _faultInjector = faultInjector;
        _journalPath = Path.Combine(
            EdgeClientProgramDataPaths.ResolveLauncherDirectory(baseDirectory),
            JournalFileName);
    }

    public async Task<EdgePluginInstallResult> InstallAsync(
        IReadOnlyList<EdgePluginCompositionTarget> targets,
        IReadOnlyList<EdgePluginCompositionRelease> releases,
        string compatibilityHostVersion,
        string compatibilityHostApiVersion,
        string? pendingHostVersion,
        IProgress<int>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(targets);
        ArgumentNullException.ThrowIfNull(releases);
        if (targets.Count == 0 || releases.Count == 0)
        {
            return EdgePluginInstallResult.Failed("插件组合事务缺少目标或发布包。");
        }

        if (releases
            .GroupBy(static release => release.Release.ModuleId, StringComparer.OrdinalIgnoreCase)
            .Any(static group => group.Count() > 1))
        {
            return EdgePluginInstallResult.Failed("插件组合事务包含重复 ModuleId。");
        }

        var modulePathIssue = ValidateModulePaths(releases);
        if (modulePathIssue is not null)
        {
            return EdgePluginInstallResult.Failed(modulePathIssue);
        }

        var recovery = RecoverPendingTransaction();
        if (!recovery.Success || recovery.Blocked || File.Exists(_journalPath))
        {
            return EdgePluginInstallResult.Failed(
                recovery.ErrorMessage ?? "上一笔更新事务尚未完成清理，禁止开始新事务。");
        }

        var pluginsRoot = ResolveSharedPluginsRoot(targets);
        if (pluginsRoot is null)
        {
            return EdgePluginInstallResult.Failed("本次工序没有共享同一插件目录。");
        }

        UpdateTransactionJournal journal;
        string transactionRoot;
        var journalPersisted = false;
        try
        {
            var transactionId = Guid.NewGuid().ToString("N");
            var transactionRelativePath = Path.Combine(
                TransactionsDirectoryName,
                transactionId);
            transactionRoot = ResolveRelativePath(pluginsRoot, transactionRelativePath);
            journal = CreateJournal(
                transactionId,
                transactionRelativePath,
                pluginsRoot,
                targets,
                releases.Select(static item => item.Release).ToArray(),
                pendingHostVersion);
        }
        catch (Exception ex) when (ex is IOException
                                       or UnauthorizedAccessException
                                       or InvalidOperationException
                                       or ArgumentException)
        {
            return EdgePluginInstallResult.Failed(
                $"插件组合事务无法建立安全快照: {ex.GetType().Name}");
        }

        try
        {
            Directory.CreateDirectory(transactionRoot);
            var prepared = new List<PreparedEdgePluginPackage>();
            for (var index = 0; index < releases.Count; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var release = releases[index];
                var stagingRoot = Path.Combine(
                    transactionRoot,
                    "staging",
                    EdgeClientProgramDataPaths.SanitizePathSegment(release.Release.ModuleId));
                prepared.Add(await _packageInstaller
                    .PrepareAsync(
                        stagingRoot,
                        release.Release,
                        release.CloudOptions,
                        compatibilityHostVersion,
                        compatibilityHostApiVersion,
                        ScaleProgress(progress, index, releases.Count, 0, 55),
                        cancellationToken)
                    .ConfigureAwait(false));
                _faultInjector?.Invoke(EdgePluginTransactionStage.InstallRecordWritten);
                _faultInjector?.Invoke(EdgePluginTransactionStage.PackagePrepared);
            }

            SnapshotProfiles(journal, pluginsRoot);
            StageProfiles(
                journal,
                pluginsRoot,
                targets,
                prepared);
            WriteJournal(journal);
            journalPersisted = true;

            journal.State = StateCommitting;
            WriteJournal(journal);
            for (var index = 0; index < prepared.Count; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var package = prepared[index];
                var entry = journal.Plugins.Single(item =>
                    string.Equals(
                        item.ModuleId,
                        package.ModuleId,
                        StringComparison.OrdinalIgnoreCase));
                var moduleDirectory = ResolveRelativePath(pluginsRoot, entry.ModulePath);
                var backupDirectory = ResolveRelativePath(pluginsRoot, entry.BackupPath);
                entry.CommitStarted = true;
                WriteJournal(journal);
                EdgePluginPackageInstaller.CommitPreparedDirectory(
                    moduleDirectory,
                    package.ExtractDirectory,
                    backupDirectory);
                _faultInjector?.Invoke(
                    EdgePluginTransactionStage.DirectoryMovedBeforeJournal);
                entry.Committed = true;
                WriteJournal(journal);
                _faultInjector?.Invoke(EdgePluginTransactionStage.DirectoryReplaced);
                progress?.Report(55 + (index + 1) * 25 / prepared.Count);
            }

            foreach (var target in targets)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var profileEntry = journal.Profiles.Single(item =>
                    string.Equals(
                        item.MachineProfile,
                        target.Target.MachineProfile,
                        StringComparison.OrdinalIgnoreCase));
                CommitStagedProfile(profileEntry, pluginsRoot);
                WriteJournal(journal);
                _faultInjector?.Invoke(EdgePluginTransactionStage.ProfileWritten);
            }

            if (!string.IsNullOrWhiteSpace(pendingHostVersion))
            {
                journal.State = StateHostHandoffPending;
                WriteJournal(journal);
                _faultInjector?.Invoke(EdgePluginTransactionStage.HostHandoffPending);
            }
            else
            {
                journal.State = StateCleanupPending;
                WriteJournal(journal);
                FinalizeCommittedTransaction(journal, pluginsRoot);
            }

            progress?.Report(100);
            return EdgePluginInstallResult.Succeeded(
                releases
                    .Select(static release => release.Release.ModuleId)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray());
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            if (journalPersisted)
            {
                var rollback = RollbackJournal(journal, pluginsRoot);
                if (!rollback.Success)
                {
                    return EdgePluginInstallResult.Failed(
                        $"插件组合事务已取消，但旧组合回滚失败：{rollback.ErrorMessage}");
                }
            }
            else
            {
                EdgePluginPackageInstaller.TryDeleteDirectory(transactionRoot);
            }

            throw;
        }
        catch (Exception ex)
        {
            if (!journalPersisted)
            {
                EdgePluginPackageInstaller.TryDeleteDirectory(transactionRoot);
                return EdgePluginInstallResult.Failed(
                    $"插件组合事务失败: {ex.Message}");
            }

            var rollback = RollbackJournal(journal, pluginsRoot);
            var detail = rollback.Success
                ? ex.Message
                : $"{ex.Message}；{rollback.ErrorMessage}";
            return EdgePluginInstallResult.Failed($"插件组合事务失败: {detail}");
        }
    }

    public EdgePluginInstallResult RollbackPendingHostHandoff()
    {
        try
        {
            var journal = ReadJournal(out var error);
            if (journal is null)
            {
                return EdgePluginInstallResult.Failed(
                    error ?? "没有可回滚的 Host 交接事务。");
            }

            var pluginsRoot = ResolveJournalPluginsRoot(journal);
            if (pluginsRoot is null)
            {
                return EdgePluginInstallResult.Failed("更新事务插件根目录无效。");
            }

            return RollbackJournal(journal, pluginsRoot).Success
                ? EdgePluginInstallResult.Succeeded([])
                : EdgePluginInstallResult.Failed(
                    ReadJournal(out _)?.LastError ?? "Host 交接回滚失败。");
        }
        catch (Exception ex) when (IsRecoveryException(ex))
        {
            _blockAllProfiles = true;
            return EdgePluginInstallResult.Failed(
                $"Host 交接回滚失败: {ex.GetType().Name}");
        }
    }

    public EdgeUpdateTransactionRecoveryResult RecoverPendingTransaction()
    {
        try
        {
            return RecoverPendingTransactionCore();
        }
        catch (Exception ex) when (IsRecoveryException(ex))
        {
            _blockAllProfiles = true;
            return new EdgeUpdateTransactionRecoveryResult(
                Success: false,
                Recovered: false,
                Blocked: true,
                ErrorMessage: $"更新事务恢复失败: {ex.GetType().Name}");
        }
    }

    private EdgeUpdateTransactionRecoveryResult RecoverPendingTransactionCore()
    {
        if (!File.Exists(_journalPath))
        {
            _blockAllProfiles = false;
            return new EdgeUpdateTransactionRecoveryResult(
                Success: true,
                Recovered: false,
                Blocked: false);
        }

        var journal = ReadJournal(out var error);
        if (journal is null)
        {
            _blockAllProfiles = true;
            return new EdgeUpdateTransactionRecoveryResult(
                Success: false,
                Recovered: false,
                Blocked: true,
                ErrorMessage: error ?? "更新事务日志无效。");
        }

        var pluginsRoot = ResolveJournalPluginsRoot(journal);
        if (pluginsRoot is null)
        {
            _blockAllProfiles = true;
            return new EdgeUpdateTransactionRecoveryResult(
                Success: false,
                Recovered: false,
                Blocked: true,
                ErrorMessage: "更新事务插件根目录无效。");
        }

        if (string.Equals(journal.State, StateRollbackFailed, StringComparison.Ordinal))
        {
            return new EdgeUpdateTransactionRecoveryResult(
                Success: false,
                Recovered: false,
                Blocked: true,
                ErrorMessage: journal.LastError ?? "上一笔更新回滚失败。");
        }

        if (string.Equals(journal.State, StateCleanupPending, StringComparison.Ordinal))
        {
            var cleaned = FinalizeCommittedTransaction(journal, pluginsRoot);
            return new EdgeUpdateTransactionRecoveryResult(
                Success: true,
                Recovered: cleaned,
                Blocked: false,
                ErrorMessage: cleaned ? null : "已提交更新的备份清理待重试。");
        }

        if (string.Equals(
                journal.State,
                StateRollbackCleanupPending,
                StringComparison.Ordinal))
        {
            var cleaned = FinalizeRolledBackTransaction(journal, pluginsRoot);
            return new EdgeUpdateTransactionRecoveryResult(
                Success: true,
                Recovered: cleaned,
                Blocked: false,
                ErrorMessage: cleaned ? null : "已回滚更新的备份清理待重试。");
        }

        if (string.Equals(journal.State, StateHostHandoffPending, StringComparison.Ordinal)
            && IsExpectedHostHandoffState(journal, pluginsRoot))
        {
            journal.State = StateCleanupPending;
            WriteJournal(journal);
            var cleaned = FinalizeCommittedTransaction(journal, pluginsRoot);
            return new EdgeUpdateTransactionRecoveryResult(
                Success: true,
                Recovered: cleaned,
                Blocked: false,
                ErrorMessage: cleaned ? null : "已提交更新的备份清理待重试。");
        }

        var rollback = RollbackJournal(journal, pluginsRoot);
        return new EdgeUpdateTransactionRecoveryResult(
            Success: rollback.Success,
            Recovered: rollback.Success,
            Blocked: !rollback.Success,
            ErrorMessage: rollback.ErrorMessage);
    }

    public bool IsProfileBlocked(string machineProfile)
    {
        if (_blockAllProfiles)
        {
            return true;
        }

        if (!File.Exists(_journalPath))
        {
            return false;
        }

        try
        {
            var journal = ReadJournal(out _);
            if (journal is null)
            {
                _blockAllProfiles = true;
                return true;
            }

            if (string.Equals(journal.State, StateCleanupPending, StringComparison.Ordinal)
                || string.Equals(
                    journal.State,
                    StateRollbackCleanupPending,
                    StringComparison.Ordinal))
            {
                return false;
            }

            return journal.Profiles.Count == 0
                   || journal.Profiles.Any(profile =>
                       string.Equals(
                           profile.MachineProfile,
                           machineProfile,
                           StringComparison.OrdinalIgnoreCase));
        }
        catch (Exception ex) when (IsRecoveryException(ex))
        {
            _blockAllProfiles = true;
            return true;
        }
    }

    private static string? ValidateModulePaths(
        IReadOnlyList<EdgePluginCompositionRelease> releases)
    {
        var modulePaths = releases
            .Select(static release =>
                EdgeClientProgramDataPaths.SanitizePathSegment(
                    release.Release.ModuleId))
            .ToArray();
        if (modulePaths.Any(static segment =>
                !string.Equals(
                    segment,
                    segment.TrimEnd(' ', '.'),
                    StringComparison.Ordinal)))
        {
            return "插件 ModuleId 映射到 Windows 尾随句点或空格别名。";
        }

        if (modulePaths.Any(static segment =>
                string.Equals(
                    segment,
                    TransactionsDirectoryName,
                    StringComparison.OrdinalIgnoreCase)))
        {
            return "插件 ModuleId 映射到保留的事务目录。";
        }

        return modulePaths
            .GroupBy(static segment => segment, StringComparer.OrdinalIgnoreCase)
            .Any(static group => group.Count() > 1)
            ? "不同插件 ModuleId 映射到同一插件目录。"
            : null;
    }

    private UpdateTransactionJournal CreateJournal(
        string transactionId,
        string transactionRelativePath,
        string pluginsRoot,
        IReadOnlyList<EdgePluginCompositionTarget> targets,
        IReadOnlyList<EdgePluginVersionRelease> releases,
        string? pendingHostVersion)
    {
        var configRoot = EdgeClientProgramDataPaths.ResolveConfigRoot(_baseDirectory);
        var plugins = releases
            .Select(release =>
            {
                var segment = EdgeClientProgramDataPaths.SanitizePathSegment(release.ModuleId);
                return new PluginJournalEntry
                {
                    ModuleId = release.ModuleId,
                    Version = release.PackageVersion,
                    PackageSha256 = release.Sha256,
                    ModulePath = segment,
                    BackupPath = Path.Combine(
                        transactionRelativePath,
                        "backups",
                        "plugins",
                        segment),
                    OriginalExists = Directory.Exists(Path.Combine(pluginsRoot, segment))
                };
            })
            .ToList();
        var profiles = targets
            .DistinctBy(
                static item => item.Target.MachineProfile,
                StringComparer.OrdinalIgnoreCase)
            .Select(item =>
            {
                var configPath = EdgeClientProgramDataPaths.ResolveMachineProfileConfigPath(
                    item.Target.MachineProfile,
                    item.Target.HostDirectory);
                var relativeConfigPath = GetSafeRelativePath(configRoot, configPath);
                var profileSegment = EdgeClientProgramDataPaths.SanitizePathSegment(
                    item.Target.MachineProfile);
                return new ProfileJournalEntry
                {
                    MachineProfile = item.Target.MachineProfile,
                    ConfigPath = relativeConfigPath,
                    BackupPath = Path.Combine(
                        transactionRelativePath,
                        "backups",
                        "profiles",
                        $"{profileSegment}.json"),
                    StagedPath = Path.Combine(
                        transactionRelativePath,
                        "staging",
                        "profiles",
                        $"{profileSegment}.json"),
                    OriginalExists = File.Exists(configPath),
                    OriginalSha256 = ComputeFileSha256(configPath)
                };
            })
            .ToList();
        var firstTarget = targets[0].Target;
        return new UpdateTransactionJournal
        {
            SchemaVersion = JournalSchemaVersion,
            TransactionId = transactionId,
            State = StatePrepared,
            TransactionPath = transactionRelativePath,
            PluginsRoot = GetSafeRelativePath(
                ResolveLayoutRoot(),
                pluginsRoot),
            HostDirectory = GetSafeRelativePath(
                ResolveLayoutRoot(),
                firstTarget.HostDirectory),
            HostExecutablePath = GetSafeRelativePath(
                ResolveLayoutRoot(),
                firstTarget.HostExecutablePath),
            ExpectedHostVersion = pendingHostVersion,
            Plugins = plugins,
            Profiles = profiles
        };
    }

    private void SnapshotProfiles(
        UpdateTransactionJournal journal,
        string pluginsRoot)
    {
        foreach (var profile in journal.Profiles)
        {
            if (!profile.OriginalExists)
            {
                continue;
            }

            var sourcePath = ResolveProfilePath(profile.ConfigPath);
            var backupPath = ResolveRelativePath(pluginsRoot, profile.BackupPath);
            Directory.CreateDirectory(
                Path.GetDirectoryName(backupPath)
                ?? throw new InvalidOperationException("profile 备份缺少目录。"));
            File.Copy(sourcePath, backupPath, overwrite: false);
        }
    }

    private void StageProfiles(
        UpdateTransactionJournal journal,
        string pluginsRoot,
        IReadOnlyList<EdgePluginCompositionTarget> targets,
        IReadOnlyList<PreparedEdgePluginPackage> prepared)
    {
        var targetsByProfile = targets
            .DistinctBy(
                static item => item.Target.MachineProfile,
                StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                static item => item.Target.MachineProfile,
                StringComparer.OrdinalIgnoreCase);
        foreach (var profile in journal.Profiles)
        {
            var target = targetsByProfile[profile.MachineProfile];
            var targetPath = ResolveProfilePath(profile.ConfigPath);
            var stagedPath = ResolveRelativePath(
                pluginsRoot,
                profile.StagedPath);
            var root = ReadProfileSeed(target.Target, targetPath);
            foreach (var package in prepared)
            {
                if (!target.ModuleIds.Contains(
                        package.ModuleId,
                        StringComparer.OrdinalIgnoreCase))
                {
                    continue;
                }

                foreach (var activation in package.ActivationProfiles.Where(
                             item => string.Equals(
                                 item.ProfileId,
                                 profile.MachineProfile,
                                 StringComparison.OrdinalIgnoreCase)))
                {
                    var templatePath = ResolveRelativePath(
                        package.ExtractDirectory,
                        activation.MachineConfigPath);
                    MergeMissing(root, ReadJsonObject(templatePath));
                }
            }

            EnsureModulesEnabled(
                root,
                _profileStore
                    .ReadEnabledModules(target.Target)
                    .Concat(target.ModuleIds));
            WriteJsonObject(stagedPath, root);
            profile.TargetSha256 = ComputeFileSha256(stagedPath)
                ?? throw new InvalidOperationException(
                    $"profile 暂存文件缺失：{profile.MachineProfile}。");
            _faultInjector?.Invoke(
                EdgePluginTransactionStage.ActivationProfileStaged);
        }
    }

    private static JsonObject ReadProfileSeed(
        EdgeUpdateTarget target,
        string externalPath)
    {
        if (File.Exists(externalPath))
        {
            return ReadJsonObject(externalPath);
        }

        var packagedPath = Path.Combine(
            target.HostDirectory,
            $"appsettings.machine.{target.MachineProfile}.json");
        return File.Exists(packagedPath)
            ? ReadJsonObject(packagedPath)
            : new JsonObject();
    }

    private static JsonObject ReadJsonObject(string path)
        => JsonNode.Parse(File.ReadAllText(path)) as JsonObject
           ?? throw new InvalidOperationException(
               $"profile JSON 根节点必须是对象：{path}");

    private static void MergeMissing(
        JsonObject target,
        JsonObject template)
    {
        foreach (var property in template)
        {
            if (!target.TryGetPropertyValue(property.Key, out var current)
                || current is null)
            {
                target[property.Key] = property.Value?.DeepClone();
                continue;
            }

            if (current is JsonObject currentObject
                && property.Value is JsonObject templateObject)
            {
                MergeMissing(currentObject, templateObject);
            }
        }
    }

    private static void EnsureModulesEnabled(
        JsonObject root,
        IEnumerable<string> moduleIds)
    {
        if (root["Modules"] is not JsonObject modules)
        {
            modules = new JsonObject();
            root["Modules"] = modules;
        }

        modules["Enabled"] = new JsonArray(
            moduleIds
                .Where(static moduleId => !string.IsNullOrWhiteSpace(moduleId))
                .Select(static moduleId => moduleId.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(static moduleId => moduleId, StringComparer.OrdinalIgnoreCase)
                .Select(static moduleId => JsonValue.Create(moduleId))
                .Cast<JsonNode?>()
                .ToArray());
    }

    private static void WriteJsonObject(
        string path,
        JsonObject root)
    {
        Directory.CreateDirectory(
            Path.GetDirectoryName(path)
            ?? throw new InvalidOperationException(
                "profile 暂存路径缺少目录。"));
        File.WriteAllText(
            path,
            root.ToJsonString(new JsonSerializerOptions
            {
                WriteIndented = true
            }),
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    }

    private void CommitStagedProfile(
        ProfileJournalEntry profile,
        string pluginsRoot)
    {
        var stagedPath = ResolveRelativePath(
            pluginsRoot,
            profile.StagedPath);
        var targetPath = ResolveProfilePath(profile.ConfigPath);
        RestoreFileAtomically(stagedPath, targetPath);
        var actualSha256 = ComputeFileSha256(targetPath);
        if (!string.Equals(
                actualSha256,
                profile.TargetSha256,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"profile 提交校验失败：{profile.MachineProfile}。");
        }
    }

    private RollbackResult RollbackJournal(
        UpdateTransactionJournal journal,
        string pluginsRoot)
    {
        try
        {
            _faultInjector?.Invoke(EdgePluginTransactionStage.Rollback);
            foreach (var profile in journal.Profiles.AsEnumerable().Reverse())
            {
                var configPath = ResolveProfilePath(profile.ConfigPath);
                if (profile.OriginalExists)
                {
                    var backupPath = ResolveRelativePath(pluginsRoot, profile.BackupPath);
                    RestoreFileAtomically(backupPath, configPath);
                }
                else if (File.Exists(configPath))
                {
                    File.Delete(configPath);
                }
            }

            foreach (var plugin in journal.Plugins.AsEnumerable().Reverse())
            {
                if (!plugin.CommitStarted)
                {
                    continue;
                }

                var modulePath = ResolveRelativePath(pluginsRoot, plugin.ModulePath);
                var backupPath = ResolveRelativePath(pluginsRoot, plugin.BackupPath);
                if (plugin.OriginalExists)
                {
                    if (Directory.Exists(backupPath))
                    {
                        RestorePluginDirectoryReplaySafe(
                            backupPath,
                            modulePath);
                        _faultInjector?.Invoke(
                            EdgePluginTransactionStage.RollbackPluginRestored);
                    }
                    else if (plugin.Committed || !Directory.Exists(modulePath))
                    {
                        throw new InvalidOperationException(
                            $"插件 {plugin.ModuleId} 的旧目录备份缺失。");
                    }
                }
                else if (Directory.Exists(modulePath))
                {
                    Directory.Delete(modulePath, recursive: true);
                }
            }

            journal.State = StateRollbackCleanupPending;
            journal.LastError = null;
            WriteJournal(journal);
            _ = FinalizeRolledBackTransaction(journal, pluginsRoot);
            _blockAllProfiles = false;
            return new RollbackResult(true, null);
        }
        catch (Exception ex) when (IsRecoveryException(ex))
        {
            journal.State = StateRollbackFailed;
            journal.LastError = $"回滚失败: {ex.GetType().Name}";
            TryWriteJournal(journal);
            return new RollbackResult(false, journal.LastError);
        }
    }

    private static void RestorePluginDirectoryReplaySafe(
        string backupPath,
        string modulePath)
    {
        var restorePath = $"{backupPath}.restore";
        if (Directory.Exists(restorePath))
        {
            Directory.Delete(restorePath, recursive: true);
        }
        else if (File.Exists(restorePath))
        {
            File.Delete(restorePath);
        }

        CopyDirectoryTree(backupPath, restorePath);
        if (Directory.Exists(modulePath))
        {
            Directory.Delete(modulePath, recursive: true);
        }
        else if (File.Exists(modulePath))
        {
            File.Delete(modulePath);
        }

        Directory.CreateDirectory(
            Path.GetDirectoryName(modulePath)
            ?? throw new InvalidOperationException("插件目录缺少父目录。"));
        Directory.Move(restorePath, modulePath);
    }

    private static void CopyDirectoryTree(
        string sourcePath,
        string destinationPath)
    {
        if (!Directory.Exists(sourcePath))
        {
            throw new DirectoryNotFoundException(
                $"插件回滚备份不存在：{sourcePath}");
        }

        Directory.CreateDirectory(destinationPath);
        foreach (var sourceEntry in Directory.EnumerateFileSystemEntries(sourcePath))
        {
            var attributes = File.GetAttributes(sourceEntry);
            if ((attributes & FileAttributes.ReparsePoint) != 0)
            {
                throw new InvalidOperationException(
                    "插件回滚备份包含不允许的重解析点。");
            }

            var destinationEntry = Path.Combine(
                destinationPath,
                Path.GetFileName(sourceEntry));
            if ((attributes & FileAttributes.Directory) != 0)
            {
                CopyDirectoryTree(sourceEntry, destinationEntry);
            }
            else
            {
                File.Copy(sourceEntry, destinationEntry, overwrite: false);
            }
        }
    }

    private bool IsExpectedHostHandoffState(
        UpdateTransactionJournal journal,
        string pluginsRoot)
    {
        if (string.IsNullOrWhiteSpace(journal.ExpectedHostVersion))
        {
            return false;
        }

        var hostDirectory = ResolveLayoutRelativePath(journal.HostDirectory);
        var hostExecutable = ResolveLayoutRelativePath(journal.HostExecutablePath);
        var currentHostVersion = ResolveHostVersion(hostDirectory, hostExecutable);
        if (!string.Equals(
                currentHostVersion,
                journal.ExpectedHostVersion,
                StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        foreach (var plugin in journal.Plugins)
        {
            var modulePath = ResolveRelativePath(pluginsRoot, plugin.ModulePath);
            if (!IsExpectedPluginState(modulePath, plugin))
            {
                return false;
            }
        }

        return journal.Profiles.All(profile =>
            string.Equals(
                ComputeFileSha256(ResolveProfilePath(profile.ConfigPath)),
                profile.TargetSha256,
                StringComparison.OrdinalIgnoreCase));
    }

    private bool FinalizeCommittedTransaction(
        UpdateTransactionJournal journal,
        string pluginsRoot)
    {
        try
        {
            _faultInjector?.Invoke(EdgePluginTransactionStage.Cleanup);
            DeleteTransactionEvidence(journal, pluginsRoot);
            _blockAllProfiles = false;
            return true;
        }
        catch (IOException)
        {
            journal.State = StateCleanupPending;
            TryWriteJournal(journal);
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            journal.State = StateCleanupPending;
            TryWriteJournal(journal);
            return false;
        }
    }

    private bool FinalizeRolledBackTransaction(
        UpdateTransactionJournal journal,
        string pluginsRoot)
    {
        try
        {
            DeleteTransactionEvidence(journal, pluginsRoot);
            _blockAllProfiles = false;
            return true;
        }
        catch (IOException)
        {
            journal.State = StateRollbackCleanupPending;
            TryWriteJournal(journal);
            _blockAllProfiles = false;
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            journal.State = StateRollbackCleanupPending;
            TryWriteJournal(journal);
            _blockAllProfiles = false;
            return false;
        }
    }

    private void DeleteTransactionEvidence(
        UpdateTransactionJournal journal,
        string pluginsRoot)
    {
        var transactionRoot = ResolveRelativePath(
            pluginsRoot,
            journal.TransactionPath);
        if (Directory.Exists(transactionRoot))
        {
            Directory.Delete(transactionRoot, recursive: true);
        }

        _faultInjector?.Invoke(EdgePluginTransactionStage.JournalRemoval);
        if (File.Exists(_journalPath))
        {
            File.Delete(_journalPath);
        }
    }

    private UpdateTransactionJournal? ReadJournal(out string? error)
    {
        error = null;
        if (!File.Exists(_journalPath))
        {
            return null;
        }

        try
        {
            var journal = JsonSerializer.Deserialize<UpdateTransactionJournal>(
                File.ReadAllText(_journalPath),
                JournalJsonOptions);
            if (journal is null
                || journal.SchemaVersion != JournalSchemaVersion
                || string.IsNullOrWhiteSpace(journal.TransactionId)
                || string.IsNullOrWhiteSpace(journal.TransactionPath)
                || string.IsNullOrWhiteSpace(journal.PluginsRoot)
                || string.IsNullOrWhiteSpace(journal.HostDirectory)
                || string.IsNullOrWhiteSpace(journal.HostExecutablePath)
                || !IsKnownState(journal.State)
                || journal.Plugins is null
                || journal.Profiles is null
                || journal.Plugins.Any(static plugin =>
                    string.IsNullOrWhiteSpace(plugin.ModuleId)
                    || string.IsNullOrWhiteSpace(plugin.Version)
                    || string.IsNullOrWhiteSpace(plugin.PackageSha256)
                    || string.IsNullOrWhiteSpace(plugin.ModulePath)
                    || string.IsNullOrWhiteSpace(plugin.BackupPath))
                || journal.Profiles.Any(static profile =>
                    string.IsNullOrWhiteSpace(profile.MachineProfile)
                    || string.IsNullOrWhiteSpace(profile.ConfigPath)
                    || string.IsNullOrWhiteSpace(profile.BackupPath)
                    || string.IsNullOrWhiteSpace(profile.StagedPath)
                    || string.IsNullOrWhiteSpace(profile.TargetSha256)))
            {
                error = "更新事务日志结构无效。";
                return null;
            }

            return journal;
        }
        catch (JsonException)
        {
            error = "更新事务日志 JSON 无效。";
            return null;
        }
        catch (IOException)
        {
            error = "更新事务日志不可读取。";
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            error = "更新事务日志不可读取。";
            return null;
        }
    }

    private void WriteJournal(UpdateTransactionJournal journal)
    {
        var directory = Path.GetDirectoryName(_journalPath)
            ?? throw new InvalidOperationException("更新事务日志缺少目录。");
        Directory.CreateDirectory(directory);
        var temporaryPath = Path.Combine(
            directory,
            $".{JournalFileName}.{Guid.NewGuid():N}.tmp");
        try
        {
            File.WriteAllText(
                temporaryPath,
                JsonSerializer.Serialize(journal, JournalJsonOptions),
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            File.Move(temporaryPath, _journalPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    private void TryWriteJournal(UpdateTransactionJournal journal)
    {
        try
        {
            WriteJournal(journal);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private string? ResolveSharedPluginsRoot(
        IReadOnlyList<EdgePluginCompositionTarget> targets)
    {
        var roots = targets
            .Select(target => Path.GetFullPath(
                EdgeClientProgramDataPaths.ResolveApplicationPluginRoot(
                    target.Target.HostDirectory)))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        return roots.Length == 1 ? roots[0] : null;
    }

    private string? ResolveJournalPluginsRoot(UpdateTransactionJournal journal)
    {
        try
        {
            return ResolveLayoutRelativePath(journal.PluginsRoot);
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    private string ResolveProfilePath(string relativePath)
        => ResolveRelativePath(
            EdgeClientProgramDataPaths.ResolveConfigRoot(_baseDirectory),
            relativePath);

    private string ResolveLayoutRelativePath(string relativePath)
        => ResolveRelativePath(ResolveLayoutRoot(), relativePath);

    private string ResolveLayoutRoot()
        => Path.GetFullPath(Path.Combine(_baseDirectory, ".."));

    private static string ResolveRelativePath(string root, string relativePath)
    {
        if (Path.IsPathRooted(relativePath) || relativePath.Contains('\0'))
        {
            throw new InvalidOperationException("更新事务路径必须是安全相对路径。");
        }

        var normalizedRoot = Path.GetFullPath(root)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;
        var resolved = Path.GetFullPath(Path.Combine(root, relativePath));
        if (!resolved.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("更新事务路径越界。");
        }

        return resolved;
    }

    private static string GetSafeRelativePath(string root, string path)
    {
        var relative = Path.GetRelativePath(
            Path.GetFullPath(root),
            Path.GetFullPath(path));
        _ = ResolveRelativePath(root, relative);
        return relative;
    }

    private static void RestoreFileAtomically(
        string backupPath,
        string targetPath)
    {
        var directory = Path.GetDirectoryName(targetPath)
            ?? throw new InvalidOperationException("profile 恢复路径缺少目录。");
        Directory.CreateDirectory(directory);
        var temporaryPath = Path.Combine(
            directory,
            $".{Path.GetFileName(targetPath)}.{Guid.NewGuid():N}.restore");
        try
        {
            File.Copy(backupPath, temporaryPath, overwrite: false);
            File.Move(temporaryPath, targetPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    private static string? ComputeFileSha256(string path)
    {
        if (!File.Exists(path))
        {
            return null;
        }

        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream));
    }

    private static bool TryReadManifestVersion(
        string manifestPath,
        out string? version)
    {
        version = null;
        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(manifestPath));
            if (!document.RootElement.TryGetProperty("version", out var value)
                && !document.RootElement.TryGetProperty("Version", out value))
            {
                return false;
            }

            version = value.ValueKind == JsonValueKind.String
                ? value.GetString()?.Trim()
                : null;
            return !string.IsNullOrWhiteSpace(version);
        }
        catch (Exception ex) when (ex is JsonException
                                       or IOException
                                       or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static bool IsExpectedPluginState(
        string modulePath,
        PluginJournalEntry plugin)
    {
        var manifestPath = Path.Combine(modulePath, "plugin.json");
        if (!TryReadManifestVersion(manifestPath, out var version)
            || !string.Equals(
                version,
                plugin.Version,
                StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        try
        {
            using var document = JsonDocument.Parse(
                File.ReadAllText(Path.Combine(modulePath, "install.json")));
            var root = document.RootElement;
            return TryReadString(root, "moduleId", out var moduleId)
                   && TryReadString(root, "version", out var installedVersion)
                   && TryReadString(root, "packageSha256", out var packageSha256)
                   && string.Equals(
                       moduleId,
                       plugin.ModuleId,
                       StringComparison.OrdinalIgnoreCase)
                   && string.Equals(
                       installedVersion,
                       plugin.Version,
                       StringComparison.OrdinalIgnoreCase)
                   && string.Equals(
                       packageSha256,
                       plugin.PackageSha256,
                       StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is JsonException
                                       or IOException
                                       or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static bool TryReadString(
        JsonElement root,
        string propertyName,
        out string? value)
    {
        value = null;
        if (!root.TryGetProperty(propertyName, out var property)
            || property.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        value = property.GetString()?.Trim();
        return !string.IsNullOrWhiteSpace(value);
    }

    private static bool IsKnownState(string state)
        => state is StatePrepared
            or StateCommitting
            or StateHostHandoffPending
            or StateCleanupPending
            or StateRollbackCleanupPending
            or StateRollbackFailed;

    private static bool IsRecoveryException(Exception ex)
        => ex is IOException
            or UnauthorizedAccessException
            or InvalidOperationException
            or JsonException
            or ArgumentException
            or NotSupportedException
            or PathTooLongException;

    private static string ResolveHostVersion(
        string hostDirectory,
        string hostExecutablePath)
    {
        var candidates = new[]
        {
            Path.Combine(hostDirectory, "IIoT.Edge.Host.Bootstrap.dll"),
            Path.Combine(hostDirectory, "IIoT.Edge.Shell.dll"),
            hostExecutablePath
        };
        foreach (var candidate in candidates)
        {
            try
            {
                if (!File.Exists(candidate))
                {
                    continue;
                }

                return EdgeClientHostRuntime.FormatHostVersion(
                    AssemblyName.GetAssemblyName(candidate).Version);
            }
            catch (Exception ex) when (ex is BadImageFormatException
                                           or FileLoadException
                                           or IOException
                                           or UnauthorizedAccessException)
            {
            }
        }

        return string.Empty;
    }

    private static IProgress<int>? ScaleProgress(
        IProgress<int>? progress,
        int index,
        int count,
        int start,
        int span)
        => progress is null
            ? null
            : new Progress<int>(value =>
            {
                var perItem = span / Math.Max(count, 1);
                progress.Report(Math.Clamp(
                    start + index * perItem + value * perItem / 100,
                    0,
                    99));
            });

    private sealed class UpdateTransactionJournal
    {
        public int SchemaVersion { get; set; }

        public string TransactionId { get; set; } = string.Empty;

        public string State { get; set; } = string.Empty;

        public string TransactionPath { get; set; } = string.Empty;

        public string PluginsRoot { get; set; } = string.Empty;

        public string HostDirectory { get; set; } = string.Empty;

        public string HostExecutablePath { get; set; } = string.Empty;

        public string? ExpectedHostVersion { get; set; }

        public string? LastError { get; set; }

        public List<PluginJournalEntry> Plugins { get; set; } = [];

        public List<ProfileJournalEntry> Profiles { get; set; } = [];
    }

    private sealed class PluginJournalEntry
    {
        public string ModuleId { get; set; } = string.Empty;

        public string Version { get; set; } = string.Empty;

        public string PackageSha256 { get; set; } = string.Empty;

        public string ModulePath { get; set; } = string.Empty;

        public string BackupPath { get; set; } = string.Empty;

        public bool OriginalExists { get; set; }

        public bool CommitStarted { get; set; }

        public bool Committed { get; set; }
    }

    private sealed class ProfileJournalEntry
    {
        public string MachineProfile { get; set; } = string.Empty;

        public string ConfigPath { get; set; } = string.Empty;

        public string BackupPath { get; set; } = string.Empty;

        public string StagedPath { get; set; } = string.Empty;

        public bool OriginalExists { get; set; }

        public string? OriginalSha256 { get; set; }

        public string? TargetSha256 { get; set; }
    }

    private sealed record RollbackResult(
        bool Success,
        string? ErrorMessage);
}
