using System.Linq.Expressions;
using IIoT.Edge.Module.Contracts.Cloud;
using IIoT.Edge.Module.Contracts.Config;
using IIoT.Edge.Module.Contracts.Device;
using IIoT.Edge.Module.Contracts.Plc;
using IIoT.Edge.Module.Contracts.Plc.Store;
using IIoT.Edge.Infrastructure.Integration.EdgeHost;
using IIoT.Edge.Domain.Hardware.Aggregates;
using IIoT.Edge.Module.Contracts.Runtime;
using IIoT.Edge.SharedKernel.Domain;
using IIoT.Edge.Module.Contracts.Hardware;
using IIoT.Edge.SharedKernel.Repository;
using IIoT.Edge.SharedKernel.Specification;

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
            new InMemoryRepository<NetworkDeviceEntity>(plcA, plcB),
            new FakePlcConnectionManager(
                new PlcConnectionRuntimeSnapshot
                {
                    NetworkDeviceId = plcA.Id,
                    DeviceName = plcA.DeviceName,
                    IsConnected = true,
                    ConnectionState = PlcConnectionState.Connected,
                    LastReadAtUtc = observedAt
                }));

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
            new InMemoryRepository<NetworkDeviceEntity>(plc),
            new FakePlcConnectionManager(
                new PlcConnectionRuntimeSnapshot
                {
                    NetworkDeviceId = plc.Id,
                    DeviceName = plc.DeviceName,
                    ConnectionState = connectionState,
                    IsConnected = isConnected,
                    LastError = lastError
                }));

        var item = Assert.Single(await provider.GetCurrentAsync(TestContext.Current.CancellationToken));

        Assert.Equal(expectedRuntimeStatus, item.RuntimeStatus);
        Assert.Equal(expectedRuntimeStatus == "Connected", item.IsConnected);
        Assert.Equal(lastError, item.LastError);
    }

    [Fact]
    public async Task SnapshotProvider_WhenConfiguredPlcRenamed_ShouldKeepStableCodeAndReportNewName()
    {
        var plc = CreatePlc(1, "PLC-A01", "10.10.1.11", 6000, "MC-3E");
        plc.Rename("一号 PLC");
        var provider = new EdgeHostPlcRuntimeStateSnapshotProvider(
            new InMemoryRepository<NetworkDeviceEntity>(plc),
            new FakePlcConnectionManager());

        var item = Assert.Single(await provider.GetCurrentAsync(TestContext.Current.CancellationToken));

        Assert.Equal("PLC-A01", item.PlcCode);
        Assert.Equal("一号 PLC", item.ReportedPlcName);
    }

    [Fact]
    public async Task SnapshotProvider_WhenNoConfiguredPlcs_ShouldIgnoreStaleRuntimeSnapshots()
    {
        var provider = new EdgeHostPlcRuntimeStateSnapshotProvider(
            new InMemoryRepository<NetworkDeviceEntity>(),
            new FakePlcConnectionManager(
                new PlcConnectionRuntimeSnapshot
                {
                    NetworkDeviceId = 99,
                    DeviceName = "STALE-PLC",
                    ConnectionState = PlcConnectionState.Connected,
                    IsConnected = true
                }));

        var items = await provider.GetCurrentAsync(TestContext.Current.CancellationToken);

        Assert.Empty(items);
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

    private static NetworkDeviceEntity CreatePlc(
        int id,
        string deviceName,
        string ipAddress,
        int port,
        string protocol)
    {
        var device = NetworkDeviceEntity.Create(deviceName, DeviceType.PLC, ipAddress, port)
            .WithId(id);
        device.UpdateProtocolFrame(protocol);
        return device;
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

    private sealed class InMemoryRepository<T>(params T[] seedItems) : IRepository<T>
        where T : class, IEntity<int>, IAggregateRoot
    {
        private readonly List<T> _items = [.. seedItems];

        public IQueryable<T> GetQueryable() => _items.AsQueryable();

        public Task<T?> GetByIdAsync<TKey>(TKey id, CancellationToken cancellationToken = default)
            where TKey : notnull
            => Task.FromResult(_items.FirstOrDefault(x => EqualityComparer<TKey>.Default.Equals((TKey)(object)x.Id, id)));

        public Task<T?> GetAsync(
            Expression<Func<T, bool>> expression,
            Expression<Func<T, object>>[]? includes = null,
            CancellationToken cancellationToken = default)
            => Task.FromResult(_items.AsQueryable().FirstOrDefault(expression));

        public Task<List<T>> GetListAsync(
            Expression<Func<T, bool>> expression,
            CancellationToken cancellationToken = default)
            => Task.FromResult(_items.AsQueryable().Where(expression).ToList());

        public Task<List<T>> GetListAsync(
            Expression<Func<T, bool>> expression,
            Expression<Func<T, object>>[]? includes = null,
            CancellationToken cancellationToken = default)
            => Task.FromResult(_items.AsQueryable().Where(expression).ToList());

        public Task<List<T>> GetListAsync(
            ISpecification<T>? specification = null,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<T?> GetSingleOrDefaultAsync(
            ISpecification<T>? specification = null,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<int> GetCountAsync(
            Expression<Func<T, bool>> expression,
            CancellationToken cancellationToken = default)
            => Task.FromResult(_items.AsQueryable().Count(expression));

        public Task<int> CountAsync(
            ISpecification<T>? specification = null,
            CancellationToken cancellationToken = default)
            => Task.FromResult(_items.Count);

        public Task<bool> AnyAsync(
            ISpecification<T>? specification = null,
            CancellationToken cancellationToken = default)
            => Task.FromResult(_items.Count > 0);

        public T Add(T entity)
        {
            _items.Add(entity);
            return entity;
        }

        public void Update(T entity) { }
        public void Delete(T entity) => _items.Remove(entity);
        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) => Task.FromResult(0);

        public Task<int> ExecuteDeleteAsync(
            Expression<Func<T, bool>> predicate,
            CancellationToken cancellationToken = default)
        {
            var remove = _items.AsQueryable().Where(predicate).ToArray();
            foreach (var item in remove)
            {
                _items.Remove(item);
            }

            return Task.FromResult(remove.Length);
        }

        public async Task<int> ReplaceAsync(
            Expression<Func<T, bool>> predicate,
            IReadOnlyCollection<T> replacements,
            CancellationToken cancellationToken = default)
        {
            var affected = await ExecuteDeleteAsync(predicate, cancellationToken);
            foreach (var replacement in replacements)
            {
                Add(replacement);
                affected++;
            }

            return affected;
        }
    }
}
