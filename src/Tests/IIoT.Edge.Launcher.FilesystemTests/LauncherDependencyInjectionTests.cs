using IIoT.Edge.Application.Features.Updates;
using IIoT.Edge.Module.Contracts.Updates;
using IIoT.Edge.Infrastructure.Update.Configuration;
using IIoT.Edge.Infrastructure.Update.Host;
using IIoT.Edge.Infrastructure.Update.Packages;
using IIoT.Edge.Launcher;
using IIoT.Edge.Launcher.Models;
using IIoT.Edge.Launcher.Services;
using IIoT.Edge.UI.Shared.Localization;
using Microsoft.Extensions.DependencyInjection;
using System.Globalization;
using System.Reflection;
using Xunit;

namespace IIoT.Edge.Launcher.FilesystemTests;

public sealed class LauncherDependencyInjectionTests
{
    [Fact]
    public void AddLauncherServices_ShouldRegisterRequiredServices()
    {
        var services = new ServiceCollection();
        var baseDirectory = Path.Combine(Path.GetTempPath(), $"iiot-launcher-test-{Guid.NewGuid():N}");

        try
        {
            services.AddLauncherServices(baseDirectory);

            using var provider = services.BuildServiceProvider(
                new ServiceProviderOptions
                {
                    ValidateOnBuild = true,
                    ValidateScopes = true
                });

            Assert.IsType<LauncherProfileCatalog>(provider.GetRequiredService<ILauncherProfileCatalog>());
            Assert.IsType<ProcessStarter>(provider.GetRequiredService<IProcessStarter>());
            Assert.IsType<ShellInstanceIdResolver>(provider.GetRequiredService<IShellInstanceIdResolver>());
            Assert.IsType<NamedMutexShellInstanceProbe>(provider.GetRequiredService<IShellInstanceProbe>());
            Assert.IsType<ShellLaunchService>(provider.GetRequiredService<IShellLaunchService>());
            Assert.IsType<LauncherAccountCatalogInitializer>(
                provider.GetRequiredService<ILauncherAccountCatalogInitializer>());
            Assert.IsType<LauncherAccountCatalog>(provider.GetRequiredService<ILauncherAccountCatalog>());
            Assert.IsType<LocalLauncherAuthService>(provider.GetRequiredService<ILocalLauncherAuthService>());
            Assert.IsType<LauncherUpdateTargetFactory>(
                provider.GetRequiredService<ILauncherUpdateTargetFactory>());
            Assert.IsType<FileEdgeUpdateConfigInitializer>(
                provider.GetRequiredService<IEdgeUpdateConfigInitializer>());
            Assert.NotNull(provider.GetRequiredService<IEdgeUpdateConfigurationProvider>());
            Assert.NotNull(provider.GetRequiredService<IEdgeInstalledPluginCatalog>());
            Assert.NotNull(provider.GetRequiredService<IEdgeProfileModuleConfigurationStore>());
            Assert.NotNull(provider.GetRequiredService<EdgePluginPackageInstaller>());
            Assert.NotNull(provider.GetRequiredService<IEdgePluginCompositionTransaction>());
            Assert.NotNull(provider.GetRequiredService<IEdgeUpdateTransactionRecovery>());
            Assert.NotNull(provider.GetRequiredService<IEdgeReleaseService>());
            Assert.NotNull(provider.GetRequiredService<IEdgeHostUpdateService>());
            Assert.IsType<FileLauncherUpdateOperationGate>(
                provider.GetRequiredService<ILauncherUpdateOperationGate>());
        }
        finally
        {
            if (Directory.Exists(baseDirectory))
            {
                Directory.Delete(baseDirectory, recursive: true);
            }
        }
    }

    [Fact]
    public void LauncherAccountCatalogInitializer_ShouldNotCopySampleAccountAsDefaultCatalog()
    {
        var tempDirectory = Path.Combine(Path.GetTempPath(), $"iiot-launcher-test-{Guid.NewGuid():N}");

        try
        {
            Directory.CreateDirectory(tempDirectory);
            File.WriteAllText(
                LauncherAccountCatalog.GetCatalogPath(tempDirectory, LauncherAccountCatalog.SampleCatalogFileName),
                """
                [
                  {
                    "userName": "admin",
                    "displayName": "本地管理员",
                    "passwordHash": "hash",
                    "isEnabled": true
                  }
                ]
                """);

            var catalog = new LauncherAccountCatalog(tempDirectory);
            var initializer = new LauncherAccountCatalogInitializer(tempDirectory);

            initializer.EnsureCatalogExists();

            Assert.False(File.Exists(LauncherAccountCatalog.GetCatalogPath(tempDirectory)));
            Assert.Equal(LauncherAccountCatalogStatus.Missing, catalog.GetCatalogStatus());
            Assert.Throws<FileNotFoundException>(() => catalog.LoadAccounts());
        }
        finally
        {
            if (Directory.Exists(tempDirectory))
            {
                Directory.Delete(tempDirectory, recursive: true);
            }
        }
    }

    [Fact]
    public void LauncherAccountCatalog_WhenCatalogIsMissing_ShouldReportMissing()
    {
        var tempDirectory = Path.Combine(Path.GetTempPath(), $"iiot-launcher-test-{Guid.NewGuid():N}");

        try
        {
            var catalog = new LauncherAccountCatalog(tempDirectory);

            Assert.Equal(LauncherAccountCatalogStatus.Missing, catalog.GetCatalogStatus());
        }
        finally
        {
            if (Directory.Exists(tempDirectory))
            {
                Directory.Delete(tempDirectory, recursive: true);
            }
        }
    }

    [Fact]
    public void LauncherAccountCatalog_WhenCatalogIsEmpty_ShouldReportEmpty()
    {
        var tempDirectory = Path.Combine(Path.GetTempPath(), $"iiot-launcher-test-{Guid.NewGuid():N}");

        try
        {
            Directory.CreateDirectory(tempDirectory);
            File.WriteAllText(LauncherAccountCatalog.GetCatalogPath(tempDirectory), "[]");

            var catalog = new LauncherAccountCatalog(tempDirectory);

            Assert.Equal(LauncherAccountCatalogStatus.Empty, catalog.GetCatalogStatus());
        }
        finally
        {
            if (Directory.Exists(tempDirectory))
            {
                Directory.Delete(tempDirectory, recursive: true);
            }
        }
    }

    [Fact]
    public void LauncherAccountCatalog_WhenCatalogOnlyHasEmptyHashSample_ShouldReportNeedsInitialSetup()
    {
        var tempDirectory = Path.Combine(Path.GetTempPath(), $"iiot-launcher-test-{Guid.NewGuid():N}");

        try
        {
            Directory.CreateDirectory(tempDirectory);
            File.WriteAllText(
                LauncherAccountCatalog.GetCatalogPath(tempDirectory),
                """
                [
                  {
                    "userName": "101650",
                    "displayName": "现场启动管理员",
                    "passwordHash": "",
                    "isEnabled": true
                  }
                ]
                """);

            var catalog = new LauncherAccountCatalog(tempDirectory);

            Assert.Equal(LauncherAccountCatalogStatus.NeedsInitialSetup, catalog.GetCatalogStatus());
        }
        finally
        {
            if (Directory.Exists(tempDirectory))
            {
                Directory.Delete(tempDirectory, recursive: true);
            }
        }
    }

    [Fact]
    public void LauncherAccountCatalog_WhenCatalogHasInvalidHashFormat_ShouldReportCorruptAndRefuseInitialization()
    {
        var tempDirectory = Path.Combine(Path.GetTempPath(), $"iiot-launcher-test-{Guid.NewGuid():N}");

        try
        {
            Directory.CreateDirectory(tempDirectory);
            var path = LauncherAccountCatalog.GetCatalogPath(tempDirectory);
            File.WriteAllText(
                path,
                """
                [
                  {
                    "userName": "101650",
                    "displayName": "现场启动管理员",
                    "passwordHash": "not-a-hash",
                    "isEnabled": true
                  }
                ]
                """);
            var original = File.ReadAllText(path);

            var catalog = new LauncherAccountCatalog(tempDirectory);

            Assert.Equal(LauncherAccountCatalogStatus.Corrupt, catalog.GetCatalogStatus());
            Assert.Throws<InvalidOperationException>(() => catalog.InitializeAccount(
                "101650",
                "现场启动管理员",
                LauncherPasswordHasher.HashPassword("NewPass123!")));
            Assert.Equal(original, File.ReadAllText(path));
        }
        finally
        {
            if (Directory.Exists(tempDirectory))
            {
                Directory.Delete(tempDirectory, recursive: true);
            }
        }
    }

    [Fact]
    public void LauncherAccountCatalog_WhenCatalogIsInvalidJson_ShouldReportCorruptAndRefuseInitialization()
    {
        var tempDirectory = Path.Combine(Path.GetTempPath(), $"iiot-launcher-test-{Guid.NewGuid():N}");

        try
        {
            Directory.CreateDirectory(tempDirectory);
            var path = LauncherAccountCatalog.GetCatalogPath(tempDirectory);
            File.WriteAllText(path, "{ invalid json");
            var original = File.ReadAllText(path);

            var catalog = new LauncherAccountCatalog(tempDirectory);

            Assert.Equal(LauncherAccountCatalogStatus.Corrupt, catalog.GetCatalogStatus());
            Assert.Throws<InvalidOperationException>(() => catalog.InitializeAccount(
                "101650",
                "现场启动管理员",
                LauncherPasswordHasher.HashPassword("NewPass123!")));
            Assert.Equal(original, File.ReadAllText(path));
        }
        finally
        {
            if (Directory.Exists(tempDirectory))
            {
                Directory.Delete(tempDirectory, recursive: true);
            }
        }
    }

    [Fact]
    public void LauncherAccountCatalog_ShouldRoundTripAccounts()
    {
        var tempDirectory = Path.Combine(Path.GetTempPath(), $"iiot-launcher-test-{Guid.NewGuid():N}");

        try
        {
            Directory.CreateDirectory(tempDirectory);
            var path = LauncherAccountCatalog.GetCatalogPath(tempDirectory);
            File.WriteAllText(
                path,
                """
                [
                  {
                    "userName": "operator",
                    "displayName": "操作员",
                    "passwordHash": "hash",
                    "isEnabled": true
                  }
                ]
                """);

            var catalog = new LauncherAccountCatalog(tempDirectory);
            var loaded = catalog.LoadAccounts();

            var account = Assert.Single(loaded);
            Assert.Equal("operator", account.UserName);
            Assert.Equal("操作员", account.DisplayName);
            Assert.Equal("hash", account.PasswordHash);
            Assert.True(account.IsEnabled);
        }
        finally
        {
            if (Directory.Exists(tempDirectory))
            {
                Directory.Delete(tempDirectory, recursive: true);
            }
        }
    }

    [Fact]
    public void LauncherAccountCatalogInitializer_WhenCatalogExists_ShouldNotOverwriteExistingAccounts()
    {
        var tempDirectory = Path.Combine(Path.GetTempPath(), $"iiot-launcher-test-{Guid.NewGuid():N}");

        try
        {
            Directory.CreateDirectory(tempDirectory);
            var accountsPath = Path.Combine(tempDirectory, "protected", LauncherAccountCatalog.DefaultCatalogFileName);
            var samplePath = Path.Combine(tempDirectory, LauncherAccountCatalog.SampleCatalogFileName);
            Directory.CreateDirectory(Path.GetDirectoryName(accountsPath)!);
            File.WriteAllText(
                accountsPath,
                """
                [
                  {
                    "userName": "operator",
                    "displayName": "现场账号",
                    "passwordHash": "protected-hash",
                    "isEnabled": true
                  }
                ]
                """);
            File.WriteAllText(
                samplePath,
                """
                [
                  {
                    "userName": "admin",
                    "displayName": "样例账号",
                    "passwordHash": "sample-hash",
                    "isEnabled": true
                  }
                ]
                """);
            var originalAccounts = File.ReadAllText(accountsPath);

            var initializer = new LauncherAccountCatalogInitializer(
                new LauncherAccountCatalogPaths(accountsPath, samplePath));

            initializer.EnsureCatalogExists();

            Assert.Equal(originalAccounts, File.ReadAllText(accountsPath));
        }
        finally
        {
            if (Directory.Exists(tempDirectory))
            {
                Directory.Delete(tempDirectory, recursive: true);
            }
        }
    }

    [Fact]
    public void LauncherAccountCatalogInitializer_WhenSampleIsMissing_ShouldNotBlockStartup()
    {
        var tempDirectory = Path.Combine(Path.GetTempPath(), $"iiot-launcher-test-{Guid.NewGuid():N}");

        try
        {
            Directory.CreateDirectory(tempDirectory);
            var accountsPath = Path.Combine(tempDirectory, "protected-data", LauncherAccountCatalog.DefaultCatalogFileName);
            var samplePath = Path.Combine(tempDirectory, LauncherAccountCatalog.SampleCatalogFileName);

            var initializer = new LauncherAccountCatalogInitializer(
                new LauncherAccountCatalogPaths(accountsPath, samplePath));

            initializer.EnsureCatalogExists();

            Assert.False(File.Exists(accountsPath));
        }
        finally
        {
            if (Directory.Exists(tempDirectory))
            {
                Directory.Delete(tempDirectory, recursive: true);
            }
        }
    }

    [Fact]
    public void LauncherUpdateConfigInitializer_ShouldCreateConfigFromSample()
    {
        var tempDirectory = Path.Combine(Path.GetTempPath(), $"iiot-launcher-test-{Guid.NewGuid():N}");

        try
        {
            Directory.CreateDirectory(tempDirectory);
            var configPath = Path.Combine(tempDirectory, "protected-data", "launcher.update.json");
            var samplePath = Path.Combine(tempDirectory, FileEdgeUpdateConfigInitializer.SampleConfigFileName);
            File.WriteAllText(
                samplePath,
                """{"source":"","channel":"stable","targetRuntime":"win-x64"}""");

            var initializer = new FileEdgeUpdateConfigInitializer(
                new EdgeUpdateConfigPaths(configPath, samplePath));

            initializer.EnsureConfigExists();

            Assert.True(File.Exists(configPath));
            Assert.Equal(File.ReadAllText(samplePath), File.ReadAllText(configPath));
        }
        finally
        {
            if (Directory.Exists(tempDirectory))
            {
                Directory.Delete(tempDirectory, recursive: true);
            }
        }
    }

    [Fact]
    public void LauncherUpdateConfigInitializer_WhenLegacyConfigExists_ShouldPreserveValuesAndNormalizeKeys()
    {
        var tempDirectory = Path.Combine(Path.GetTempPath(), $"iiot-launcher-test-{Guid.NewGuid():N}");

        try
        {
            Directory.CreateDirectory(tempDirectory);
            var configPath = Path.Combine(tempDirectory, "protected-data", "launcher.update.json");
            var samplePath = Path.Combine(tempDirectory, FileEdgeUpdateConfigInitializer.SampleConfigFileName);
            Directory.CreateDirectory(Path.GetDirectoryName(configPath)!);
            File.WriteAllText(
                configPath,
                """{"Source":"http://existing.example/updates","Channel":"beta","TargetRuntime":"win-arm64","custom":"keep"}""");
            File.WriteAllText(
                samplePath,
                """{"source":"","channel":"stable","targetRuntime":"win-x64"}""");

            var initializer = new FileEdgeUpdateConfigInitializer(
                new EdgeUpdateConfigPaths(configPath, samplePath));

            initializer.EnsureConfigExists();

            var migrated = File.ReadAllText(configPath);
            Assert.Contains("\"source\": \"http://existing.example/updates\"", migrated, StringComparison.Ordinal);
            Assert.Contains("\"channel\": \"beta\"", migrated, StringComparison.Ordinal);
            Assert.Contains("\"targetRuntime\": \"win-arm64\"", migrated, StringComparison.Ordinal);
            Assert.Contains("\"custom\": \"keep\"", migrated, StringComparison.Ordinal);
            Assert.DoesNotContain("\"Source\"", migrated, StringComparison.Ordinal);
            Assert.DoesNotContain("\"Channel\"", migrated, StringComparison.Ordinal);
            Assert.DoesNotContain("\"TargetRuntime\"", migrated, StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(tempDirectory))
            {
                Directory.Delete(tempDirectory, recursive: true);
            }
        }
    }

    [Fact]
    public void LauncherUpdateConfigInitializer_WhenCurrentKeyIsEmpty_ShouldMigrateNonEmptyLegacyValue()
    {
        var tempDirectory = Path.Combine(Path.GetTempPath(), $"iiot-launcher-test-{Guid.NewGuid():N}");

        try
        {
            Directory.CreateDirectory(tempDirectory);
            var configPath = Path.Combine(tempDirectory, "protected-data", "launcher.update.json");
            var samplePath = Path.Combine(tempDirectory, FileEdgeUpdateConfigInitializer.SampleConfigFileName);
            Directory.CreateDirectory(Path.GetDirectoryName(configPath)!);
            File.WriteAllText(
                configPath,
                """{"source":"","Source":"http://existing.example/updates","channel":"stable","targetRuntime":"win-x64"}""");
            File.WriteAllText(
                samplePath,
                """{"source":"","channel":"stable","targetRuntime":"win-x64"}""");

            var initializer = new FileEdgeUpdateConfigInitializer(
                new EdgeUpdateConfigPaths(configPath, samplePath));

            initializer.EnsureConfigExists();

            var migrated = File.ReadAllText(configPath);
            Assert.Contains("\"source\": \"http://existing.example/updates\"", migrated, StringComparison.Ordinal);
            Assert.DoesNotContain("\"Source\"", migrated, StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(tempDirectory))
            {
                Directory.Delete(tempDirectory, recursive: true);
            }
        }
    }

    [Fact]
    public void StartupRecovery_ShouldHoldSharedUpdateGateAndReleaseItAfterRecovery()
    {
        var baseDirectory = Path.Combine(
            Path.GetTempPath(),
            $"iiot-launcher-recovery-test-{Guid.NewGuid():N}");

        try
        {
            var gate = new FileLauncherUpdateOperationGate(baseDirectory);
            var recovery = new GateObservingRecovery(
                new FileLauncherUpdateOperationGate(baseDirectory));
            var startupActions = new GateObservingStartupActions(
                new FileLauncherUpdateOperationGate(baseDirectory));
            var diagnostics = new LauncherStartupDiagnosticStore();
            var services = new ServiceCollection()
                .AddSingleton<ILauncherUpdateOperationGate>(gate)
                .AddSingleton<IEdgeUpdateTransactionRecovery>(recovery)
                .AddSingleton<ILauncherPluginActivationReconciler>(startupActions)
                .AddSingleton<ILauncherDeviceBindingImporter>(startupActions)
                .AddSingleton<ILauncherStartupCoordinator>(provider =>
                    new LauncherStartupCoordinator(
                        null!,
                        null!,
                        null!,
                        provider.GetRequiredService<ILauncherUpdateOperationGate>(),
                        provider.GetRequiredService<IEdgeUpdateTransactionRecovery>(),
                        provider.GetRequiredService<ILauncherPluginActivationReconciler>(),
                        provider.GetRequiredService<ILauncherDeviceBindingImporter>(),
                        diagnostics));
            using var provider = services.BuildServiceProvider();

            var ready = App.TryCompleteUpdateStartup(provider);

            Assert.True(ready);
            Assert.True(recovery.WasCalled);
            Assert.True(recovery.ObservedGateHeld);
            Assert.True(startupActions.ReconcileCalled);
            Assert.True(startupActions.BindingImportCalled);
            Assert.True(startupActions.ObservedGateHeld);
            using var leaseAfterRecovery = gate.TryAcquire();
            Assert.NotNull(leaseAfterRecovery);
        }
        finally
        {
            if (Directory.Exists(baseDirectory))
            {
                Directory.Delete(baseDirectory, recursive: true);
            }
        }
    }

    [Fact]
    public void StartupRecovery_WhenAnotherOperationHoldsGate_ShouldSkipRecovery()
    {
        var baseDirectory = Path.Combine(
            Path.GetTempPath(),
            $"iiot-launcher-recovery-test-{Guid.NewGuid():N}");

        try
        {
            var gateOwner = new FileLauncherUpdateOperationGate(baseDirectory);
            using var heldLease = gateOwner.TryAcquire();
            Assert.NotNull(heldLease);
            var recovery = new GateObservingRecovery(gateOwner);
            var diagnostics = new LauncherStartupDiagnosticStore();
            var services = new ServiceCollection()
                .AddSingleton<ILauncherUpdateOperationGate>(
                    new FileLauncherUpdateOperationGate(baseDirectory))
                .AddSingleton<IEdgeUpdateTransactionRecovery>(recovery)
                .AddSingleton<ILauncherStartupCoordinator>(provider =>
                    new LauncherStartupCoordinator(
                        null!,
                        null!,
                        null!,
                        provider.GetRequiredService<ILauncherUpdateOperationGate>(),
                        provider.GetRequiredService<IEdgeUpdateTransactionRecovery>(),
                        null!,
                        null!,
                        diagnostics));
            using var provider = services.BuildServiceProvider();

            var ready = App.TryCompleteUpdateStartup(provider);

            Assert.False(ready);
            Assert.False(recovery.WasCalled);
        }
        finally
        {
            if (Directory.Exists(baseDirectory))
            {
                Directory.Delete(baseDirectory, recursive: true);
            }
        }
    }

    [Fact]
    public void StartupCoordinator_WhenLocalStepsFail_ShouldContinueAndPublishSafeDiagnostics()
    {
        var baseDirectory = Path.Combine(
            Path.GetTempPath(),
            $"iiot-launcher-local-startup-test-{Guid.NewGuid():N}");
        const string sensitiveMessage = "account path and secret must not leak";
        try
        {
            var diagnostics = new LauncherStartupDiagnosticStore();
            var language = new RecordingLanguageService();
            var account = new ThrowingAccountInitializer(sensitiveMessage);
            var update = new RecordingUpdateConfigInitializer();
            var gate = new FileLauncherUpdateOperationGate(baseDirectory);
            var recovery = new GateObservingRecovery(gate);
            var activation = new ThrowingActivationReconciler(sensitiveMessage);
            var binding = new RecordingBindingImporter();
            var coordinator = new LauncherStartupCoordinator(
                language,
                account,
                update,
                gate,
                recovery,
                activation,
                binding,
                diagnostics);

            coordinator.PrepareLocalization();
            coordinator.Initialize();

            Assert.Equal(1, language.InitializeCallCount);
            Assert.Equal(1, update.EnsureCallCount);
            Assert.True(recovery.WasCalled);
            Assert.Equal(1, activation.ReconcileCallCount);
            Assert.Equal(1, binding.ApplyCallCount);
            Assert.Contains(
                diagnostics.Snapshot,
                item => item.ReasonCode == "LAUNCHER_ACCOUNT_CATALOG_INITIALIZATION_FAILED"
                        && item.ExceptionType == nameof(IOException));
            Assert.Contains(
                diagnostics.Snapshot,
                item => item.ReasonCode == "LAUNCHER_PLUGIN_ACTIVATION_RECONCILIATION_FAILED"
                        && item.ExceptionType == nameof(IOException));
            Assert.DoesNotContain(
                diagnostics.Snapshot,
                item => item.ToString().Contains(sensitiveMessage, StringComparison.Ordinal));
        }
        finally
        {
            if (Directory.Exists(baseDirectory))
            {
                Directory.Delete(baseDirectory, recursive: true);
            }
        }
    }

    [Fact]
    public void StartupCoordinator_WhenLocalStepThrowsUnknownFault_ShouldPropagateOriginalException()
    {
        var expected = new NullReferenceException("implementation fault");
        var diagnostics = new LauncherStartupDiagnosticStore();
        var coordinator = new LauncherStartupCoordinator(
            null!,
            new UnexpectedAccountInitializer(expected),
            null!,
            null!,
            null!,
            null!,
            null!,
            diagnostics);

        var actual = Assert.Throws<NullReferenceException>(coordinator.Initialize);

        Assert.Same(expected, actual);
        Assert.DoesNotContain(
            diagnostics.Snapshot,
            item => item.ReasonCode == "LAUNCHER_ACCOUNT_CATALOG_INITIALIZATION_FAILED");
    }

    [Theory]
    [InlineData("/tmp/edge-updates")]
    [InlineData("file:///tmp/edge-updates")]
    [InlineData("ftp://updates.example/edge/")]
    public void LauncherUpdateService_WhenSourceIsNotHttp_ShouldReject(string source)
    {
        var exception = Assert.Throws<TargetInvocationException>(
            () => InvokeCreateUpdateManager(source));

        var sourceException = Assert.IsType<InvalidOperationException>(
            exception.InnerException);
        Assert.Contains("HTTP/HTTPS", sourceException.Message, StringComparison.Ordinal);
    }

    private static object? InvokeCreateUpdateManager(string source)
        => typeof(VelopackHostUpdateService)
            .GetMethod(
                "CreateUpdateManager",
                BindingFlags.NonPublic | BindingFlags.Static)!
            .Invoke(null, [source]);

    private sealed class GateObservingRecovery(
        ILauncherUpdateOperationGate observingGate) : IEdgeUpdateTransactionRecovery
    {
        public bool WasCalled { get; private set; }

        public bool ObservedGateHeld { get; private set; }

        public EdgeUpdateTransactionRecoveryResult RecoverPendingTransaction()
        {
            WasCalled = true;
            using var competingLease = observingGate.TryAcquire();
            ObservedGateHeld = competingLease is null;
            return new EdgeUpdateTransactionRecoveryResult(
                Success: true,
                Recovered: false,
                Blocked: false);
        }

        public bool IsProfileBlocked(string machineProfile) => false;
    }

    private sealed class GateObservingStartupActions(
        ILauncherUpdateOperationGate observingGate)
        : ILauncherPluginActivationReconciler,
          ILauncherDeviceBindingImporter
    {
        public bool ReconcileCalled { get; private set; }

        public bool BindingImportCalled { get; private set; }

        public bool ObservedGateHeld { get; private set; } = true;

        public void Reconcile()
        {
            ReconcileCalled = true;
            ObserveGate();
        }

        public bool IsReady(LauncherPluginActivation activation) => true;

        public void ApplyPendingBindings()
        {
            BindingImportCalled = true;
            ObserveGate();
        }

        private void ObserveGate()
        {
            using var competingLease = observingGate.TryAcquire();
            ObservedGateHeld &= competingLease is null;
        }
    }

    private sealed class RecordingLanguageService : IAppLanguageService
    {
        private static readonly LanguageOption Language = new(
            CultureInfo.GetCultureInfo("zh-CN"),
            "中文");

        public int InitializeCallCount { get; private set; }
        public CultureInfo Current => Language.Culture;
        public LanguageOption CurrentOption => Language;
        public IReadOnlyList<LanguageOption> SupportedLanguages => [Language];
        public event EventHandler? LanguageChanged;

        public void Initialize() => InitializeCallCount++;
        public void Change(CultureInfo culture) => LanguageChanged?.Invoke(this, EventArgs.Empty);
        public string GetString(string key, string fallback = "") => fallback;
        public string Format(string key, string fallback, params object[] args)
            => string.Format(CultureInfo.InvariantCulture, fallback, args);
    }

    private sealed class ThrowingAccountInitializer(string message)
        : ILauncherAccountCatalogInitializer
    {
        public void EnsureCatalogExists() => throw new IOException(message);
    }

    private sealed class UnexpectedAccountInitializer(Exception exception)
        : ILauncherAccountCatalogInitializer
    {
        public void EnsureCatalogExists() => throw exception;
    }

    private sealed class RecordingUpdateConfigInitializer : IEdgeUpdateConfigInitializer
    {
        public int EnsureCallCount { get; private set; }
        public void EnsureConfigExists() => EnsureCallCount++;
        public bool TrySyncUpdateSource(string updateSource) => false;
    }

    private sealed class ThrowingActivationReconciler(string message)
        : ILauncherPluginActivationReconciler
    {
        public int ReconcileCallCount { get; private set; }
        public void Reconcile()
        {
            ReconcileCallCount++;
            throw new IOException(message);
        }

        public bool IsReady(LauncherPluginActivation activation) => false;
    }

    private sealed class RecordingBindingImporter : ILauncherDeviceBindingImporter
    {
        public int ApplyCallCount { get; private set; }
        public void ApplyPendingBindings() => ApplyCallCount++;
    }
}
