using System.Collections.Concurrent;
using System.Data;
using IIoT.Edge.Application.Common.Identity;
using IIoT.Edge.Application.Common.Plugins;
using IIoT.Edge.Application.Common.Tasks;
using IIoT.Edge.Application.Features.Config.LocalParameterConfig;
using IIoT.Edge.Host.Bootstrap;
using IIoT.Edge.Host.Bootstrap.Modules;
using IIoT.Edge.Infrastructure.DeviceComm.Plc;
using IIoT.Edge.Infrastructure.Persistence.Dapper.Connection;
using IIoT.Edge.Infrastructure.Persistence.Dapper.Repository;
using IIoT.Edge.Module.Contracts.Diagnostics;
using IIoT.Edge.Module.Contracts.Config;
using IIoT.Edge.Module.Contracts.Identity;
using IIoT.Edge.Module.Contracts.Logging;
using IIoT.Edge.Module.Contracts.Modules;
using IIoT.Edge.Module.Contracts.Plugins;
using IIoT.Edge.Module.Contracts.Tasks;
using IIoT.Edge.Shell.Core;
using IIoT.Edge.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace IIoT.Edge.Startup.IntegrationTests;

public sealed class PluginDatabaseStartupTests
{
    [Fact]
    public async Task DatabaseStartup_UsesDeclaredOwnerAndPluginDbPath()
    {
        var root = CreatePluginRoot();
        try
        {
            var runtime = new FixedRuntimeContext();
            var owner = new FakePluginDatabaseOwner(Snapshot(version: 1));
            var logger = new FakeLogService();
            var cache = new DevicePluginConfigurationSnapshotCache(runtime, [owner], logger);
            var startup = new DevicePluginDatabaseStartup(
                runtime,
                [Descriptor(root, owner)],
                [owner],
                [owner],
                cache,
                logger);

            await startup.InitializeAsync(TestContext.Current.CancellationToken);

            Assert.Equal(2, owner.LifecycleRequests.Count);
            Assert.All(owner.LifecycleRequests, request =>
            {
                Assert.Equal(Path.Combine(root, "db", "plugin.db"), request.PluginDatabasePath);
                Assert.Null(request.LegacyV2DatabasePath);
                Assert.True(request.IsNewDatabase);
                Assert.Equal("CLIENT-01", request.Identity.NormalizedClientCode);
                Assert.Equal("AP", request.Identity.ModuleId);
            });
            Assert.Equal(1, owner.SnapshotReadCount);
            Assert.True(cache.IsInitialized);
            Assert.Equal(1, cache.GetRequiredSnapshot().ConfigurationVersion);
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task DatabaseStartup_WhenLifecycleAndStoreAreDifferentObjects_FailsBeforeDatabaseAccess()
    {
        var root = CreatePluginRoot();
        try
        {
            var runtime = new FixedRuntimeContext();
            var lifecycle = new FakePluginDatabaseOwner(Snapshot(version: 1));
            var store = new FakePluginDatabaseOwner(Snapshot(version: 1));
            var logger = new FakeLogService();
            var cache = new DevicePluginConfigurationSnapshotCache(runtime, [store], logger);
            var startup = new DevicePluginDatabaseStartup(
                runtime,
                [Descriptor(root, lifecycle)],
                [lifecycle],
                [store],
                cache,
                logger);

            var exception = await Assert.ThrowsAsync<DevicePluginDatabaseStartupException>(
                () => startup.InitializeAsync(TestContext.Current.CancellationToken));

            Assert.Equal("PLUGIN_DATABASE_PORT_OWNER_MISMATCH", exception.ReasonCode);
            Assert.Empty(lifecycle.LifecycleRequests);
            Assert.Empty(store.LifecycleRequests);
            Assert.Equal(0, store.SnapshotReadCount);
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task SnapshotCache_AfterWarmup_HotReadsNeverCallPluginStore()
    {
        var runtime = new FixedRuntimeContext();
        var store = new FakePluginDatabaseOwner(Snapshot(version: 1));
        var cache = new DevicePluginConfigurationSnapshotCache(
            runtime,
            [store],
            new FakeLogService());

        await cache.RefreshAsync(TestContext.Current.CancellationToken);
        for (var index = 0; index < 2_000; index++)
        {
            Assert.Single(cache.GetPlcs());
            Assert.Single(cache.GetIoPoints());
            Assert.Single(cache.GetTaskBindings());
        }

        Assert.Equal(1, store.SnapshotReadCount);
    }

    [Fact]
    public async Task SnapshotCache_OnCommittedVersionEvent_InvalidatesAndPublishesNewVersion()
    {
        var runtime = new FixedRuntimeContext();
        var store = new FakePluginDatabaseOwner(Snapshot(version: 1));
        var cache = new DevicePluginConfigurationSnapshotCache(
            runtime,
            [store],
            new FakeLogService());
        await cache.RefreshAsync(TestContext.Current.CancellationToken);

        store.Publish(Snapshot(version: 2, ipAddress: "10.0.0.22"));
        await WaitUntilAsync(
            () => cache.IsInitialized
                  && cache.GetRequiredSnapshot().ConfigurationVersion == 2,
            TestContext.Current.CancellationToken);

        Assert.Equal(2, store.SnapshotReadCount);
        Assert.Equal("10.0.0.22", cache.GetPlcs().Single().IpAddress);
    }

    [Fact]
    public async Task AppStartupInitializer_FormalV3RunsPluginDatabaseBeforeWorkflowTables()
    {
        var events = new ConcurrentQueue<string>();
        var directory = CreateTempDirectory("edge-startup-order");
        try
        {
            var services = new ServiceCollection();
            services.AddSingleton(new SqliteConnectionFactory(directory));
            services.AddSingleton<ITableInitializer>(new OrderedTableInitializer(events));
            await using var provider = services.BuildServiceProvider();
            var initializer = new AppStartupInitializer(
                provider,
                new FakeLogService(),
                new OrderedPluginDatabaseStartup(events),
                new FixedRuntimeContext());

            await initializer.InitializeAsync(TestContext.Current.CancellationToken);

            Assert.Equal(["plugin-db", "workflow-db"], events.ToArray());
        }
        finally
        {
            DeleteDirectory(directory);
        }
    }

    [Fact]
    public async Task AppStartupInitializer_WhenPluginDatabaseFails_DoesNotOpenWorkflowDatabase()
    {
        var events = new ConcurrentQueue<string>();
        var directory = CreateTempDirectory("edge-startup-failure");
        try
        {
            var services = new ServiceCollection();
            services.AddSingleton(new SqliteConnectionFactory(directory));
            services.AddSingleton<ITableInitializer>(new OrderedTableInitializer(events));
            await using var provider = services.BuildServiceProvider();
            var initializer = new AppStartupInitializer(
                provider,
                new FakeLogService(),
                new OrderedPluginDatabaseStartup(events, "PLUGIN_DATABASE_INTEGRITY_FAILED"),
                new FixedRuntimeContext());

            var exception = await Assert.ThrowsAsync<DevicePluginDatabaseStartupException>(
                () => initializer.InitializeAsync(TestContext.Current.CancellationToken));

            Assert.Equal("PLUGIN_DATABASE_INTEGRITY_FAILED", exception.ReasonCode);
            Assert.Equal(["plugin-db"], events.ToArray());
            Assert.Empty(Directory.EnumerateFiles(directory, "*.db"));
        }
        finally
        {
            DeleteDirectory(directory);
        }
    }

    [Fact]
    public async Task AppStartupInitializer_WhenFormalV3BindingIsMissing_FailsBeforeAnyDatabase()
    {
        var events = new ConcurrentQueue<string>();
        var services = new ServiceCollection();
        await using var provider = services.BuildServiceProvider();
        var initializer = new AppStartupInitializer(
            provider,
            new FakeLogService(),
            new OrderedPluginDatabaseStartup(events));

        var exception = await Assert.ThrowsAsync<DevicePluginDatabaseStartupException>(
            () => initializer.InitializeAsync(TestContext.Current.CancellationToken));

        Assert.Equal("PLUGIN_DATABASE_V3_REQUIRED", exception.ReasonCode);
        Assert.Empty(events);
    }

    [Fact]
    public void FormalV3ProductionComposition_ResolvesStartupAndRuntimeConfigWithoutLegacyUnitOfWork()
    {
        var directory = CreateTempDirectory("edge-formal-v3-composition");
        try
        {
            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["DevicePluginBinding:SchemaVersion"] = "3",
                    ["DevicePluginBinding:GenerationId"] = "generation-test",
                    ["DevicePluginBinding:ClientCode"] = "CLIENT-TEST",
                    ["DevicePluginBinding:ProcessType"] = "AP",
                    ["DevicePluginBinding:ModuleId"] = "AP",
                    ["DevicePluginBinding:PluginVersion"] = "2.0.22",
                    ["DevicePluginBinding:PackageSha256"] = new string('a', 64),
                    ["CloudApi:BaseUrl"] = "https://cloud.test",
                    ["CloudApi:ClientCode"] = "CLIENT-TEST",
                    ["CloudApi:Enabled"] = "true",
                    ["Shell:RuntimeDataRoot"] = Path.Combine(directory, "runtime")
                })
                .Build();
            var services = new ServiceCollection();
            services.AddEdgeHostBootstrap(
                new ViewRegistry(),
                configuration,
                new ShellRuntimePathResolver().Resolve(directory, configuration),
                "Production",
                discoveredModules: [],
                moduleCatalogIssues: [],
                configuredEnabledModuleIds: [],
                modules: []);

            using var provider = services.BuildServiceProvider();

            Assert.NotNull(provider.GetRequiredService<IAppStartupInitializer>());
            Assert.NotNull(provider.GetRequiredService<ILocalParameterConfigService>());
            Assert.NotNull(provider.GetRequiredService<ILocalSystemRuntimeConfigService>());
            Assert.DoesNotContain(
                services,
                descriptor => descriptor.ServiceType.Name == "IEdgeUnitOfWorkFactory");
        }
        finally
        {
            DeleteDirectory(directory);
        }
    }

    [Fact]
    public async Task AppLifecycle_WhenPluginDatabaseFails_DoesNotStartPlcRuntimeOrBackgroundServices()
    {
        var diagnostics = new OrderedDiagnosticsBuilder();
        var binder = new OrderedPlcBinder();
        var runtime = new OrderedRuntimeState();
        var background = new OrderedBackgroundServices();
        var manager = new AppLifecycleManager(
            new FailingStartupInitializer("PLUGIN_DATABASE_SCHEMA_UNKNOWN"),
            diagnostics,
            new InMemoryStartupDiagnosticsStore(),
            binder,
            runtime,
            background,
            new FakeLogService());

        var result = await manager.StartAsync(TestContext.Current.CancellationToken);

        Assert.False(result.Success);
        Assert.Equal("PLUGIN_DATABASE_SCHEMA_UNKNOWN", result.Message);
        Assert.Equal(0, diagnostics.CallCount);
        Assert.Equal(0, binder.CallCount);
        Assert.Equal(0, runtime.RestoreCallCount);
        Assert.Equal(0, background.StartCallCount);
    }

    private static ModulePluginDescriptor Descriptor(
        string pluginRoot,
        FakePluginDatabaseOwner owner)
    {
        var ownerType = owner.GetType();
        return new ModulePluginDescriptor(
            "AP",
            "AP",
            "AP",
            "2.0.22",
            "2.0.0",
            "2.0.0",
            "2.0.99",
            [],
            ownerType.Assembly.GetName().Name!,
            ownerType.FullName!,
            Path.Combine(pluginRoot, "app"),
            Path.Combine(pluginRoot, "app", "plugin.json"),
            Path.Combine(pluginRoot, "app", $"{ownerType.Assembly.GetName().Name}.dll"),
            PrivateDatabaseContract: new ModulePluginPrivateDatabaseContract(
                1,
                DevicePluginDatabaseContractVersions.LifecycleV1,
                DevicePluginDatabaseContractVersions.ConfigurationV1,
                ownerType.Assembly.GetName().Name!,
                ownerType.FullName!,
                RequiresProductionPlan: false));
    }

    private static DevicePluginConfigurationSnapshot Snapshot(
        long version,
        string ipAddress = "10.0.0.11")
        => new(
            new DevicePluginIdentity("CLIENT-01", "AP", "AP"),
            version,
            [new DevicePluginPlcConfiguration(
                "AP-PLC-01",
                "AP PLC",
                "Mc",
                "FX5U",
                "E4",
                ipAddress,
                6000,
                null,
                3000,
                true,
                null)],
            [new DevicePluginIoPointConfiguration(
                "AP-PLC-01",
                "AP.Status",
                "D100",
                1,
                "Int16",
                "Read",
                "单点读数据",
                "status",
                1,
                null)],
            [new DevicePluginTaskBindingConfiguration(
                "AP-PLC-01",
                "AP.Heartbeat",
                true,
                DateTimeOffset.UtcNow)],
            [],
            DateTimeOffset.UtcNow);

    private static string CreatePluginRoot()
    {
        var root = Path.Combine(CreateTempDirectory("edge-plugin-db"), "CLIENT-01");
        Directory.CreateDirectory(Path.Combine(root, "app"));
        return root;
    }

    private static string CreateTempDirectory(string prefix)
    {
        var path = Path.Combine(Path.GetTempPath(), prefix, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static void DeleteDirectory(string path)
    {
        var target = Directory.GetParent(path)?.Name == "CLIENT-01"
            ? Directory.GetParent(path)!.FullName
            : path;
        if (Directory.Exists(target))
        {
            Directory.Delete(target, recursive: true);
        }
    }

    private static async Task WaitUntilAsync(
        Func<bool> condition,
        CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(5));
        while (!condition())
        {
            await Task.Delay(10, timeout.Token);
        }
    }

    private sealed class FixedRuntimeContext : IDevicePluginRuntimeContext
    {
        public DevicePluginRuntimeIdentity Current { get; } = new(
            3,
            "generation-1",
            "CLIENT-01",
            "AP",
            "AP",
            "2.0.22",
            new string('a', 64));
    }

    private sealed class FakePluginDatabaseOwner(DevicePluginConfigurationSnapshot snapshot)
        : IDevicePluginDatabaseLifecycleV1,
          IDevicePluginConfigurationStoreV1
    {
        private DevicePluginConfigurationSnapshot _snapshot = snapshot;
        private int _snapshotReadCount;

        public event EventHandler<DevicePluginConfigurationChangedEventArgs>? ConfigurationChanged;

        public ConcurrentQueue<DevicePluginDatabaseLifecycleRequest> LifecycleRequests { get; } = new();

        public int SnapshotReadCount => Volatile.Read(ref _snapshotReadCount);

        public Task<DevicePluginDatabaseLifecycleResult> InspectAsync(
            DevicePluginDatabaseLifecycleRequest request,
            CancellationToken cancellationToken = default)
        {
            LifecycleRequests.Enqueue(request);
            return Task.FromResult(SuccessResult(seedApplied: false));
        }

        public Task<DevicePluginDatabaseLifecycleResult> InitializeOrMigrateAsync(
            DevicePluginDatabaseLifecycleRequest request,
            CancellationToken cancellationToken = default)
        {
            LifecycleRequests.Enqueue(request);
            return Task.FromResult(SuccessResult(seedApplied: request.IsNewDatabase));
        }

        public Task<DevicePluginConfigurationSnapshot> GetSnapshotAsync(
            CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _snapshotReadCount);
            return Task.FromResult(Volatile.Read(ref _snapshot));
        }

        public void Publish(DevicePluginConfigurationSnapshot next)
        {
            var previous = Volatile.Read(ref _snapshot);
            Volatile.Write(ref _snapshot, next);
            ConfigurationChanged?.Invoke(
                this,
                new DevicePluginConfigurationChangedEventArgs(
                    previous.ConfigurationVersion,
                    next.ConfigurationVersion));
        }

        public Task<DevicePluginConfigurationWriteResult> UpsertPlcAsync(
            DevicePluginPlcConfiguration configuration,
            long expectedConfigurationVersion,
            CancellationToken cancellationToken = default)
            => Rejected();

        public Task<DevicePluginConfigurationWriteResult> DeletePlcAsync(
            string plcCode,
            long expectedConfigurationVersion,
            CancellationToken cancellationToken = default)
            => Rejected();

        public Task<DevicePluginConfigurationWriteResult> UpsertIoPointAsync(
            DevicePluginIoPointConfiguration configuration,
            long expectedConfigurationVersion,
            CancellationToken cancellationToken = default)
            => Rejected();

        public Task<DevicePluginConfigurationWriteResult> DeleteIoPointAsync(
            string plcCode,
            string signalKey,
            long expectedConfigurationVersion,
            CancellationToken cancellationToken = default)
            => Rejected();

        public Task<DevicePluginConfigurationWriteResult> ReplaceTaskBindingsAsync(
            string plcCode,
            IReadOnlyList<DevicePluginTaskBindingConfiguration> bindings,
            long expectedConfigurationVersion,
            CancellationToken cancellationToken = default)
            => Rejected();

        public Task<DevicePluginConfigurationWriteResult> UpdateModuleSettingsAsync(
            IReadOnlyList<DevicePluginModuleSetting> settings,
            long expectedConfigurationVersion,
            CancellationToken cancellationToken = default)
            => Rejected();

        private static DevicePluginDatabaseLifecycleResult SuccessResult(bool seedApplied)
            => new(
                1,
                "202608100001_PrivateDatabaseOwner",
                ExistingDatabase: !seedApplied,
                NewDatabase: seedApplied,
                SeedApplied: seedApplied,
                new Dictionary<string, int>(),
                "ok",
                CutoverReady: true,
                FailureReasonCode: null);

        private static Task<DevicePluginConfigurationWriteResult> Rejected()
            => Task.FromResult(new DevicePluginConfigurationWriteResult(
                DevicePluginConfigurationWriteStatus.Rejected,
                0,
                "TEST_WRITE_NOT_SUPPORTED"));
    }

    private sealed class OrderedPluginDatabaseStartup(
        ConcurrentQueue<string> events,
        string? failureReasonCode = null) : IDevicePluginDatabaseStartup
    {
        public Task InitializeAsync(CancellationToken cancellationToken = default)
        {
            events.Enqueue("plugin-db");
            return failureReasonCode is null
                ? Task.CompletedTask
                : Task.FromException(new DevicePluginDatabaseStartupException(failureReasonCode));
        }
    }

    private sealed class OrderedTableInitializer(ConcurrentQueue<string> events) : ITableInitializer
    {
        public string DbName => "workflow-test";

        public Task InitializeTableAsync(IDbConnection connection)
        {
            events.Enqueue("workflow-db");
            return Task.CompletedTask;
        }
    }

    private sealed class FailingStartupInitializer(string reasonCode) : IAppStartupInitializer
    {
        public Task<IReadOnlyList<StartupDiagnosticIssue>> InitializeAsync(
            CancellationToken cancellationToken = default)
            => Task.FromException<IReadOnlyList<StartupDiagnosticIssue>>(
                new DevicePluginDatabaseStartupException(reasonCode));
    }

    private sealed class OrderedDiagnosticsBuilder : IStartupDiagnosticsReportBuilder
    {
        public int CallCount { get; private set; }

        public Task<StartupDiagnosticsReport> BuildAsync(CancellationToken cancellationToken = default)
        {
            CallCount++;
            return Task.FromResult(StartupDiagnosticsReport.Empty());
        }

        public bool HasBlockingIssues(IReadOnlyCollection<StartupDiagnosticIssue> issues) => false;

        public string BuildValidationMessage(IReadOnlyCollection<StartupDiagnosticIssue> issues)
            => string.Empty;
    }

    private sealed class InMemoryStartupDiagnosticsStore : IStartupDiagnosticsStore
    {
        public StartupDiagnosticsReport Current { get; private set; } = StartupDiagnosticsReport.Empty();

        public void Update(StartupDiagnosticsReport report) => Current = report;
    }

    private sealed class OrderedPlcBinder : IPlcRuntimeTaskBinder
    {
        public int CallCount { get; private set; }

        public Task BindAsync(CancellationToken cancellationToken = default)
        {
            CallCount++;
            return Task.CompletedTask;
        }

        public Task<PlcRuntimeTaskApplyResult> BindDeviceAsync(
            int networkDeviceId,
            bool applyToRunningDevice,
            CancellationToken cancellationToken = default)
            => Task.FromResult(new PlcRuntimeTaskApplyResult(
                PlcRuntimeTaskApplyState.Applied,
                []));
    }

    private sealed class OrderedRuntimeState : IAppRuntimeStateCoordinator
    {
        public int RestoreCallCount { get; private set; }

        public Task RestoreAsync(CancellationToken cancellationToken = default)
        {
            RestoreCallCount++;
            return Task.CompletedTask;
        }

        public Task SaveAsync(CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }

    private sealed class OrderedBackgroundServices : IBackgroundServiceCoordinator
    {
        public int StartCallCount { get; private set; }

        public Task StartAsync(CancellationToken cancellationToken = default)
        {
            StartCallCount++;
            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }
}
