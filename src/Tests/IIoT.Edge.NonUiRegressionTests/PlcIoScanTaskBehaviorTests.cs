using IIoT.Edge.Application.Abstractions.Plc;
using IIoT.Edge.Application.Abstractions.Plc.Store;
using IIoT.Edge.Application.Features.Hardware.IoMappings;
using IIoT.Edge.Application.Modules.Hardware;
using IIoT.Edge.Domain.Hardware.Aggregates;
using IIoT.Edge.Infrastructure.DeviceComm.Barcode.Readers;
using IIoT.Edge.Infrastructure.DeviceComm.Plc;
using IIoT.Edge.Infrastructure.DeviceComm.Plc.Store;
using IIoT.Edge.Infrastructure.DeviceComm.Signals;
using IIoT.Edge.Module.Sdk.Signals;
using IIoT.Edge.SharedKernel.Context;
using IIoT.Edge.SharedKernel.Enums;
using System.Diagnostics;

namespace IIoT.Edge.NonUiRegressionTests;

public sealed class PlcIoScanTaskBehaviorTests
{
    private static readonly IPlcSignalBlockPlanner SignalBlockPlanner = new DefaultPlcSignalBlockPlanner();

    [Fact]
    public void ProductionContextSignalBindingStore_ShouldPreserveIoDisplayMetadata()
    {
        var context = new ProductionContext { DeviceName = "PLC-A" };
        var store = new ProductionContextSignalBindingStore();

        store.Set(
            context,
            [
                new(
                    "Homogenization.Interaction.Inbound",
                    "D701",
                    1,
                    "Int16",
                    "Read",
                    2,
                    "信号交互",
                    "扫码进站")
            ]);

        var binding = Assert.Single(store.Get(context));
        Assert.Equal("信号交互", binding.Category);
        Assert.Equal("扫码进站", binding.BusinessGroup);
    }

    [Fact]
    public async Task PlcBarcodeReader_WhenCancellationRequested_ShouldPropagateCancellation()
    {
        var plcService = new BlockingPlcService();
        var reader = new PlcBarcodeReader(plcService, "D100", codeCount: 1, wordsPerCode: 1);
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => reader.ReadAsync(cts.Token));
    }

    [Fact]
    public async Task PlcIoScanTask_ConnectAsync_WhenConnectTimesOut_ShouldLogAndStayDisconnected()
    {
        var plcService = new ScriptedPlcService();
        plcService.ConnectOutcomes.Enqueue(new TimeoutException("connect timeout"));

        var logger = new FakeLogService();
        var statusStore = new PlcConnectionStatusStore();
        var interaction = new PlcIoScanTask(
            plcService,
            new PlcDataStore(),
            CreateDevice(1, "PLC-A"),
            [],
            logger,
            SignalBlockPlanner,
            statusStore);

        await interaction.ConnectAsync();

        Assert.False(plcService.IsConnected);
        Assert.Equal(1, plcService.ConnectAsyncCallCount);
        Assert.Contains(logger.Entries, x => x.Message.Contains("PLC 连接异常", StringComparison.Ordinal));
        Assert.False(statusStore.GetSnapshot(1)?.IsConnected);
        Assert.Equal("connect timeout", statusStore.GetSnapshot(1)?.LastError);
    }

    [Fact]
    public async Task PlcIoScanTask_ConnectAsync_ShouldCapConfiguredTcpEndpointTimeout()
    {
        var plcService = new ScriptedPlcService();
        plcService.ConnectOutcomes.Enqueue(true);
        var device = CreateDevice(11, "PLC-ENDPOINT");
        device.UpdateEndpoint("10.1.2.3", 502, null, 4500);

        var interaction = new PlcIoScanTask(
            plcService,
            new PlcDataStore(),
            device,
            [],
            new FakeLogService(),
            SignalBlockPlanner);

        await interaction.ConnectAsync();

        var endpoint = Assert.IsType<TcpPlcEndpoint>(plcService.Endpoint);
        Assert.Equal("10.1.2.3", endpoint.Host);
        Assert.Equal(502, endpoint.Port);
        Assert.Equal(3000, endpoint.ConnectTimeoutMs);
    }

    [Fact]
    public async Task PlcIoScanTask_ConnectAsync_WhenConnectNeverReturns_ShouldTimeoutAndCloseConnection()
    {
        var plcService = new NeverCompletingConnectPlcService();
        var device = CreateDevice(12, "PLC-HANG");
        device.UpdateEndpoint("10.1.2.4", 502, null, 30);
        var statusStore = new PlcConnectionStatusStore();

        var interaction = new PlcIoScanTask(
            plcService,
            new PlcDataStore(),
            device,
            [],
            new FakeLogService(),
            SignalBlockPlanner,
            statusStore);

        var stopwatch = Stopwatch.StartNew();
        await interaction.ConnectAsync();

        Assert.True(stopwatch.ElapsedMilliseconds < 1000);
        Assert.Equal(1, plcService.ConnectAsyncCallCount);
        Assert.Equal(1, plcService.DisconnectCallCount);
        var snapshot = statusStore.GetSnapshot(device.Id);
        Assert.NotNull(snapshot);
        Assert.False(snapshot!.IsConnected);
        Assert.Equal(PlcConnectionState.Retrying, snapshot.ConnectionState);
        Assert.False(string.IsNullOrWhiteSpace(snapshot.LastError));
    }

    [Fact]
    public async Task PlcIoScanTask_WhenReadTimesOut_ShouldDisconnectAndReconnectBeforeRecovering()
    {
        var plcService = new ScriptedPlcService();
        plcService.ConnectOutcomes.Enqueue(true);
        plcService.ConnectOutcomes.Enqueue(true);
        plcService.ReadOutcomes.Enqueue(new TimeoutException("read timeout"));
        plcService.ReadOutcomes.Enqueue(new ushort[] { 7 });

        var dataStore = new PlcDataStore();
        dataStore.Register(1, readSize: 1, writeSize: 0);
        var statusStore = new PlcConnectionStatusStore();

        var interaction = new PlcIoScanTask(
            plcService,
            dataStore,
            CreateDevice(1, "PLC-A"),
            [CreateIoMapping(1, "Read", "D100", 1)],
            new FakeLogService(),
            SignalBlockPlanner,
            statusStore);

        var buffer = Assert.IsType<PlcBuffer>(dataStore.GetBuffer(1));
        await interaction.ConnectAsync();
        await Assert.ThrowsAsync<InvalidOperationException>(() => interaction.ExecuteOneCycleAsync());
        Assert.Equal(1, plcService.DisconnectCallCount);
        Assert.False(plcService.IsConnected);
        Assert.False(statusStore.GetSnapshot(1)?.IsConnected);

        await interaction.ConnectAsync();
        await interaction.ExecuteOneCycleAsync();

        Assert.True(plcService.ConnectAsyncCallCount >= 2);
        Assert.True(plcService.ReadAsyncCallCount >= 2);
        Assert.True(buffer.TryGetReadWords("Read-D100", out var readWords));
        Assert.Equal((ushort)7, Assert.Single(readWords));
        Assert.True(statusStore.GetSnapshot(1)?.IsConnected);
    }

    [Fact]
    public async Task PlcIoScanTask_WhenWriteTimesOut_ShouldDisconnectAndReconnectBeforeRecovering()
    {
        var plcService = new ScriptedPlcService();
        plcService.ConnectOutcomes.Enqueue(true);
        plcService.ConnectOutcomes.Enqueue(true);
        plcService.WriteOutcomes.Enqueue(new TimeoutException("write timeout"));
        plcService.WriteOutcomes.Enqueue(null);

        var dataStore = new PlcDataStore();
        dataStore.Register(2, readSize: 0, writeSize: 1);
        var statusStore = new PlcConnectionStatusStore();

        var buffer = Assert.IsType<PlcBuffer>(dataStore.GetBuffer(2));
        buffer.SetWriteValue(0, 9);

        var interaction = new PlcIoScanTask(
            plcService,
            dataStore,
            CreateDevice(2, "PLC-B"),
            [CreateIoMapping(2, "Write", "D200", 1)],
            new FakeLogService(),
            SignalBlockPlanner,
            statusStore);

        await interaction.ConnectAsync();
        await Assert.ThrowsAsync<InvalidOperationException>(() => interaction.ExecuteOneCycleAsync());
        Assert.Equal(1, plcService.DisconnectCallCount);
        Assert.False(plcService.IsConnected);
        Assert.False(statusStore.GetSnapshot(2)?.IsConnected);

        await interaction.ConnectAsync();
        await interaction.ExecuteOneCycleAsync();

        Assert.True(plcService.ConnectAsyncCallCount >= 2);
        Assert.True(plcService.WriteAsyncCallCount >= 2);
        Assert.True(statusStore.GetSnapshot(2)?.IsConnected);
    }

    [Fact]
    public async Task PlcIoScanTask_WhenSignalAddressesHaveGaps_ShouldReadOneMergedBlockAndBindBySignalKey()
    {
        var plcService = new ScriptedPlcService();
        plcService.ConnectOutcomes.Enqueue(true);
        plcService.ReadOutcomes.Enqueue(new ushort[] { 10, 0, 0, 13 });

        var dataStore = new PlcDataStore();
        dataStore.Register(5, readSize: 0, writeSize: 0);

        var interaction = new PlcIoScanTask(
            plcService,
            dataStore,
            CreateDevice(5, "PLC-GAP"),
            [
                CreateIoMapping(5, "Read", "D700", 1, sortOrder: 1),
                CreateIoMapping(5, "Read", "D703", 1, sortOrder: 4)
            ],
            new FakeLogService(),
            SignalBlockPlanner);

        await interaction.ExecuteOneCycleAsync();

        var request = Assert.Single(plcService.ReadRequests);
        Assert.Equal("D700", request.Address);
        Assert.Equal((ushort)4, request.Length);

        var buffer = Assert.IsType<PlcBuffer>(dataStore.GetBuffer(5));
        Assert.True(buffer.TryGetReadWords("Read-D700", out var first));
        Assert.True(buffer.TryGetReadWords("Read-D703", out var second));
        Assert.Equal((ushort)10, Assert.Single(first));
        Assert.Equal((ushort)13, Assert.Single(second));
    }

    [Fact]
    public async Task PlcIoScanTask_ShouldUseInjectedSignalBlockPlanner()
    {
        var plcService = new ScriptedPlcService();
        plcService.ConnectOutcomes.Enqueue(true);
        plcService.ReadOutcomes.Enqueue(new ushort[] { 42 });

        var dataStore = new PlcDataStore();
        dataStore.Register(10, readSize: 0, writeSize: 0);
        var planner = new SpySignalBlockPlanner();

        var interaction = new PlcIoScanTask(
            plcService,
            dataStore,
            CreateDevice(10, "PLC-INJECT"),
            [CreateIoMapping(10, "Read", "D700", 1)],
            new FakeLogService(),
            planner);

        await interaction.ExecuteOneCycleAsync();

        Assert.Equal(2, planner.PlanCalls.Count);
        Assert.Contains(planner.PlanCalls, call => !call.IsWrite && call.MappingCount == 1);

        var request = Assert.Single(plcService.ReadRequests);
        Assert.Equal("D900", request.Address);

        var buffer = Assert.IsType<PlcBuffer>(dataStore.GetBuffer(10));
        Assert.True(buffer.TryGetReadWords("Read-D700", out var words));
        Assert.Equal((ushort)42, Assert.Single(words));
    }

    [Fact]
    public async Task PlcIoScanTask_WhenSignalBlockExceedsPolicy_ShouldSplitReadBlocks()
    {
        var plcService = new ScriptedPlcService();
        plcService.ConnectOutcomes.Enqueue(true);
        plcService.ReadOutcomes.Enqueue(new ushort[] { 1 });
        plcService.ReadOutcomes.Enqueue(new ushort[] { 2 });

        var dataStore = new PlcDataStore();
        dataStore.Register(6, readSize: 0, writeSize: 0);

        var interaction = new PlcIoScanTask(
            plcService,
            dataStore,
            CreateDevice(6, "PLC-SPLIT"),
            [
                CreateIoMapping(6, "Read", "D700", 1, sortOrder: 1),
                CreateIoMapping(6, "Read", "D720", 1, sortOrder: 2)
            ],
            new FakeLogService(),
            SignalBlockPlanner,
            runtimePolicy: new PlcIoRuntimePolicy(MaxSignalBlockWordCount: 10));

        await interaction.ExecuteOneCycleAsync();

        Assert.Equal(["D700", "D720"], plcService.ReadRequests.Select(static x => x.Address));
        Assert.All(plcService.ReadRequests, request => Assert.Equal((ushort)1, request.Length));
    }

    [Fact]
    public async Task PlcIoScanTask_WhenWriteGapPolicyIsZero_ShouldWriteOneBlockWithZeroGaps()
    {
        var plcService = new ScriptedPlcService();
        plcService.ConnectOutcomes.Enqueue(true);

        var dataStore = new PlcDataStore();
        dataStore.Register(7, readSize: 0, writeSize: 0);
        var buffer = Assert.IsType<PlcBuffer>(dataStore.GetBuffer(7));
        buffer.SetWriteValue("Write-D600", 0, 1);
        buffer.SetWriteValue("Write-D603", 0, 4);

        var interaction = new PlcIoScanTask(
            plcService,
            dataStore,
            CreateDevice(7, "PLC-WRITE-ZERO"),
            [
                CreateIoMapping(7, "Write", "D600", 1, sortOrder: 1),
                CreateIoMapping(7, "Write", "D603", 1, sortOrder: 4)
            ],
            new FakeLogService(),
            SignalBlockPlanner,
            runtimePolicy: new PlcIoRuntimePolicy(WriteGapPolicy: PlcIoWriteGapPolicy.Zero));

        await interaction.ExecuteOneCycleAsync();

        var request = Assert.Single(plcService.WriteRequests);
        Assert.Equal("D600", request.Address);
        Assert.Equal([(ushort)1, (ushort)0, (ushort)0, (ushort)4], request.Data);
    }

    [Fact]
    public async Task PlcIoScanTask_WhenWriteGapPolicyIsSplit_ShouldWriteSeparateBlocks()
    {
        var plcService = new ScriptedPlcService();
        plcService.ConnectOutcomes.Enqueue(true);

        var dataStore = new PlcDataStore();
        dataStore.Register(8, readSize: 0, writeSize: 0);
        var buffer = Assert.IsType<PlcBuffer>(dataStore.GetBuffer(8));
        buffer.SetWriteValue("Write-D600", 0, 1);
        buffer.SetWriteValue("Write-D603", 0, 4);

        var interaction = new PlcIoScanTask(
            plcService,
            dataStore,
            CreateDevice(8, "PLC-WRITE-SPLIT"),
            [
                CreateIoMapping(8, "Write", "D600", 1, sortOrder: 1),
                CreateIoMapping(8, "Write", "D603", 1, sortOrder: 4)
            ],
            new FakeLogService(),
            SignalBlockPlanner,
            runtimePolicy: new PlcIoRuntimePolicy(WriteGapPolicy: PlcIoWriteGapPolicy.Split));

        await interaction.ExecuteOneCycleAsync();

        Assert.Equal(["D600", "D603"], plcService.WriteRequests.Select(static x => x.Address));
        Assert.Equal([(ushort)1], plcService.WriteRequests[0].Data);
        Assert.Equal([(ushort)4], plcService.WriteRequests[1].Data);
    }

    [Fact]
    public async Task PlcIoScanTask_ShouldOnlyScanRealtimeIoCategory()
    {
        var plcService = new ScriptedPlcService();
        plcService.ConnectOutcomes.Enqueue(true);
        plcService.ReadOutcomes.Enqueue(new ushort[] { 9 });

        var dataStore = new PlcDataStore();
        dataStore.Register(9, readSize: 0, writeSize: 0);

        var interaction = new PlcIoScanTask(
            plcService,
            dataStore,
            CreateDevice(9, "PLC-CATEGORY"),
            [
                CreateIoMapping(9, "Read", "D700", 1, category: IoMappingOptionCatalog.CategoryInteraction, sortOrder: 1),
                CreateIoMapping(9, "Read", "D300", 1, category: IoMappingOptionCatalog.CategorySingleRead, sortOrder: 2),
                CreateIoMapping(9, "Read", "D24500", 30, category: IoMappingOptionCatalog.CategoryContinuousRead, sortOrder: 3),
                CreateIoMapping(9, "Write", "D200", 1, category: IoMappingOptionCatalog.CategorySingleWrite, sortOrder: 4),
                CreateIoMapping(9, "Write", "D220", 8, category: IoMappingOptionCatalog.CategoryContinuousWrite, sortOrder: 5)
            ],
            new FakeLogService(),
            SignalBlockPlanner);

        await interaction.ExecuteOneCycleAsync();

        var request = Assert.Single(plcService.ReadRequests);
        Assert.Equal("D700", request.Address);
        Assert.Empty(plcService.WriteRequests);
    }

    [Fact]
    public async Task PlcIoScanTask_StartAsync_WhenCanceled_ShouldStopPolling()
    {
        var plcService = new ScriptedPlcService();
        plcService.ConnectOutcomes.Enqueue(true);

        var dataStore = new PlcDataStore();
        dataStore.Register(3, readSize: 1, writeSize: 0);

        var interaction = new PlcIoScanTask(
            plcService,
            dataStore,
            CreateDevice(3, "PLC-C"),
            [CreateIoMapping(3, "Read", "D300", 1)],
            new FakeLogService(),
            SignalBlockPlanner);

        using var cts = new CancellationTokenSource();
        var runTask = interaction.StartAsync(cts.Token);

        await WaitUntilAsync(() => plcService.ReadAsyncCallCount >= 2);
        var readCountBeforeCancel = plcService.ReadAsyncCallCount;

        await StopInteractionAsync(runTask, cts);
        var readCountAfterStop = plcService.ReadAsyncCallCount;
        Assert.True(readCountAfterStop >= readCountBeforeCancel);
        await AssertReadCountRemainsAsync(plcService, readCountAfterStop, TimeSpan.FromMilliseconds(80));

        Assert.Equal(readCountAfterStop, plcService.ReadAsyncCallCount);
    }

    [Fact]
    public async Task PlcIoScanTask_ExecuteOneCycleAsync_WhenCanceledDuringBackoff_ShouldPropagateCancellation()
    {
        var plcService = new ScriptedPlcService();
        plcService.ConnectOutcomes.Enqueue(false);

        var interaction = new PlcIoScanTask(
            plcService,
            new PlcDataStore(),
            CreateDevice(4, "PLC-D"),
            [],
            new FakeLogService(),
            SignalBlockPlanner);

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => interaction.ExecuteOneCycleAsync(cts.Token));
        Assert.Equal(1, plcService.ConnectAsyncCallCount);
    }

    private static NetworkDeviceEntity CreateDevice(int id, string deviceName)
    {
        var entity = NetworkDeviceEntity.Create(deviceName, DeviceType.PLC, "127.0.0.1", 102);
        entity.WithId(id);
        entity.UpdateDeviceModel("S7");
        return entity;
    }

    private static IoMappingEntity CreateIoMapping(
        int deviceId,
        string direction,
        string address,
        int addressCount,
        string? category = null,
        int sortOrder = 1)
    {
        var entity = IoMappingEntity.Create(
            deviceId,
            $"{direction}-{address}",
            address,
            addressCount,
            "UInt16",
            direction,
            category ?? IoMappingOptionCatalog.CategoryInteraction,
            "测试信号交互");
        entity.UpdateSortOrder(sortOrder);
        return entity;
    }

    private static async Task WaitUntilAsync(Func<bool> condition, int timeoutMs = 1500)
    {
        var stopwatch = Stopwatch.StartNew();
        while (stopwatch.ElapsedMilliseconds < timeoutMs)
        {
            if (condition())
            {
                return;
            }

            await Task.Yield();
        }

        throw new TimeoutException("Condition was not met within the test timeout.");
    }

    private static async Task AssertReadCountRemainsAsync(
        ScriptedPlcService plcService,
        int expected,
        TimeSpan duration)
    {
        var deadline = DateTime.UtcNow.Add(duration);
        while (DateTime.UtcNow < deadline)
        {
            Assert.Equal(expected, plcService.ReadAsyncCallCount);
            await Task.Yield();
        }

        Assert.Equal(expected, plcService.ReadAsyncCallCount);
    }

    private static async Task StopInteractionAsync(Task runTask, CancellationTokenSource cts)
    {
        cts.Cancel();

        try
        {
            await runTask;
        }
        catch (OperationCanceledException)
        {
        }
    }

    private sealed class BlockingPlcService : IPlcService
    {
        public bool IsConnected => true;

        public void Init(PlcEndpoint endpoint)
        {
        }

        public Task<bool> ConnectAsync() => Task.FromResult(true);

        public void Disconnect()
        {
        }

        public async Task<List<T>> ReadDataAsync<T>(string address, ushort length)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan).ConfigureAwait(false);
            return [];
        }

        public Task WriteDataAsync<T>(string address, List<T> data) => Task.CompletedTask;

        public void Dispose()
        {
        }
    }

    private sealed class SpySignalBlockPlanner : IPlcSignalBlockPlanner
    {
        public List<(bool IsWrite, int MappingCount)> PlanCalls { get; } = [];

        public IReadOnlyList<PlcSignalBlock> Plan(
            IReadOnlyCollection<PlcIoScanMapping> mappings,
            int maxBlockWordCount,
            PlcIoWriteGapPolicy writeGapPolicy,
            bool isWrite)
        {
            PlanCalls.Add((isWrite, mappings.Count));
            if (isWrite || mappings.Count == 0)
            {
                return [];
            }

            var mapping = Assert.Single(mappings);
            return [new PlcSignalBlock("D900", 1, [new PlcSignalBlockItem(mapping, 0)])];
        }
    }

    private sealed class ScriptedPlcService : IPlcService
    {
        public Queue<object?> ConnectOutcomes { get; } = new();
        public Queue<object?> ReadOutcomes { get; } = new();
        public Queue<object?> WriteOutcomes { get; } = new();
        public List<(string Address, ushort Length)> ReadRequests { get; } = [];
        public List<(string Address, ushort[] Data)> WriteRequests { get; } = [];

        public bool IsConnected { get; private set; }
        public PlcEndpoint? Endpoint { get; private set; }
        public int ConnectAsyncCallCount { get; private set; }
        public int DisconnectCallCount { get; private set; }
        public int ReadAsyncCallCount { get; private set; }
        public int WriteAsyncCallCount { get; private set; }

        public void Init(PlcEndpoint endpoint)
        {
            Endpoint = endpoint;
        }

        public Task<bool> ConnectAsync()
        {
            ConnectAsyncCallCount++;

            if (ConnectOutcomes.Count > 0)
            {
                var outcome = ConnectOutcomes.Dequeue();
                if (outcome is Exception ex)
                {
                    throw ex;
                }

                IsConnected = outcome as bool? ?? true;
                return Task.FromResult(IsConnected);
            }

            IsConnected = true;
            return Task.FromResult(true);
        }

        public void Disconnect()
        {
            DisconnectCallCount++;
            IsConnected = false;
        }

        public Task<List<T>> ReadDataAsync<T>(string address, ushort length)
        {
            ReadAsyncCallCount++;
            ReadRequests.Add((address, length));

            if (ReadOutcomes.Count > 0)
            {
                var outcome = ReadOutcomes.Dequeue();
                if (outcome is Exception ex)
                {
                    throw ex;
                }

                if (outcome is ushort[] values && typeof(T) == typeof(ushort))
                {
                    return Task.FromResult(values.Select(x => (T)(object)x).ToList());
                }
            }

            return Task.FromResult(Enumerable.Repeat((T)(object)(ushort)1, length).ToList());
        }

        public Task WriteDataAsync<T>(string address, List<T> data)
        {
            WriteAsyncCallCount++;
            if (typeof(T) == typeof(ushort))
            {
                WriteRequests.Add((address, data.Select(static x => (ushort)(object)x!).ToArray()));
            }

            if (WriteOutcomes.Count > 0)
            {
                var outcome = WriteOutcomes.Dequeue();
                if (outcome is Exception ex)
                {
                    throw ex;
                }
            }

            return Task.CompletedTask;
        }

        public void Dispose()
        {
        }
    }

    private sealed class NeverCompletingConnectPlcService : IPlcService
    {
        public bool IsConnected => false;

        public PlcEndpoint? Endpoint { get; private set; }

        public int ConnectAsyncCallCount { get; private set; }

        public int DisconnectCallCount { get; private set; }

        public void Init(PlcEndpoint endpoint)
        {
            Endpoint = endpoint;
        }

        public Task<bool> ConnectAsync()
        {
            ConnectAsyncCallCount++;
            return new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously).Task;
        }

        public void Disconnect()
        {
            DisconnectCallCount++;
        }

        public Task<List<T>> ReadDataAsync<T>(string address, ushort length)
            => throw new NotSupportedException();

        public Task WriteDataAsync<T>(string address, List<T> data)
            => throw new NotSupportedException();

        public void Dispose()
        {
        }
    }
}
