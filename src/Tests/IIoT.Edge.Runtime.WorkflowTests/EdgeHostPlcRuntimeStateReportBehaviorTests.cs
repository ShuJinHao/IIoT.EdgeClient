using System.Linq.Expressions;
using IIoT.Edge.Module.Contracts.Cloud;
using IIoT.Edge.Module.Contracts.Config;
using IIoT.Edge.Module.Contracts.Device;
using IIoT.Edge.Module.Contracts.Plc;
using IIoT.Edge.Module.Contracts.Plc.Store;
using IIoT.Edge.Application.Common.Plugins;
using IIoT.Edge.Infrastructure.Integration.EdgeHost;
using IIoT.Edge.Module.Contracts.Runtime;
using IIoT.Edge.Module.Contracts.Hardware;
using IIoT.Edge.Module.Contracts.Identity;
using IIoT.Edge.Module.Contracts.Plugins;

namespace IIoT.Edge.Runtime.WorkflowTests;

public sealed class EdgeHostPlcRuntimeStateReportBehaviorTests
{
    [Fact]
    public async Task SnapshotProvider_WhenConfiguredPlcHasRuntimeSnapshot_ShouldOverlayRealRuntimeState()
    {
        var observedAt = new DateTimeOffset(2026, 7, 3, 8, 20, 30, TimeSpan.Zero);
        var plcA = CreatePlc(1, "PLC-A01", "10.10.1.11", 6000, "MC-3E");
        var plcB = CreatePlc(2, "PLC-A02", "10.10.1.12", 6000, "MC-3E");
        var provider = new EdgeHostPlcRuntimeStateSnapshotProvider(
            new InMemoryPluginConfiguration(100, plcA, plcB),
            new FakePlcConnectionManager(
                new PlcConnectionRuntimeSnapshot
                {
                    NetworkDeviceId = plcA.Id,
                    DeviceName = plcA.DeviceName,
                    IsConnected = true,
                    ConnectionState = PlcConnectionState.Connected,
                    LastReadAtUtc = observedAt
                }),
            CreateIdentifiedDeviceService());

        var items = await provider.GetCurrentAsync(TestContext.Current.CancellationToken);

        Assert.Equal(2, items.Count);
        var connected = Assert.Single(items, item => item.PlcCode == "PLC-A01");
        Assert.True(connected.IsConnected);
        Assert.Equal("Connected", connected.RuntimeStatus);
        Assert.Equal(observedAt.UtcDateTime, connected.ObservedAtUtc);
        Assert.Equal("MC-3E", connected.Protocol);
        Assert.Equal("10.10.1.11:6000", connected.Address);

        var uncollected = Assert.Single(items, item => item.PlcCode == "PLC-A02");
        Assert.False(uncollected.IsConnected);
        Assert.Equal("Unknown", uncollected.RuntimeStatus);
        Assert.Null(uncollected.ObservedAtUtc);
    }

    [Theory]
    [InlineData(PlcConnectionState.Connecting, false, null, "Unknown")]
    [InlineData(PlcConnectionState.Retrying, false, "连接超时", "Faulted")]
    [InlineData(PlcConnectionState.Disconnected, false, null, "Disconnected")]
    [InlineData(PlcConnectionState.Faulted, false, "配置错误", "Faulted")]
    [InlineData(PlcConnectionState.Connected, true, null, "Connected")]
    [InlineData(PlcConnectionState.Faulted, true, "旧错误", "Connected")]
    public async Task SnapshotProvider_ShouldMapRuntimeStatusWithoutCloudGuessing(
        PlcConnectionState connectionState,
        bool isConnected,
        string? lastError,
        string expectedRuntimeStatus)
    {
        var plc = CreatePlc(1, "PLC-A01", "10.10.1.11", 6000, "MC-3E");
        var provider = new EdgeHostPlcRuntimeStateSnapshotProvider(
            new InMemoryPluginConfiguration(100, plc),
            new FakePlcConnectionManager(
                new PlcConnectionRuntimeSnapshot
                {
                    NetworkDeviceId = plc.Id,
                    DeviceName = plc.DeviceName,
                    ConnectionState = connectionState,
                    IsConnected = isConnected,
                    LastError = lastError
                }),
            CreateIdentifiedDeviceService());

        var item = Assert.Single(await provider.GetCurrentAsync(TestContext.Current.CancellationToken));

        Assert.Equal(expectedRuntimeStatus, item.RuntimeStatus);
        Assert.Equal(expectedRuntimeStatus == "Connected", item.IsConnected);
        Assert.Equal(lastError, item.LastError);
    }

    [Fact]
    public async Task SnapshotProvider_WhenConfiguredPlcRenamed_ShouldKeepStableCodeAndReportNewName()
    {
        var plc = CreatePlc(1, "一号 PLC", "10.10.1.11", 6000, "MC-3E", "PLC-A01");
        var provider = new EdgeHostPlcRuntimeStateSnapshotProvider(
            new InMemoryPluginConfiguration(100, plc),
            new FakePlcConnectionManager(),
            CreateIdentifiedDeviceService());

        var item = Assert.Single(await provider.GetCurrentAsync(TestContext.Current.CancellationToken));

        Assert.Equal("PLC-A01", item.PlcCode);
        Assert.Equal("一号 PLC", item.ReportedPlcName);
    }

    [Fact]
    public async Task SnapshotProvider_WhenDeviceRowIsRebuilt_ShouldMatchRuntimeByStablePlcCode()
    {
        var plc = CreatePlc(99, "重建后名称", "10.10.1.11", 6000, "MC-3E");
        var provider = new EdgeHostPlcRuntimeStateSnapshotProvider(
            new InMemoryPluginConfiguration(100, plc),
            new FakePlcConnectionManager(
                new PlcConnectionRuntimeSnapshot
                {
                    NetworkDeviceId = 1,
                    PlcCode = plc.PlcCode,
                    DeviceName = "重建前名称",
                    ConnectionState = PlcConnectionState.Connected,
                    IsConnected = true
                }),
            CreateIdentifiedDeviceService());

        var item = Assert.Single(await provider.GetCurrentAsync(TestContext.Current.CancellationToken));

        Assert.True(item.IsConnected);
        Assert.Equal(plc.PlcCode, item.PlcCode);
        Assert.Equal("重建后名称", item.ReportedPlcName);
    }

    [Fact]
    public async Task SnapshotProvider_WhenRuntimePlcCodeConflictsWithSameRowId_ShouldFailClosed()
    {
        var plc = CreatePlc(1, "PLC-A01", "10.10.1.11", 6000, "MC-3E");
        var provider = new EdgeHostPlcRuntimeStateSnapshotProvider(
            new InMemoryPluginConfiguration(100, plc),
            new FakePlcConnectionManager(
                new PlcConnectionRuntimeSnapshot
                {
                    NetworkDeviceId = plc.Id,
                    PlcCode = "PLC-OTHER",
                    DeviceName = plc.DeviceName,
                    ConnectionState = PlcConnectionState.Connected,
                    IsConnected = true
                }),
            CreateIdentifiedDeviceService());

        var item = Assert.Single(await provider.GetCurrentAsync(TestContext.Current.CancellationToken));

        Assert.False(item.IsConnected);
        Assert.Equal("Unknown", item.RuntimeStatus);
    }

    [Fact]
    public async Task SnapshotProvider_WhenNoConfiguredPlcs_ShouldIgnoreStaleRuntimeSnapshots()
    {
        var provider = new EdgeHostPlcRuntimeStateSnapshotProvider(
            new InMemoryPluginConfiguration(100),
            new FakePlcConnectionManager(
                new PlcConnectionRuntimeSnapshot
                {
                    NetworkDeviceId = 99,
                    DeviceName = "STALE-PLC",
                    ConnectionState = PlcConnectionState.Connected,
                    IsConnected = true
                }),
            CreateIdentifiedDeviceService());

        var items = await provider.GetCurrentAsync(TestContext.Current.CancellationToken);

        Assert.Empty(items);
    }

    [Fact]
    public async Task AuthoritativeSnapshot_AfterRestart_ShouldUsePluginPersistentConfigurationVersion()
    {
        var deviceService = CreateIdentifiedDeviceService();
        var configuration = new InMemoryPluginConfiguration(100);
        var firstProvider = new EdgeHostPlcRuntimeStateSnapshotProvider(
            configuration,
            new FakePlcConnectionManager(),
            deviceService);
        var first = await ((IAuthoritativePlcSnapshotProvider)firstProvider)
            .GetCurrentAsync(TestContext.Current.CancellationToken);

        configuration.PublishVersion(101);
        firstProvider.Invalidate();
        var changed = await ((IAuthoritativePlcSnapshotProvider)firstProvider)
            .GetCurrentAsync(TestContext.Current.CancellationToken);

        var restartedProvider = new EdgeHostPlcRuntimeStateSnapshotProvider(
            configuration,
            new FakePlcConnectionManager(),
            deviceService);
        var afterRestart = await ((IAuthoritativePlcSnapshotProvider)restartedProvider)
            .GetCurrentAsync(TestContext.Current.CancellationToken);

        Assert.True(first.ClearProjection);
        Assert.True(changed.ClearProjection);
        Assert.Equal(101, changed.ConfigurationVersion);
        Assert.Equal(changed.ConfigurationVersion, afterRestart.ConfigurationVersion);
        Assert.True(afterRestart.ClearProjection);
    }

    [Fact]
    public async Task Reporter_WhenDeviceIsOnline_ShouldPostDedicatedPayload()
    {
        var cloudHttp = new FakeCloudHttpClient();
        var deviceService = new FakeDeviceService();
        var deviceId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        deviceService.SetOnline(new DeviceSession
        {
            DeviceId = deviceId,
            ClientCode = "EDGE-APUC",
            DeviceName = "AP 上位机",
            ProcessId = Guid.NewGuid(),
            UploadAccessToken = "token",
            UploadAccessTokenExpiresAtUtc = DateTimeOffset.UtcNow.AddMinutes(5)
        });
        var reporter = new EdgeHostPlcRuntimeStateReporter(
            new StaticPlcRuntimeStateSnapshotProvider(
            [
                new EdgeHostPlcRuntimeStateReportItem(
                    "PLC-A01",
                    "PLC-A01",
                    true,
                    "Connected",
                    new DateTime(2026, 7, 3, 8, 20, 30, DateTimeKind.Utc),
                    Protocol: "MC-3E",
                    Address: "10.10.1.11:6000")
            ]),
            cloudHttp,
            new FakeCloudApiEndpointProvider(),
            deviceService,
            new FakeLocalSystemRuntimeConfigService(),
            new FakeLogService());

        var result = await reporter.ReportOnceAsync(TestContext.Current.CancellationToken);

        Assert.True(result.Success);
        Assert.Equal(1, result.ReportedCount);
        Assert.Equal("/api/v1/edge/edge-hosts/plc-runtime-states", cloudHttp.LastPostUrl);

        var payload = Assert.IsType<EdgeHostPlcRuntimeStateReport>(cloudHttp.LastPayload);
        Assert.Equal(deviceId, payload.DeviceId);
        Assert.Equal("EDGE-APUC", payload.ClientCode);
        var plcState = Assert.Single(payload.PlcStates);
        Assert.Equal("PLC-A01", plcState.PlcCode);
        Assert.True(plcState.IsConnected);
        Assert.Equal("Connected", plcState.RuntimeStatus);
    }

    [Fact]
    public async Task Reporter_WhenDeviceSessionMissing_ShouldRefreshBootstrapAndSkipWithoutPost()
    {
        var cloudHttp = new FakeCloudHttpClient();
        var deviceService = new FakeDeviceService();
        var reporter = new EdgeHostPlcRuntimeStateReporter(
            new StaticPlcRuntimeStateSnapshotProvider(
            [
                new EdgeHostPlcRuntimeStateReportItem("PLC-A01", "PLC-A01", true, "Connected")
            ]),
            cloudHttp,
            new FakeCloudApiEndpointProvider(),
            deviceService,
            new FakeLocalSystemRuntimeConfigService(),
            new FakeLogService());

        var result = await reporter.ReportOnceAsync(TestContext.Current.CancellationToken);

        Assert.False(result.Success);
        Assert.Equal("device_unidentified", result.ReasonCode);
        Assert.Equal(1, deviceService.RefreshBootstrapCallCount);
        Assert.Equal(0, cloudHttp.PostCallCount);
    }

    [Fact]
    public async Task Reporter_WhenSnapshotIsEmpty_ShouldPostEmptyFullSnapshot()
    {
        var cloudHttp = new FakeCloudHttpClient();
        var deviceService = new FakeDeviceService();
        deviceService.SetOnline(new DeviceSession
        {
            DeviceId = Guid.Parse("22222222-2222-2222-2222-222222222222"),
            ClientCode = "EDGE-EMPTY",
            DeviceName = "空配置上位机",
            ProcessId = Guid.NewGuid(),
            UploadAccessToken = "token",
            UploadAccessTokenExpiresAtUtc = DateTimeOffset.UtcNow.AddMinutes(5)
        });
        var reporter = new EdgeHostPlcRuntimeStateReporter(
            new StaticPlcRuntimeStateSnapshotProvider([]),
            cloudHttp,
            new FakeCloudApiEndpointProvider(),
            deviceService,
            new FakeLocalSystemRuntimeConfigService(),
            new FakeLogService());

        var result = await reporter.ReportOnceAsync(TestContext.Current.CancellationToken);

        Assert.True(result.Success);
        Assert.Equal(0, result.ReportedCount);
        Assert.Equal(1, cloudHttp.PostCallCount);
        var payload = Assert.IsType<EdgeHostPlcRuntimeStateReport>(cloudHttp.LastPayload);
        Assert.Empty(payload.PlcStates);
    }

    private static DevicePluginPlcSnapshot CreatePlc(
        int id,
        string deviceName,
        string ipAddress,
        int port,
        string protocol,
        string? plcCode = null)
        => new(
            id,
            new DevicePluginPlcConfiguration(
                plcCode ?? deviceName,
                deviceName,
                "Mc",
                "FX5U",
                protocol,
                ipAddress,
                port,
                null,
                3000,
                true,
                null));

    private static FakeDeviceService CreateIdentifiedDeviceService()
    {
        var service = new FakeDeviceService();
        service.SetOnline(new DeviceSession
        {
            DeviceId = Guid.NewGuid(),
            ClientCode = "PLC-SNAPSHOT-DEVICE",
            DeviceName = "PLC snapshot test device",
            ProcessId = Guid.NewGuid(),
            UploadAccessToken = "token",
            UploadAccessTokenExpiresAtUtc = DateTimeOffset.UtcNow.AddMinutes(5)
        });
        return service;
    }

    private sealed class InMemoryPluginConfiguration(
        long version,
        params DevicePluginPlcSnapshot[] plcs)
        : IDevicePluginConfigurationSnapshotAccessor
    {
        private long _version = version;

        public bool IsInitialized => true;

        public DevicePluginConfigurationSnapshot GetRequiredSnapshot()
            => new(
                new DevicePluginIdentity("PLC-SNAPSHOT-DEVICE", "AP", "AP"),
                Volatile.Read(ref _version),
                plcs.Select(static item => item.Configuration).ToArray(),
                [],
                [],
                [],
                DateTimeOffset.UtcNow);

        public IReadOnlyList<DevicePluginPlcSnapshot> GetPlcs() => plcs;

        public IReadOnlyList<DevicePluginIoPointSnapshot> GetIoPoints() => [];

        public IReadOnlyList<DevicePluginTaskBindingSnapshot> GetTaskBindings() => [];

        public Task RefreshAsync(CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public void PublishVersion(long next) => Interlocked.Exchange(ref _version, next);
    }

    private sealed class StaticPlcRuntimeStateSnapshotProvider(
        IReadOnlyList<EdgeHostPlcRuntimeStateReportItem> items) : IEdgeHostPlcRuntimeStateSnapshotProvider
    {
        public Task<IReadOnlyList<EdgeHostPlcRuntimeStateReportItem>> GetCurrentAsync(
            CancellationToken cancellationToken = default)
            => Task.FromResult(items);
    }

    private sealed class FakePlcConnectionManager(params PlcConnectionRuntimeSnapshot[] snapshots) : IPlcConnectionManager
    {
        public Task InitializeAsync(CancellationToken ct = default) => Task.CompletedTask;
        public Task StopAsync(CancellationToken ct = default) => Task.CompletedTask;
        public Task ReloadAsync(string deviceName, CancellationToken ct = default) => Task.CompletedTask;
        public Task StopDeviceAsync(int networkDeviceId, CancellationToken ct = default) => Task.CompletedTask;
        public void RegisterTasks(string deviceName, Func<IPlcBuffer, ProductionContext, List<IPlcTask>> factory) { }
        public IPlcService? GetPlc(int networkDeviceId) => null;
        public ProductionContext? GetContext(string deviceName) => null;
        public void MarkRuntimeFault(int networkDeviceId, string deviceName, string error) { }
        public PlcConnectionRuntimeSnapshot? GetRuntimeStatus(int networkDeviceId)
            => snapshots.FirstOrDefault(snapshot => snapshot.NetworkDeviceId == networkDeviceId);
        public IReadOnlyCollection<PlcConnectionRuntimeSnapshot> GetRuntimeStatuses() => snapshots;
        public void Dispose() { }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

}
