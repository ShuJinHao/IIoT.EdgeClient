using IIoT.Edge.Application.Common.Plc;
using IIoT.Edge.Application.Modules.Hardware;
using IIoT.Edge.Infrastructure.DeviceComm.Plc;
using IIoT.Edge.Infrastructure.DeviceComm.Plc.Store;
using IIoT.Edge.Infrastructure.DeviceComm.Signals;
using IIoT.Edge.Module.Contracts.Hardware;
using IIoT.Edge.Module.Contracts.Plc;
using IIoT.Edge.Module.Sdk.Hardware;
using System.Net.Sockets;

namespace IIoT.Edge.Runtime.WorkflowTests;

public sealed class PlcBusinessOnDemandReadCoordinatorBehaviorTests
{
    [Fact]
    public async Task Capture_ShouldReadExactTaskSignalSetAsOneAtomicBatch()
    {
        var response = new ushort[21];
        response[0] = 11;
        response[1] = 12;
        response[10] = 23;
        response[20] = 34;
        var service = new OnDemandPlcService();
        service.ReadOutcomes.Enqueue(response);
        using var runtime = new CancellationTokenSource();
        var coordinator = CreateCoordinator(service, runtime.Token);
        var required = new[] { "MG1.Code", "MG1.Quantity", "Shared.Speed" };

        Assert.True(coordinator.Handles(required));
        Assert.False(coordinator.TryCapture(required, out _));
        await WaitUntilAsync(() => service.ReadRequests.Count == 1);
        await runtime.CancelAsync();
        Assert.True(coordinator.TryCapture(required, out var snapshot));

        Assert.NotNull(snapshot);
        var request = Assert.Single(service.ReadRequests);
        Assert.Equal("D100", request.Address);
        Assert.Equal((ushort)21, request.Length);
        Assert.Equal(3, snapshot!.Signals.Count);
        Assert.All(snapshot.Signals.Values, signal =>
        {
            Assert.Equal(snapshot.Generation, signal.Generation);
            Assert.Equal(snapshot.BatchId, signal.BatchId);
            Assert.Equal(snapshot.CapturedAtUtc, signal.CapturedAtUtc);
            Assert.True(signal.ReadSucceeded);
        });
        Assert.Equal([(ushort)11, (ushort)12], snapshot.Signals["MG1.Code"].Words);
        Assert.Equal((ushort)23, Assert.Single(snapshot.Signals["MG1.Quantity"].Words));
        Assert.Equal((ushort)34, Assert.Single(snapshot.Signals["Shared.Speed"].Words));
    }

    [Fact]
    public async Task Capture_Mg1AndMg2_ShouldKeepRequestsAndSnapshotsIsolated()
    {
        var mg1Response = new ushort[21];
        mg1Response[0] = 101;
        mg1Response[10] = 111;
        mg1Response[20] = 121;
        var mg2Response = new ushort[21];
        mg2Response[0] = 222;
        mg2Response[10] = 202;
        mg2Response[20] = 212;
        var service = new OnDemandPlcService();
        service.ReadOutcomes.Enqueue(mg1Response);
        service.ReadOutcomes.Enqueue(mg2Response);
        using var runtime = new CancellationTokenSource();
        var coordinator = CreateCoordinator(service, runtime.Token);
        var mg1 = new[] { "MG1.Code", "MG1.Quantity", "Shared.Speed" };
        var mg2 = new[] { "MG2.Code", "MG2.Quantity", "Shared.Speed" };

        Assert.False(coordinator.TryCapture(mg1, out _));
        Assert.False(coordinator.TryCapture(mg2, out _));
        await WaitUntilAsync(() => service.ReadRequests.Count == 2);
        await runtime.CancelAsync();
        Assert.True(coordinator.TryCapture(mg1, out var mg1Snapshot));
        Assert.True(coordinator.TryCapture(mg2, out var mg2Snapshot));

        Assert.Equal(["D100", "D120"], service.ReadRequests.Select(static request => request.Address));
        Assert.NotEqual(mg1Snapshot!.BatchId, mg2Snapshot!.BatchId);
        Assert.Equal((ushort)101, mg1Snapshot.Signals["MG1.Code"].Words[0]);
        Assert.Equal((ushort)111, Assert.Single(mg1Snapshot.Signals["MG1.Quantity"].Words));
        Assert.Equal((ushort)121, Assert.Single(mg1Snapshot.Signals["Shared.Speed"].Words));
        Assert.Equal((ushort)202, mg2Snapshot.Signals["MG2.Code"].Words[0]);
        Assert.Equal((ushort)212, Assert.Single(mg2Snapshot.Signals["MG2.Quantity"].Words));
        Assert.Equal((ushort)222, Assert.Single(mg2Snapshot.Signals["Shared.Speed"].Words));
    }

    [Fact]
    public async Task Capture_WhenReadTimesOut_ShouldPublishFailedQualityWithoutDisconnecting()
    {
        var service = new OnDemandPlcService();
        service.ReadOutcomes.Enqueue(new TimeoutException("sensitive protocol detail"));
        using var runtime = new CancellationTokenSource();
        var coordinator = CreateCoordinator(service, runtime.Token);
        var required = new[] { "MG1.Code", "MG1.Quantity", "Shared.Speed" };

        Assert.False(coordinator.TryCapture(required, out _));
        await WaitUntilAsync(() => service.ReadRequests.Count == 1);
        await runtime.CancelAsync();
        Assert.True(coordinator.TryCapture(required, out var snapshot));

        Assert.True(service.IsConnected);
        Assert.Equal(0, service.DisconnectCallCount);
        Assert.All(snapshot!.Signals.Values, signal =>
        {
            Assert.False(signal.ReadSucceeded);
            Assert.Equal(PlcTaskRuntimeErrorCodes.Timeout, signal.FailureReason);
            Assert.All(signal.Words, static word => Assert.Equal((ushort)0, word));
        });
    }

    [Fact]
    public async Task Capture_WhenSocketFails_ShouldDisconnectAndPublishTransportFailureQuality()
    {
        var service = new OnDemandPlcService();
        service.ReadOutcomes.Enqueue(new SocketException());
        using var runtime = new CancellationTokenSource();
        var connectionStates = new List<bool>();
        var coordinator = CreateCoordinator(
            service,
            runtime.Token,
            connectionStates.Add);
        var required = new[] { "MG1.Code", "MG1.Quantity", "Shared.Speed" };

        Assert.False(coordinator.TryCapture(required, out _));
        await WaitUntilAsync(() => service.DisconnectCallCount == 1);
        await runtime.CancelAsync();
        Assert.True(coordinator.TryCapture(required, out var snapshot));

        Assert.False(service.IsConnected);
        Assert.Equal([false], connectionStates);
        Assert.All(snapshot!.Signals.Values, signal =>
        {
            Assert.False(signal.ReadSucceeded);
            Assert.Equal(PlcTaskRuntimeErrorCodes.TransportDisconnected, signal.FailureReason);
        });
    }

    private static PlcBusinessOnDemandReadCoordinator CreateCoordinator(
        OnDemandPlcService service,
        CancellationToken runtimeCancellation,
        Action<bool>? connectionStateChanged = null)
    {
        var buffer = new PlcBuffer(readSize: 0, writeSize: 0);
        return new PlcBusinessOnDemandReadCoordinator(
            service,
            buffer,
            CreateMappings(),
            new HashSet<string>(
                ["MG1.Code", "MG1.Quantity", "MG2.Code", "MG2.Quantity"],
                StringComparer.OrdinalIgnoreCase),
            new FakeLogService(),
            new PlcConnectionStatusStore(),
            connectionStateChanged ?? (_ => { }),
            new DefaultPlcSignalBlockPlanner(),
            new PlcIoRuntimePolicy(MaxSignalBlockWordCount: 100, DataReadLoopIntervalMs: 1000),
            static _ => Task.FromResult(1000),
            runtimeCancellation,
            deviceId: 1,
            plcCode: "PLC-01",
            deviceName: "PLC Display");
    }

    private static IReadOnlyCollection<PlcIoScanMapping> CreateMappings()
        =>
        [
            Mapping("MG1.Code", "D100", 1, addressCount: 2),
            Mapping("MG1.Quantity", "D110", 2),
            Mapping("Shared.Speed", "D120", 3),
            Mapping("MG2.Code", "D130", 4, addressCount: 2),
            Mapping("MG2.Quantity", "D140", 5)
        ];

    private static PlcIoScanMapping Mapping(
        string signalKey,
        string address,
        int sortOrder,
        int addressCount = 1)
        => new(
            signalKey,
            address,
            addressCount,
            IoMappingOptionCatalog.DataTypeUInt16,
            IoMappingOptionCatalog.DirectionRead,
            IoMappingOptionCatalog.CategorySingleRead,
            sortOrder);

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        while (!condition())
        {
            await Task.Yield();
            TestContext.Current.CancellationToken.ThrowIfCancellationRequested();
        }
    }

    private sealed class OnDemandPlcService : PlcServiceTestDouble
    {
        public Queue<object> ReadOutcomes { get; } = new();

        public List<(string Address, ushort Length)> ReadRequests { get; } = [];

        public override bool IsConnected { get; protected set; } = true;

        public int DisconnectCallCount { get; private set; }

        public override Task<List<T>> ReadDataAsync<T>(
            string address,
            ushort length,
            CancellationToken cancellationToken = default)
        {
            ReadRequests.Add((address, length));
            if (ReadOutcomes.TryDequeue(out var outcome))
            {
                if (outcome is Exception exception)
                {
                    throw exception;
                }

                if (outcome is ushort[] words && typeof(T) == typeof(ushort))
                {
                    return Task.FromResult(words.Select(static word => (T)(object)word).ToList());
                }
            }

            return Task.FromResult(Enumerable.Repeat((T)(object)(ushort)1, length).ToList());
        }

        public override Task DisconnectAsync(CancellationToken cancellationToken = default)
        {
            DisconnectCallCount++;
            IsConnected = false;
            return Task.CompletedTask;
        }
    }
}
