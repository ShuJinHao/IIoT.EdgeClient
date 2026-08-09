using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using IIoT.Edge.Application.Features.Updates;
using IIoT.Edge.Infrastructure.Update.Packages;
using IIoT.Edge.Infrastructure.Update.Profiles;
using IIoT.Edge.Module.Contracts.Updates;
using IIoT.Edge.SharedKernel.Configuration;
using IIoT.Edge.SharedKernel.Runtime;
using IIoT.Edge.SharedKernel.Security;
using Xunit;

namespace IIoT.Edge.Update.ContractTests;

public sealed class EdgePluginCompositionTransactionTests
{
    [Theory]
    [InlineData(EdgePluginTransactionStage.InstallRecordWritten)]
    [InlineData(EdgePluginTransactionStage.PackagePrepared)]
    [InlineData(EdgePluginTransactionStage.ActivationProfileStaged)]
    [InlineData(EdgePluginTransactionStage.DirectoryMovedBeforeJournal)]
    [InlineData(EdgePluginTransactionStage.DirectoryReplaced)]
    [InlineData(EdgePluginTransactionStage.ProfileWritten)]
    [InlineData(EdgePluginTransactionStage.HostHandoffPending)]
    public async Task InstallAsync_WhenStageFails_ShouldRestoreOldCombination(
        EdgePluginTransactionStage failureStage)
    {
        using var fixture = TransactionFixture.Create(["TestPlugin"]);
        var transaction = fixture.CreateTransaction(stage =>
        {
            if (stage == failureStage)
            {
                throw new InvalidOperationException($"fault:{stage}");
            }
        });

        var result = await transaction.InstallAsync(
            [fixture.TargetForModules(["TestPlugin"])],
            [fixture.Source(fixture.Release("TestPlugin"))],
            "1.0.0",
            EdgeClientHostRuntime.HostApiVersion,
            pendingHostVersion: "2.0.0",
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.False(result.Success);
        fixture.AssertOldCombination(["TestPlugin"]);
        Assert.False(File.Exists(fixture.JournalPath));
    }

    [Fact]
    public async Task InstallAsync_WhenSecondDependencyPreparationFails_ShouldNotMutateAnyFormalDirectory()
    {
        using var fixture = TransactionFixture.Create(["Dependency", "TestPlugin"]);
        var preparedCount = 0;
        var transaction = fixture.CreateTransaction(stage =>
        {
            if (stage == EdgePluginTransactionStage.PackagePrepared
                && ++preparedCount == 2)
            {
                throw new InvalidOperationException("dependency preparation fault");
            }
        });

        var result = await transaction.InstallAsync(
            [fixture.TargetForModules(["Dependency", "TestPlugin"])],
            [
                fixture.Source(fixture.Release("Dependency")),
                fixture.Source(fixture.Release("TestPlugin", ["Dependency"]))
            ],
            "1.0.0",
            EdgeClientHostRuntime.HostApiVersion,
            pendingHostVersion: null,
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.False(result.Success);
        fixture.AssertOldCombination(["Dependency", "TestPlugin"]);
        Assert.False(File.Exists(fixture.JournalPath));
    }

    [Fact]
    public async Task InstallAsync_WhenSanitizedModulePathsCollide_ShouldRejectBeforeCreatingTransaction()
    {
        using var fixture = TransactionFixture.Create(["Plugin A", "Plugin_A"]);
        var transaction = fixture.CreateTransaction();

        var result = await transaction.InstallAsync(
            [fixture.TargetForModules(["Plugin A", "Plugin_A"])],
            [
                fixture.Source(fixture.Release("Plugin A")),
                fixture.Source(fixture.Release("Plugin_A"))
            ],
            "1.0.0",
            EdgeClientHostRuntime.HostApiVersion,
            pendingHostVersion: null,
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.False(result.Success);
        Assert.Contains(
            "同一插件目录",
            result.ErrorMessage ?? string.Empty,
            StringComparison.Ordinal);
        fixture.AssertOldCombination(["Plugin A", "Plugin_A"]);
        Assert.False(File.Exists(fixture.JournalPath));
        Assert.False(Directory.Exists(Path.Combine(fixture.PluginsRoot, ".transactions")));
    }

    [Fact]
    public async Task InstallAsync_WhenModulePathIsReserved_ShouldRejectBeforeCreatingTransaction()
    {
        using var fixture = TransactionFixture.Create([".transactions"]);
        var transaction = fixture.CreateTransaction();

        var result = await transaction.InstallAsync(
            [fixture.TargetForModules([".transactions"])],
            [fixture.Source(fixture.Release(".transactions"))],
            "1.0.0",
            EdgeClientHostRuntime.HostApiVersion,
            pendingHostVersion: null,
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.False(result.Success);
        Assert.Contains(
            "保留的事务目录",
            result.ErrorMessage ?? string.Empty,
            StringComparison.Ordinal);
        fixture.AssertOldCombination([".transactions"]);
        Assert.False(File.Exists(fixture.JournalPath));
        Assert.Empty(Directory.EnumerateDirectories(
            Path.Combine(fixture.PluginsRoot, ".transactions")));
    }

    [Theory]
    [InlineData(".transactions.", ".transactions")]
    [InlineData("AP.", "AP")]
    [InlineData("AP ", "AP")]
    public async Task InstallAsync_WhenModulePathUsesWin32TrailingAlias_ShouldRejectBeforeCreatingTransaction(
        string moduleId,
        string existingModuleId)
    {
        using var fixture = TransactionFixture.Create(
            [moduleId],
            [existingModuleId]);
        var transaction = fixture.CreateTransaction();

        var result = await transaction.InstallAsync(
            [fixture.TargetForModules([moduleId])],
            [fixture.Source(fixture.Release(moduleId))],
            "1.0.0",
            EdgeClientHostRuntime.HostApiVersion,
            pendingHostVersion: null,
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.False(result.Success);
        Assert.Contains(
            "Windows 尾随句点或空格别名",
            result.ErrorMessage ?? string.Empty,
            StringComparison.Ordinal);
        fixture.AssertOldCombination([existingModuleId]);
        Assert.False(File.Exists(fixture.JournalPath));
        var transactionStorage = Path.Combine(
            fixture.PluginsRoot,
            ".transactions");
        if (Directory.Exists(transactionStorage))
        {
            Assert.Empty(Directory.EnumerateDirectories(transactionStorage));
        }
    }

    [Fact]
    public async Task InstallAsync_WhenModuleIdIsNotCanonicalPathSegment_ShouldRejectBeforeCreatingTransaction()
    {
        const string moduleId = "AP CP";
        using var fixture = TransactionFixture.Create(
            [moduleId],
            ["AP_CP"]);
        var transaction = fixture.CreateTransaction();

        var result = await transaction.InstallAsync(
            [fixture.TargetForModules([moduleId])],
            [fixture.Source(fixture.Release(moduleId))],
            "1.0.0",
            EdgeClientHostRuntime.HostApiVersion,
            pendingHostVersion: null,
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.False(result.Success);
        Assert.Contains(
            "规范的 Windows 路径段",
            result.ErrorMessage ?? string.Empty,
            StringComparison.Ordinal);
        fixture.AssertOldCombination(["AP_CP"]);
        Assert.False(File.Exists(fixture.JournalPath));
        Assert.False(Directory.Exists(
            Path.Combine(fixture.PluginsRoot, ".transactions")));
    }

    [Fact]
    public async Task RecoverPendingTransaction_WhenHostVersionDoesNotMatch_ShouldRollbackOldCombination()
    {
        using var fixture = TransactionFixture.Create(["TestPlugin"]);
        var firstLauncher = fixture.CreateTransaction();
        var install = await firstLauncher.InstallAsync(
            [fixture.TargetForModules(["TestPlugin"])],
            [fixture.Source(fixture.Release("TestPlugin"))],
            "1.0.0",
            EdgeClientHostRuntime.HostApiVersion,
            pendingHostVersion: "99.0.0",
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(install.Success, install.ErrorMessage);
        Assert.True(File.Exists(fixture.JournalPath));

        var restartedLauncher = fixture.CreateTransaction();
        var recovery = restartedLauncher.RecoverPendingTransaction();

        Assert.True(recovery.Success, recovery.ErrorMessage);
        Assert.True(recovery.Recovered);
        Assert.False(recovery.Blocked);
        fixture.AssertOldCombination(["TestPlugin"]);
        Assert.False(File.Exists(fixture.JournalPath));
    }

    [Fact]
    public async Task RecoverPendingTransaction_WhenProcessStopsAfterPluginRestore_ShouldReplayRollback()
    {
        using var fixture = TransactionFixture.Create(["TestPlugin"]);
        var firstLauncher = fixture.CreateTransaction();
        var install = await firstLauncher.InstallAsync(
            [fixture.TargetForModules(["TestPlugin"])],
            [fixture.Source(fixture.Release("TestPlugin"))],
            "1.0.0",
            EdgeClientHostRuntime.HostApiVersion,
            pendingHostVersion: "99.0.0",
            cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(install.Success, install.ErrorMessage);

        var interruptedLauncher = fixture.CreateTransaction(stage =>
        {
            if (stage == EdgePluginTransactionStage.RollbackPluginRestored)
            {
                throw new SimulatedProcessTerminationException();
            }
        });

        Assert.Throws<SimulatedProcessTerminationException>(
            () => interruptedLauncher.RollbackPendingHostHandoff());
        fixture.AssertOldCombination(["TestPlugin"]);
        Assert.True(File.Exists(fixture.JournalPath));

        var restartedLauncher = fixture.CreateTransaction();
        var recovery = restartedLauncher.RecoverPendingTransaction();

        Assert.True(recovery.Success, recovery.ErrorMessage);
        Assert.True(recovery.Recovered);
        Assert.False(recovery.Blocked);
        fixture.AssertOldCombination(["TestPlugin"]);
        Assert.False(File.Exists(fixture.JournalPath));
    }

    [Fact]
    public async Task IsProfileBlocked_WhenHostHandoffIsPending_ShouldBlockUntilRecovery()
    {
        using var fixture = TransactionFixture.Create(["TestPlugin"]);
        var transaction = fixture.CreateTransaction();
        var install = await transaction.InstallAsync(
            [fixture.TargetForModules(["TestPlugin"])],
            [fixture.Source(fixture.Release("TestPlugin"))],
            "1.0.0",
            EdgeClientHostRuntime.HostApiVersion,
            pendingHostVersion: "99.0.0",
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(install.Success, install.ErrorMessage);
        Assert.True(transaction.IsProfileBlocked(fixture.Target.MachineProfile));

        var recovery = transaction.RecoverPendingTransaction();

        Assert.True(recovery.Success, recovery.ErrorMessage);
        Assert.False(transaction.IsProfileBlocked(fixture.Target.MachineProfile));
        fixture.AssertOldCombination(["TestPlugin"]);
    }

    [Fact]
    public void CorruptJournal_ShouldBlockAllProfilesAndFailRollbackWithoutThrowing()
    {
        using var fixture = TransactionFixture.Create(["TestPlugin"]);
        Directory.CreateDirectory(Path.GetDirectoryName(fixture.JournalPath)!);
        File.WriteAllText(fixture.JournalPath, """{"schemaVersion":1,"state":"committing"}""");
        var transaction = fixture.CreateTransaction();

        Assert.True(transaction.IsProfileBlocked("LineA"));
        Assert.True(transaction.IsProfileBlocked("OtherLine"));

        var rollback = transaction.RollbackPendingHostHandoff();

        Assert.False(rollback.Success);
        Assert.True(File.Exists(fixture.JournalPath));
    }

    [Fact]
    public async Task RecoverPendingTransaction_WhenHostPluginAndProfileMatch_ShouldFinalizeNewCombination()
    {
        using var fixture = TransactionFixture.Create(["TestPlugin"]);
        var currentAssembly = typeof(EdgePluginCompositionTransactionTests).Assembly.Location;
        File.Copy(currentAssembly, fixture.Target.HostExecutablePath, overwrite: true);
        var expectedHostVersion = EdgeClientHostRuntime.FormatHostVersion(
            typeof(EdgePluginCompositionTransactionTests).Assembly.GetName().Version);
        var firstLauncher = fixture.CreateTransaction();
        var install = await firstLauncher.InstallAsync(
            [fixture.TargetForModules(["TestPlugin"])],
            [fixture.Source(fixture.Release("TestPlugin"))],
            "1.0.0",
            EdgeClientHostRuntime.HostApiVersion,
            pendingHostVersion: expectedHostVersion,
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(install.Success, install.ErrorMessage);
        Assert.True(File.Exists(fixture.JournalPath));

        var restartedLauncher = fixture.CreateTransaction();
        var recovery = restartedLauncher.RecoverPendingTransaction();

        Assert.True(recovery.Success, recovery.ErrorMessage);
        Assert.True(recovery.Recovered);
        Assert.False(recovery.Blocked);
        fixture.AssertNewPlugin("TestPlugin");
        fixture.AssertProfileContainsModules(["Existing", "TestPlugin"]);
        fixture.AssertActivationDefaultsApplied("TestPlugin");
        fixture.AssertUnrelatedStateUnchanged();
        Assert.False(File.Exists(fixture.JournalPath));
    }

    [Fact]
    public async Task RollbackPendingHostHandoff_WhenRollbackFails_ShouldKeepEvidenceAndBlockAffectedProfile()
    {
        using var fixture = TransactionFixture.Create(["TestPlugin"]);
        var transaction = fixture.CreateTransaction(stage =>
        {
            if (stage == EdgePluginTransactionStage.Rollback)
            {
                throw new IOException("simulated rollback failure");
            }
        });
        var install = await transaction.InstallAsync(
            [fixture.TargetForModules(["TestPlugin"])],
            [fixture.Source(fixture.Release("TestPlugin"))],
            "1.0.0",
            EdgeClientHostRuntime.HostApiVersion,
            pendingHostVersion: "2.0.0",
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(install.Success, install.ErrorMessage);
        var rollback = transaction.RollbackPendingHostHandoff();

        Assert.False(rollback.Success);
        Assert.True(File.Exists(fixture.JournalPath));
        Assert.True(transaction.IsProfileBlocked(fixture.Target.MachineProfile));
        Assert.NotEmpty(Directory.EnumerateDirectories(
            Path.Combine(fixture.PluginsRoot, ".transactions")));
        var journal = File.ReadAllText(fixture.JournalPath);
        Assert.Contains("\"state\": \"rollbackFailed\"", journal, StringComparison.Ordinal);
        Assert.Contains("\"stagedPath\":", journal, StringComparison.Ordinal);
        Assert.Contains("\"targetSha256\":", journal, StringComparison.Ordinal);
        Assert.DoesNotContain("secret", journal, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RollbackPendingHostHandoff_WhenJournalRemovalFails_ShouldKeepDurableCleanupMarkerWithoutBlockingShell()
    {
        using var fixture = TransactionFixture.Create(["TestPlugin"]);
        var failJournalRemovalOnce = true;
        var transaction = fixture.CreateTransaction(stage =>
        {
            if (stage == EdgePluginTransactionStage.JournalRemoval
                && failJournalRemovalOnce)
            {
                failJournalRemovalOnce = false;
                throw new IOException("simulated journal removal failure");
            }
        });
        var install = await transaction.InstallAsync(
            [fixture.TargetForModules(["TestPlugin"])],
            [fixture.Source(fixture.Release("TestPlugin"))],
            "1.0.0",
            EdgeClientHostRuntime.HostApiVersion,
            pendingHostVersion: "2.0.0",
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(install.Success, install.ErrorMessage);

        var rollback = transaction.RollbackPendingHostHandoff();

        Assert.True(rollback.Success, rollback.ErrorMessage);
        fixture.AssertOldCombination(["TestPlugin"]);
        Assert.True(File.Exists(fixture.JournalPath));
        Assert.False(transaction.IsProfileBlocked(fixture.Target.MachineProfile));
        Assert.Contains(
            "\"state\": \"rollbackCleanupPending\"",
            File.ReadAllText(fixture.JournalPath),
            StringComparison.Ordinal);
        Assert.Empty(Directory.EnumerateDirectories(
            Path.Combine(fixture.PluginsRoot, ".transactions")));

        var recovery = transaction.RecoverPendingTransaction();

        Assert.True(recovery.Success, recovery.ErrorMessage);
        Assert.True(recovery.Recovered);
        Assert.False(recovery.Blocked);
        Assert.False(File.Exists(fixture.JournalPath));
        fixture.AssertOldCombination(["TestPlugin"]);
    }

    [Fact]
    public async Task InstallAsync_WhenNoHostHandoff_ShouldPreserveCustomConfigurationAndUnrelatedState()
    {
        using var fixture = TransactionFixture.Create(["TestPlugin"]);
        var transaction = fixture.CreateTransaction();

        var result = await transaction.InstallAsync(
            [fixture.TargetForModules(["TestPlugin"])],
            [fixture.Source(fixture.Release("TestPlugin"))],
            "1.0.0",
            EdgeClientHostRuntime.HostApiVersion,
            pendingHostVersion: null,
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(result.Success, result.ErrorMessage);
        fixture.AssertNewPlugin("TestPlugin");
        fixture.AssertProfileContainsModules(["Existing", "TestPlugin"]);
        fixture.AssertActivationDefaultsApplied("TestPlugin");
        fixture.AssertCustomProfileValuesUnchanged();
        fixture.AssertUnrelatedStateUnchanged();
        Assert.False(File.Exists(fixture.JournalPath));
    }

    [Fact]
    public async Task InstallAsync_WhenCommittedBackupCleanupFails_ShouldKeepJournalAndRejectNextUpdate()
    {
        using var fixture = TransactionFixture.Create(["TestPlugin"]);
        var transaction = fixture.CreateTransaction(stage =>
        {
            if (stage == EdgePluginTransactionStage.Cleanup)
            {
                throw new IOException("simulated cleanup failure");
            }
        });

        var first = await transaction.InstallAsync(
            [fixture.TargetForModules(["TestPlugin"])],
            [fixture.Source(fixture.Release("TestPlugin"))],
            "1.0.0",
            EdgeClientHostRuntime.HostApiVersion,
            pendingHostVersion: null,
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(first.Success, first.ErrorMessage);
        Assert.True(File.Exists(fixture.JournalPath));
        Assert.False(transaction.IsProfileBlocked(fixture.Target.MachineProfile));
        fixture.AssertNewPlugin("TestPlugin");

        var second = await transaction.InstallAsync(
            [fixture.TargetForModules(["TestPlugin"])],
            [fixture.Source(fixture.Release("TestPlugin"))],
            "1.0.0",
            EdgeClientHostRuntime.HostApiVersion,
            pendingHostVersion: null,
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.False(second.Success);
        Assert.Contains("清理", second.ErrorMessage ?? string.Empty, StringComparison.Ordinal);
        Assert.True(File.Exists(fixture.JournalPath));
        fixture.AssertNewPlugin("TestPlugin");
    }

    [Fact]
    public async Task InstallAsync_WhenCanceledAfterDirectoryReplacement_ShouldRollbackAndRethrowCancellation()
    {
        using var fixture = TransactionFixture.Create(["TestPlugin"]);
        using var cancellation = new CancellationTokenSource();
        var transaction = fixture.CreateTransaction(stage =>
        {
            if (stage == EdgePluginTransactionStage.DirectoryReplaced)
            {
                cancellation.Cancel();
                throw new OperationCanceledException(cancellation.Token);
            }
        });

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            transaction.InstallAsync(
                [fixture.TargetForModules(["TestPlugin"])],
                [fixture.Source(fixture.Release("TestPlugin"))],
                "1.0.0",
                EdgeClientHostRuntime.HostApiVersion,
                pendingHostVersion: "2.0.0",
                cancellationToken: cancellation.Token));

        fixture.AssertOldCombination(["TestPlugin"]);
        Assert.False(File.Exists(fixture.JournalPath));
    }

    [Fact]
    public async Task InstallAsync_WhenCanceledAndRollbackFails_ShouldReturnFailureAndKeepEvidence()
    {
        using var fixture = TransactionFixture.Create(["TestPlugin"]);
        using var cancellation = new CancellationTokenSource();
        var transaction = fixture.CreateTransaction(stage =>
        {
            if (stage == EdgePluginTransactionStage.DirectoryReplaced)
            {
                cancellation.Cancel();
                throw new OperationCanceledException(cancellation.Token);
            }

            if (stage == EdgePluginTransactionStage.Rollback)
            {
                throw new IOException("simulated rollback failure");
            }
        });

        var result = await transaction.InstallAsync(
            [fixture.TargetForModules(["TestPlugin"])],
            [fixture.Source(fixture.Release("TestPlugin"))],
            "1.0.0",
            EdgeClientHostRuntime.HostApiVersion,
            pendingHostVersion: "2.0.0",
            cancellationToken: cancellation.Token);

        Assert.False(result.Success);
        Assert.Contains("回滚失败", result.ErrorMessage ?? string.Empty, StringComparison.Ordinal);
        Assert.True(File.Exists(fixture.JournalPath));
        Assert.True(transaction.IsProfileBlocked(fixture.Target.MachineProfile));
    }

    [Fact]
    public async Task InstallAsync_BindingV3_ShouldReplaceOnlyMatchingClientCodeAndUpdateBinding()
    {
        using var fixture = TransactionFixture.Create(["P1Module", "P2Module"]);
        fixture.ConfigureBindingV3(
            ("P1-DEVICE", "P1Module"),
            ("P2-DEVICE", "P2Module"));
        var p2Before = fixture.ComputeDevicePluginHash("P2-DEVICE");
        var transaction = fixture.CreateTransaction();

        var result = await transaction.InstallAsync(
            [fixture.TargetForClient("P1-DEVICE", "P1Module")],
            [fixture.Source(fixture.Release("P1Module"))],
            "1.0.0",
            EdgeClientHostRuntime.HostApiVersion,
            pendingHostVersion: null,
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(result.Success, result.ErrorMessage);
        fixture.AssertV3PluginVersion("P1-DEVICE", "2.0.0");
        fixture.AssertV3PluginVersion("P2-DEVICE", "1.0.0");
        Assert.Equal(p2Before, fixture.ComputeDevicePluginHash("P2-DEVICE"));
        Assert.False(Directory.Exists(Path.Combine(fixture.PluginsRoot, "P1Module")));
        var runtime = fixture.ReadRuntimeBinding();
        var p1 = Assert.Single(runtime.Bindings, item => item.ClientCode == "P1-DEVICE");
        var p2 = Assert.Single(runtime.Bindings, item => item.ClientCode == "P2-DEVICE");
        Assert.Equal("2.0.0", p1.PluginVersion);
        Assert.Equal(
            TransactionFixture.ComputeSha256(fixture.PackagePath("P1Module")),
            p1.PackageSha256,
            ignoreCase: true);
        Assert.Equal("1.0.0", p2.PluginVersion);
    }

    [Fact]
    public async Task InstallAsync_BindingV3FailureAfterBindingWrite_ShouldRollbackPluginAndBinding()
    {
        using var fixture = TransactionFixture.Create(["P1Module", "P2Module"]);
        fixture.ConfigureBindingV3(
            ("P1-DEVICE", "P1Module"),
            ("P2-DEVICE", "P2Module"));
        var p1Before = fixture.ComputeDevicePluginHash("P1-DEVICE");
        var p2Before = fixture.ComputeDevicePluginHash("P2-DEVICE");
        var bindingBefore = File.ReadAllBytes(fixture.RuntimeBindingPath);
        var transaction = fixture.CreateTransaction(stage =>
        {
            if (stage == EdgePluginTransactionStage.RuntimeBindingWritten)
            {
                throw new InvalidOperationException("stop after binding commit");
            }
        });

        var result = await transaction.InstallAsync(
            [fixture.TargetForClient("P1-DEVICE", "P1Module")],
            [fixture.Source(fixture.Release("P1Module"))],
            "1.0.0",
            EdgeClientHostRuntime.HostApiVersion,
            pendingHostVersion: null,
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.False(result.Success);
        Assert.Equal(p1Before, fixture.ComputeDevicePluginHash("P1-DEVICE"));
        Assert.Equal(p2Before, fixture.ComputeDevicePluginHash("P2-DEVICE"));
        Assert.Equal(bindingBefore, File.ReadAllBytes(fixture.RuntimeBindingPath));
        Assert.False(File.Exists(fixture.JournalPath));
    }

    private sealed class TransactionFixture : IDisposable
    {
        private const string NewVersion = "2.0.0";

        private readonly Dictionary<string, string> _packagePaths;
        private readonly Dictionary<string, byte[]> _oldPluginMarkers;
        private readonly byte[] _originalProfile;
        private readonly byte[] _originalSqlite;
        private readonly byte[] _originalAccounts;

        private TransactionFixture(
            string root,
            string launcherDirectory,
            string hostDirectory,
            EdgeUpdateTarget target,
            string pluginsRoot,
            string profilePath,
            string sqlitePath,
            string accountsPath,
            Dictionary<string, string> packagePaths,
            Dictionary<string, byte[]> oldPluginMarkers,
            byte[] originalProfile,
            byte[] originalSqlite,
            byte[] originalAccounts)
        {
            Root = root;
            LauncherDirectory = launcherDirectory;
            HostDirectory = hostDirectory;
            Target = target;
            PluginsRoot = pluginsRoot;
            ProfilePath = profilePath;
            SqlitePath = sqlitePath;
            AccountsPath = accountsPath;
            _packagePaths = packagePaths;
            _oldPluginMarkers = oldPluginMarkers;
            _originalProfile = originalProfile;
            _originalSqlite = originalSqlite;
            _originalAccounts = originalAccounts;
            JournalPath = Path.Combine(
                EdgeClientProgramDataPaths.ResolveLauncherDirectory(launcherDirectory),
                EdgePluginCompositionTransaction.JournalFileName);
            CloudOptions = new EdgeUpdateCloudApiOptions(
                "https://cloud.example.test",
                5,
                "EDGE-001",
                "secret-value-must-not-enter-journal",
                "/api/v1/bootstrap/device-instance",
                "/api/v1/edge/client-releases/device/{deviceId}/catalog",
                "/api/v1/edge/client-releases/version-reports",
                "/api/v1/edge/runtime-heartbeats");
        }

        public string Root { get; }

        public string LauncherDirectory { get; }

        public string HostDirectory { get; }

        public EdgeUpdateTarget Target { get; }

        public string PluginsRoot { get; }

        public string ProfilePath { get; }

        public string SqlitePath { get; }

        public string AccountsPath { get; }

        public string JournalPath { get; }

        public string RuntimeBindingPath
            => EdgeClientProgramDataPaths.ResolveRuntimeBindingPath(LauncherDirectory);

        public EdgeUpdateCloudApiOptions CloudOptions { get; }

        public static TransactionFixture Create(
            IReadOnlyList<string> moduleIds,
            IReadOnlyList<string>? existingModuleIds = null)
        {
            var root = Path.Combine(
                Path.GetTempPath(),
                "edge-update-transaction-tests",
                Guid.NewGuid().ToString("N"));
            var layoutRoot = Path.Combine(root, "layout");
            var launcherDirectory = Path.Combine(layoutRoot, "launcher");
            var hostDirectory = Path.Combine(layoutRoot, "current");
            Directory.CreateDirectory(launcherDirectory);
            Directory.CreateDirectory(hostDirectory);
            var target = new EdgeUpdateTarget(
                "LineA",
                hostDirectory,
                Path.Combine(hostDirectory, "IIoT.Edge.Shell.dll"));
            var pluginsRoot = EdgeClientProgramDataPaths.ResolveApplicationPluginRoot(
                hostDirectory);
            Directory.CreateDirectory(pluginsRoot);

            var packagePaths = new Dictionary<string, string>(
                StringComparer.OrdinalIgnoreCase);
            var oldPluginMarkers = new Dictionary<string, byte[]>(
                StringComparer.OrdinalIgnoreCase);
            foreach (var moduleId in existingModuleIds ?? moduleIds)
            {
                var moduleDirectory = Path.Combine(pluginsRoot, moduleId);
                Directory.CreateDirectory(moduleDirectory);
                File.WriteAllText(
                    Path.Combine(moduleDirectory, "plugin.json"),
                    $$"""{"moduleId":"{{moduleId}}","version":"1.0.0"}""");
                var marker = RandomNumberGenerator.GetBytes(32);
                File.WriteAllBytes(
                    Path.Combine(moduleDirectory, "old.marker"),
                    marker);
                oldPluginMarkers[moduleId] = marker;
            }

            foreach (var moduleId in moduleIds)
            {
                var packagePath = Path.Combine(
                    root,
                    $"{moduleId}-{NewVersion}.zip");
                CreatePackage(packagePath, moduleId, NewVersion);
                packagePaths[moduleId] = packagePath;
            }

            var profilePath = EdgeClientProgramDataPaths.ResolveMachineProfileConfigPath(
                target.MachineProfile,
                hostDirectory);
            WriteText(
                profilePath,
                """
                {
                  "CloudApi": {
                    "BaseUrl": "https://custom-cloud.example.test",
                    "ClientCode": "EDGE-CUSTOM"
                  },
                  "Mes": {
                    "Endpoint": "https://custom-mes.example.test"
                  },
                  "Plc": {
                    "TimeoutMs": 4321
                  },
                  "Modules": {
                    "Enabled": [ "Existing" ]
                  },
                  "Custom": {
                    "Keep": true
                  }
                }
                """);
            var sqlitePath = Path.Combine(
                EdgeClientProgramDataPaths.ResolveDataRoot(hostDirectory),
                "client.db");
            WriteBytes(sqlitePath, RandomNumberGenerator.GetBytes(128));
            var accountsPath = EdgeClientProgramDataPaths.ResolveLauncherAccountsPath(
                launcherDirectory);
            WriteBytes(accountsPath, RandomNumberGenerator.GetBytes(64));

            return new TransactionFixture(
                root,
                launcherDirectory,
                hostDirectory,
                target,
                pluginsRoot,
                profilePath,
                sqlitePath,
                accountsPath,
                packagePaths,
                oldPluginMarkers,
                File.ReadAllBytes(profilePath),
                File.ReadAllBytes(sqlitePath),
                File.ReadAllBytes(accountsPath));
        }

        public EdgePluginCompositionTransaction CreateTransaction(
            Action<EdgePluginTransactionStage>? faultInjector = null)
            => new(
                LauncherDirectory,
                new EdgePluginPackageInstaller(
                    new HttpClient(new PackageHttpHandler(_packagePaths)),
                    new EdgeVersionCompatibilityPolicy()),
                new FileEdgeProfileModuleConfigurationStore(),
                faultInjector);

        public EdgePluginCompositionTarget TargetForModules(
            IReadOnlyList<string> moduleIds)
            => new(Target, moduleIds);

        public EdgePluginCompositionTarget TargetForClient(
            string clientCode,
            string moduleId)
            => new(Target, [moduleId])
            {
                ClientCode = clientCode
            };

        public string PackagePath(string moduleId) => _packagePaths[moduleId];

        public void ConfigureBindingV3(
            params (string ClientCode, string ModuleId)[] devices)
        {
            const string oldSha256 = "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA";
            var bindings = new List<EdgeRuntimeDeviceBinding>();
            foreach (var device in devices)
            {
                var clientCode = EdgeClientIdentity.NormalizeClientCode(device.ClientCode);
                var legacyDirectory = Path.Combine(PluginsRoot, device.ModuleId);
                var appDirectory = EdgeClientProgramDataPaths.ResolveDevicePluginDirectory(
                    clientCode,
                    "app",
                    HostDirectory);
                Directory.CreateDirectory(Path.GetDirectoryName(appDirectory)!);
                Directory.Move(legacyDirectory, appDirectory);
                File.WriteAllText(
                    Path.Combine(appDirectory, "plugin.json"),
                    $$"""
                    {"moduleId":"{{device.ModuleId}}","supportedProcessType":"DIECUT","version":"1.0.0"}
                    """);
                File.WriteAllText(
                    Path.Combine(appDirectory, "install.json"),
                    $$"""
                    {"moduleId":"{{device.ModuleId}}","version":"1.0.0","packageSha256":"{{oldSha256}}"}
                    """);
                var root = $"plugins/{clientCode}";
                bindings.Add(new EdgeRuntimeDeviceBinding(
                    clientCode,
                    $"{clientCode} device",
                    Guid.NewGuid(),
                    "DIECUT",
                    device.ModuleId,
                    "1.0.0",
                    oldSha256,
                    $"{root}/app",
                    $"{root}/config",
                    $"{root}/db",
                    $"{root}/data",
                    $"{root}/logs",
                    $"{root}/cache",
                    $"{root}/context",
                    $"{root}/buffers",
                    WindowsCredentialManagerStore.CreatePendingReference("GEN-001", clientCode),
                    EdgeInstallerBindingCodec.RuntimePendingStatus,
                    "S-1-5-21-1000"));
            }

            var envelope = new EdgeRuntimeBindingEnvelope(
                EdgeInstallerBindingCodec.CurrentSchemaVersion,
                "GEN-001",
                DateTimeOffset.UtcNow.AddMinutes(-1),
                DateTimeOffset.UtcNow.AddDays(1),
                "https://cloud.example.test",
                new EdgeInstallerBindingPaths(
                    "/api/v1/edge/bootstrap/device-instance",
                    "/api/v1/edge/bootstrap/edge-refresh",
                    "/api/v1/edge/bootstrap/device-activate",
                    "/api/v1/edge/bootstrap/device-activation-confirm",
                    "/api/v1/human/identity/edge-login",
                    "/api/v1/human/identity/refresh",
                    "/api/v1/human/identity/session",
                    "/api/v1/edge/device-logs",
                    "/api/v1/edge/pass-stations/{typeKey}/batch",
                    "/api/v1/edge/capacity/hourly",
                    "/api/v1/edge/capacity/summary",
                    "/api/v1/edge/capacity/summary/range",
                    "/api/v1/edge/recipes/device/{deviceId}",
                    "/api/v1/edge/client-releases/device/{deviceId}/catalog",
                    "/api/v1/edge/client-releases/version-reports",
                    "/api/v1/edge/runtime-heartbeats",
                    "/api/v1/edge/edge-hosts/plc-runtime-states"),
                bindings);
            WriteText(
                RuntimeBindingPath,
                EdgeInstallerBindingCodec.SerializeRuntime(envelope));
        }

        public EdgeRuntimeBindingEnvelope ReadRuntimeBinding()
            => EdgeInstallerBindingCodec.ParseRuntime(
                File.ReadAllText(RuntimeBindingPath));

        public string ComputeDevicePluginHash(string clientCode)
        {
            var directory = EdgeClientProgramDataPaths.ResolveDevicePluginDirectory(
                clientCode,
                "app",
                HostDirectory);
            using var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            foreach (var path in Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories)
                         .OrderBy(static path => path, StringComparer.Ordinal))
            {
                var relative = Path.GetRelativePath(directory, path).Replace('\\', '/');
                var header = System.Text.Encoding.UTF8.GetBytes(relative + "\n");
                sha.AppendData(header);
                sha.AppendData(File.ReadAllBytes(path));
            }

            return Convert.ToHexString(sha.GetHashAndReset());
        }

        public void AssertV3PluginVersion(string clientCode, string expectedVersion)
        {
            var directory = EdgeClientProgramDataPaths.ResolveDevicePluginDirectory(
                clientCode,
                "app",
                HostDirectory);
            using var manifest = JsonDocument.Parse(
                File.ReadAllText(Path.Combine(directory, "plugin.json")));
            Assert.Equal(expectedVersion, manifest.RootElement.GetProperty("version").GetString());
        }

        public EdgePluginVersionRelease Release(
            string moduleId,
            IReadOnlyList<string>? dependencies = null)
        {
            var packagePath = _packagePaths[moduleId];
            return new EdgePluginVersionRelease(
                moduleId,
                moduleId,
                null,
                null,
                null,
                new EdgePluginVersionEntry(
                    Guid.NewGuid(),
                    "stable",
                    NewVersion,
                    EdgeClientHostRuntime.HostApiVersion,
                    "1.0.0",
                    "99.0.0",
                    "win-x64",
                    "net10.0",
                    $"https://packages.example.test/{Uri.EscapeDataString(moduleId)}.zip",
                    ComputeSha256(packagePath),
                    new FileInfo(packagePath).Length,
                    null,
                    dependencies ?? [],
                    "Published",
                    null,
                    "IIoT",
                    DateTime.UtcNow,
                    DateTime.UtcNow));
        }

        public EdgePluginCompositionRelease Source(
            EdgePluginVersionRelease release,
            EdgeUpdateCloudApiOptions? cloudOptions = null)
            => new(release, cloudOptions ?? CloudOptions);

        public void AssertOldCombination(IReadOnlyList<string> moduleIds)
        {
            Assert.Equal(_originalProfile, File.ReadAllBytes(ProfilePath));
            foreach (var moduleId in moduleIds)
            {
                var moduleDirectory = Path.Combine(PluginsRoot, moduleId);
                Assert.Equal(
                    _oldPluginMarkers[moduleId],
                    File.ReadAllBytes(Path.Combine(moduleDirectory, "old.marker")));
                Assert.Contains(
                    "\"version\":\"1.0.0\"",
                    File.ReadAllText(Path.Combine(moduleDirectory, "plugin.json")),
                    StringComparison.Ordinal);
            }

            AssertUnrelatedStateUnchanged();
        }

        public void AssertNewPlugin(string moduleId)
        {
            var moduleDirectory = Path.Combine(PluginsRoot, moduleId);
            Assert.False(File.Exists(Path.Combine(moduleDirectory, "old.marker")));
            Assert.Contains(
                $"\"version\": \"{NewVersion}\"",
                File.ReadAllText(Path.Combine(moduleDirectory, "plugin.json")),
                StringComparison.Ordinal);
            Assert.True(File.Exists(Path.Combine(moduleDirectory, "install.json")));
        }

        public void AssertProfileContainsModules(IReadOnlyList<string> expected)
        {
            using var document = JsonDocument.Parse(File.ReadAllText(ProfilePath));
            var enabled = document.RootElement
                .GetProperty("Modules")
                .GetProperty("Enabled")
                .EnumerateArray()
                .Select(static item => item.GetString())
                .ToArray();
            Assert.Equal(
                expected.OrderBy(static item => item, StringComparer.OrdinalIgnoreCase),
                enabled.OrderBy(static item => item, StringComparer.OrdinalIgnoreCase));
        }

        public void AssertCustomProfileValuesUnchanged()
        {
            using var document = JsonDocument.Parse(File.ReadAllText(ProfilePath));
            var root = document.RootElement;
            Assert.Equal(
                "https://custom-cloud.example.test",
                root.GetProperty("CloudApi").GetProperty("BaseUrl").GetString());
            Assert.Equal(
                "EDGE-CUSTOM",
                root.GetProperty("CloudApi").GetProperty("ClientCode").GetString());
            Assert.Equal(
                "https://custom-mes.example.test",
                root.GetProperty("Mes").GetProperty("Endpoint").GetString());
            Assert.Equal(
                4321,
                root.GetProperty("Plc").GetProperty("TimeoutMs").GetInt32());
            Assert.True(root.GetProperty("Custom").GetProperty("Keep").GetBoolean());
        }

        public void AssertActivationDefaultsApplied(string moduleId)
        {
            using var document = JsonDocument.Parse(
                File.ReadAllText(ProfilePath));
            var root = document.RootElement;
            Assert.Equal(
                Target.MachineProfile,
                root.GetProperty("InstanceId").GetString());
            Assert.Equal(
                Target.MachineProfile,
                root
                    .GetProperty("Shell")
                    .GetProperty("MachineProfile")
                    .GetString());
            Assert.Equal(
                $"required-{NewVersion}",
                root
                    .GetProperty("Modules")
                    .GetProperty(moduleId)
                    .GetProperty("RequiredSetting")
                    .GetString());
        }

        public void AssertUnrelatedStateUnchanged()
        {
            Assert.Equal(_originalSqlite, File.ReadAllBytes(SqlitePath));
            Assert.Equal(_originalAccounts, File.ReadAllBytes(AccountsPath));
        }

        public void Dispose()
        {
            if (Directory.Exists(Root))
            {
                Directory.Delete(Root, recursive: true);
            }
        }

        private sealed class PackageHttpHandler(
            IReadOnlyDictionary<string, string> packagePaths)
            : HttpMessageHandler
        {
            protected override Task<HttpResponseMessage> SendAsync(
                HttpRequestMessage request,
                CancellationToken cancellationToken)
            {
                var moduleId = Uri.UnescapeDataString(
                    Path.GetFileNameWithoutExtension(
                        request.RequestUri?.AbsolutePath
                        ?? throw new InvalidOperationException("测试包请求缺少 URL。")));
                if (!packagePaths.TryGetValue(moduleId, out var packagePath))
                {
                    return Task.FromResult(
                        new HttpResponseMessage(HttpStatusCode.NotFound));
                }

                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new ByteArrayContent(File.ReadAllBytes(packagePath))
                });
            }
        }

        private static void CreatePackage(
            string path,
            string moduleId,
            string version)
        {
            using var archive = ZipFile.Open(path, ZipArchiveMode.Create);
            WriteEntry(
                archive,
                "plugin.json",
                $$"""
                {
                  "moduleId": "{{moduleId}}",
                  "displayName": "{{moduleId}}",
                  "version": "{{version}}",
                  "hostApiVersion": "{{EdgeClientHostRuntime.HostApiVersion}}",
                  "minHostVersion": "1.0.0",
                  "maxHostVersion": "99.0.0",
                  "entryAssembly": "IIoT.Edge.{{moduleId}}.dll",
                  "entryType": "IIoT.Edge.{{moduleId}}.DependencyInjection",
                  "supportedProcessType": "{{moduleId}}",
                  "dependencies": []
                }
                """);
            WriteEntry(
                archive,
                $"IIoT.Edge.{moduleId}.dll",
                "test-binary");
            WriteEntry(
                archive,
                "activation/manifest.json",
                $$"""
                {
                  "schemaVersion": 1,
                  "moduleId": "{{moduleId}}",
                  "profiles": [
                    {
                      "profileId": "LineA",
                      "launcherProfile": "launcher/launcher.profiles.{{moduleId}}.json",
                      "machineConfig": "machine/appsettings.machine.LineA.json"
                    }
                  ]
                }
                """);
            WriteEntry(
                archive,
                $"activation/launcher/launcher.profiles.{moduleId}.json",
                """
                [
                  {
                    "profileId": "LineA",
                    "displayName": "Line A",
                    "machineProfile": "LineA",
                    "executablePath": "../host/IIoT.Edge.Shell"
                  }
                ]
                """);
            WriteEntry(
                archive,
                "activation/machine/appsettings.machine.LineA.json",
                $$"""
                {
                  "InstanceId": "LineA",
                  "Shell": {
                    "MachineProfile": "LineA"
                  },
                  "Modules": {
                    "Enabled": [ "{{moduleId}}" ],
                    "{{moduleId}}": {
                      "RequiredSetting": "required-{{NewVersion}}"
                    }
                  }
                }
                """);
        }

        private static void WriteEntry(
            ZipArchive archive,
            string entryName,
            string content)
        {
            var entry = archive.CreateEntry(entryName);
            using var writer = new StreamWriter(entry.Open());
            writer.Write(content);
        }

        internal static string ComputeSha256(string path)
        {
            using var stream = File.OpenRead(path);
            return Convert.ToHexString(SHA256.HashData(stream));
        }

        private static void WriteText(string path, string content)
        {
            Directory.CreateDirectory(
                Path.GetDirectoryName(path)
                ?? throw new InvalidOperationException("test path has no directory"));
            File.WriteAllText(path, content);
        }

        private static void WriteBytes(string path, byte[] content)
        {
            Directory.CreateDirectory(
                Path.GetDirectoryName(path)
                ?? throw new InvalidOperationException("test path has no directory"));
            File.WriteAllBytes(path, content);
        }
    }

    private sealed class SimulatedProcessTerminationException : Exception
    {
    }
}
