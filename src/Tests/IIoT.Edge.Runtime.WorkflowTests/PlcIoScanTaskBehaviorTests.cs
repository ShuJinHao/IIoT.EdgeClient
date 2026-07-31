using IIoT.Edge.Module.Contracts.Logging;
using IIoT.Edge.Module.Contracts.Plc;
using IIoT.Edge.Module.Contracts.Plc.Store;
using IIoT.Edge.Application.Common.Plc;
using IIoT.Edge.Application.Features.Hardware.IoMappings;
using IIoT.Edge.Module.Sdk.Hardware;
using IIoT.Edge.Application.Modules.Hardware;
using IIoT.Edge.Module.Contracts.Hardware;
using IIoT.Edge.Domain.Hardware.Aggregates;
using IIoT.Edge.Infrastructure.DeviceComm.Barcode.Readers;
using IIoT.Edge.Infrastructure.DeviceComm.Plc;
using IIoT.Edge.Infrastructure.DeviceComm.Plc.Store;
using IIoT.Edge.Infrastructure.DeviceComm.Signals;
using IIoT.Edge.Module.Sdk.Signals;
using IIoT.Edge.Module.Contracts.Runtime;
using Microsoft.Extensions.Time.Testing;
using System.Reflection;
using McpXLib.Exceptions;

namespace IIoT.Edge.Runtime.WorkflowTests;

public sealed class PlcIoScanTaskBehaviorTests
{
    private static readonly IPlcSignalBlockPlanner SignalBlockPlanner = new DefaultPlcSignalBlockPlanner();

    [Fact]
    public void FailureClassifier_ShouldUseStructuredCategoriesAndPreferNestedSocket()
    {
        AssertFailure(
            new System.Net.Sockets.SocketException(),
            PlcOperationFailureKind.TransportDisconnected,
            PlcTaskRuntimeErrorCodes.TransportDisconnected,
            disconnectsTransport: true);
        AssertFailure(
            new TimeoutException("sensitive timeout detail"),
            PlcOperationFailureKind.Timeout,
            PlcTaskRuntimeErrorCodes.Timeout);
        AssertFailure(
            new McProtocolException("sensitive PLC response"),
            PlcOperationFailureKind.ProtocolRejected,
            PlcTaskRuntimeErrorCodes.ProtocolRejected);
        AssertFailure(
            new RecivePacketException("sensitive packet"),
            PlcOperationFailureKind.InvalidResponse,
            PlcTaskRuntimeErrorCodes.InvalidResponse);
        AssertFailure(
            new InvalidDataException("sensitive packet"),
            PlcOperationFailureKind.InvalidResponse,
            PlcTaskRuntimeErrorCodes.InvalidResponse);
        AssertFailure(
            new DeviceAddressException("sensitive address"),
            PlcOperationFailureKind.ConfigurationInvalid,
            PlcTaskRuntimeErrorCodes.ConfigurationInvalid);
        AssertFailure(
            new IOException("naked IO failure"),
            PlcOperationFailureKind.TaskFault,
            PlcTaskRuntimeErrorCodes.TaskFault);
        AssertFailure(
            new OperationCanceledException("not caller cancellation"),
            PlcOperationFailureKind.TaskFault,
            PlcTaskRuntimeErrorCodes.TaskFault);
        AssertFailure(
            new AggregateException(
                new FormatException("configuration"),
                new InvalidOperationException(
                    "wrapper",
                    new System.Net.Sockets.SocketException())),
            PlcOperationFailureKind.TransportDisconnected,
            PlcTaskRuntimeErrorCodes.TransportDisconnected,
            disconnectsTransport: true);

        static void AssertFailure(
            Exception exception,
            PlcOperationFailureKind expectedKind,
            string expectedCode,
            bool disconnectsTransport = false)
        {
            var failure = PlcOperationFailureClassifier.Classify(exception);
            Assert.Equal(expectedKind, failure.Kind);
            Assert.Equal(expectedCode, failure.ReasonCode);
            Assert.Equal(disconnectsTransport, failure.DisconnectsTransport);
            Assert.DoesNotContain("sensitive", failure.SafeDiagnostic, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void FailureClassifier_ShouldRecognizeWrappedCallerCancellationOnlyForCanceledToken()
    {
        using var caller = new CancellationTokenSource();
        var exception = new AggregateException(
            new InvalidOperationException(
                "wrapper",
                new OperationCanceledException(caller.Token)));

        Assert.False(PlcOperationFailureClassifier.IsCallerCancellation(exception, caller.Token));

        caller.Cancel();

        Assert.True(PlcOperationFailureClassifier.IsCallerCancellation(exception, caller.Token));
    }

    [Fact]
    public void ProductionContextSignalBindingStore_ShouldPreserveIoDisplayMetadata()
    {
        var context = new ProductionContext { DeviceName = "PLC-A" };
        var store = new ProductionContextSignalBindingStore();

        store.Set(
            context,
            [
                new(
                    "TestPlugin.Interaction.Inbound",
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
        using var cts = new CancellationTokenSource();

        var readTask = reader.ReadAsync(cts.Token);
        await plcService.ReadEntered.Task.WaitAsync(TestContext.Current.CancellationToken);
        cts.Cancel();

        var exception = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => readTask);
        Assert.Equal(cts.Token, exception.CancellationToken);
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

        await interaction.ConnectAsync(TestContext.Current.CancellationToken);

        Assert.False(plcService.IsConnected);
        Assert.Equal(1, plcService.ConnectAsyncCallCount);
        Assert.Contains(logger.Entries, x => x.Message.Contains("PLC 连接异常", StringComparison.Ordinal));
        Assert.False(statusStore.GetSnapshot(1)?.IsConnected);
        Assert.Equal(PlcConnectionState.Connecting, statusStore.GetSnapshot(1)?.ConnectionState);
        Assert.Null(statusStore.GetSnapshot(1)?.LastError);
        Assert.Contains(logger.Entries, x => x.Message.Contains("原因码=Timeout", StringComparison.Ordinal));
        Assert.DoesNotContain(logger.Entries, x => x.Message.Contains("connect timeout", StringComparison.Ordinal));
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

        await interaction.ConnectAsync(TestContext.Current.CancellationToken);

        var endpoint = Assert.IsType<TcpPlcEndpoint>(plcService.Endpoint);
        Assert.Equal("10.1.2.3", endpoint.Host);
        Assert.Equal(502, endpoint.Port);
        Assert.Equal(3000, endpoint.ConnectTimeoutMs);
    }

    [Fact]
    public async Task PlcIoScanTask_ConnectAsync_WhenConnectNeverReturns_ShouldClassifyTimeoutWithoutClosingTransport()
    {
        var timeProvider = new FakeTimeProvider();
        var plcService = new NeverCompletingConnectPlcService(timeProvider);
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

        var connectTask = interaction.ConnectAsync(TestContext.Current.CancellationToken);
        Assert.Equal(1, plcService.ConnectAsyncCallCount);
        timeProvider.Advance(TimeSpan.FromMilliseconds(30));
        await connectTask;

        Assert.Equal(1, plcService.ConnectAsyncCallCount);
        Assert.Equal(0, plcService.DisconnectCallCount);
        var snapshot = statusStore.GetSnapshot(device.Id);
        Assert.NotNull(snapshot);
        Assert.False(snapshot!.IsConnected);
        Assert.Equal(PlcConnectionState.Connecting, snapshot.ConnectionState);
        Assert.Null(snapshot.LastError);
    }

    [Fact]
    public async Task PlcIoScanTask_WhenReadTimesOut_ShouldPublishFailureQualityWithoutDisconnecting()
    {
        var plcService = new ScriptedPlcService();
        plcService.ConnectOutcomes.Enqueue(true);
        plcService.ReadOutcomes.Enqueue(new TimeoutException("read timeout"));
        plcService.ReadOutcomes.Enqueue(new ushort[] { 7 });

        var dataStore = new PlcDataStore();
        dataStore.Register(1, readSize: 1, writeSize: 0);
        var statusStore = new PlcConnectionStatusStore();

        var logger = new FakeLogService();
        var interaction = new PlcIoScanTask(
            plcService,
            dataStore,
            CreateDevice(1, "PLC-A"),
            [CreateIoMapping(1, "Read", "D100", 1)],
            logger,
            SignalBlockPlanner,
            statusStore);

        var buffer = Assert.IsType<PlcBuffer>(dataStore.GetBuffer(1));
        await interaction.ConnectAsync(TestContext.Current.CancellationToken);
        await interaction.ExecuteOneCycleAsync(TestContext.Current.CancellationToken);

        Assert.Equal(0, plcService.DisconnectCallCount);
        Assert.True(plcService.IsConnected);
        Assert.True(statusStore.GetSnapshot(1)?.IsConnected);
        Assert.False(buffer.TryGetReadWords("Read-D100", out var failedWords));
        Assert.Equal((ushort)0, Assert.Single(failedWords));
        Assert.True(buffer.TryGetReadSignalState("Read-D100", out var failedState));
        Assert.False(failedState.ReadSucceeded);
        Assert.NotNull(failedState.FailedAtUtc);
        Assert.Equal(PlcTaskRuntimeErrorCodes.Timeout, failedState.FailureReason);
        Assert.DoesNotContain(logger.Entries, x => x.Message.Contains("read timeout", StringComparison.Ordinal));

        await interaction.ExecuteOneCycleAsync(TestContext.Current.CancellationToken);

        Assert.Equal(1, plcService.ConnectAsyncCallCount);
        Assert.Equal(2, plcService.ReadAsyncCallCount);
        Assert.True(buffer.TryGetReadWords("Read-D100", out var readWords));
        Assert.Equal((ushort)7, Assert.Single(readWords));
        Assert.True(statusStore.GetSnapshot(1)?.IsConnected);
    }

    [Fact]
    public async Task PlcDataReadScanTask_WhenBufferLacksBatchPublisher_ShouldFailClosed()
    {
        var plcService = new ScriptedPlcService();
        plcService.ConnectOutcomes.Enqueue(true);
        await plcService.ConnectAsync(TestContext.Current.CancellationToken);
        var legacyBuffer = new LegacyTransportBuffer();
        var dataReadScan = new PlcDataReadScanTask(
            plcService,
            new FixedBufferDataStore(legacyBuffer),
            CreateDevice(25, "PLC-DATA-NO-BATCH"),
            [
                CreateIoMapping(
                    25,
                    "Read",
                    "D100",
                    1,
                    IoMappingOptionCatalog.CategorySingleRead)
            ],
            new FakeLogService(),
            SignalBlockPlanner);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => dataReadScan.ExecuteOneCycleAsync(TestContext.Current.CancellationToken));

        Assert.Contains("拒绝逐信号降级", exception.Message, StringComparison.Ordinal);
        Assert.Equal(0, legacyBuffer.UpdateReadSignalCallCount);
    }

    [Fact]
    public async Task PlcIoScanTask_WhenBufferLacksBatchPublisher_ShouldFailClosed()
    {
        var plcService = new ScriptedPlcService();
        plcService.ConnectOutcomes.Enqueue(true);
        var legacyBuffer = new LegacyTransportBuffer();
        var interaction = new PlcIoScanTask(
            plcService,
            new FixedBufferDataStore(legacyBuffer),
            CreateDevice(26, "PLC-IO-NO-BATCH"),
            [CreateIoMapping(26, "Read", "D100", 1)],
            new FakeLogService(),
            SignalBlockPlanner);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => interaction.ExecuteOneCycleAsync(TestContext.Current.CancellationToken));

        Assert.Contains("拒绝逐信号降级", exception.Message, StringComparison.Ordinal);
        Assert.Equal(0, legacyBuffer.UpdateReadSignalCallCount);
    }

    [Fact]
    public async Task PlcIoScanTask_WhenReadTimeoutClosesService_ShouldReconnectWithoutFalseDisconnectedProjection()
    {
        var plcService = new ScriptedPlcService
        {
            DropConnectionOnReadFailure = true
        };
        plcService.ConnectOutcomes.Enqueue(true);
        plcService.ConnectOutcomes.Enqueue(true);
        plcService.ReadOutcomes.Enqueue(new TimeoutException("read timeout"));
        plcService.ReadOutcomes.Enqueue(new ushort[] { 7 });

        var dataStore = new PlcDataStore();
        dataStore.Register(24, readSize: 1, writeSize: 0);
        var statusStore = new PlcConnectionStatusStore();
        var interaction = new PlcIoScanTask(
            plcService,
            dataStore,
            CreateDevice(24, "PLC-TIMEOUT-REOPEN"),
            [CreateIoMapping(24, "Read", "D100", 1)],
            new FakeLogService(),
            SignalBlockPlanner,
            statusStore);

        await interaction.ConnectAsync(TestContext.Current.CancellationToken);
        await interaction.ExecuteOneCycleAsync(TestContext.Current.CancellationToken);

        Assert.False(plcService.IsConnected);
        var afterTimeout = statusStore.GetSnapshot(24);
        Assert.NotNull(afterTimeout);
        Assert.True(afterTimeout!.IsConnected);
        Assert.Equal(PlcConnectionState.Connected, afterTimeout.ConnectionState);

        await interaction.ExecuteOneCycleAsync(TestContext.Current.CancellationToken);

        Assert.Equal(2, plcService.ConnectAsyncCallCount);
        var afterReconnect = statusStore.GetSnapshot(24);
        Assert.NotNull(afterReconnect);
        Assert.True(afterReconnect!.IsConnected);
        Assert.Equal(PlcConnectionState.Connected, afterReconnect.ConnectionState);
    }

    [Fact]
    public async Task PlcIoScanTask_WhenSocketCloses_ShouldMarkDisconnectedAndResetTransport()
    {
        var plcService = new ScriptedPlcService();
        plcService.ConnectOutcomes.Enqueue(true);
        plcService.ReadOutcomes.Enqueue(
            new System.Net.Sockets.SocketException(
                (int)System.Net.Sockets.SocketError.ConnectionReset));

        var dataStore = new PlcDataStore();
        dataStore.Register(23, readSize: 1, writeSize: 0);
        var statusStore = new PlcConnectionStatusStore();
        var interaction = new PlcIoScanTask(
            plcService,
            dataStore,
            CreateDevice(23, "PLC-TRANSPORT"),
            [CreateIoMapping(23, "Read", "D100", 1)],
            new FakeLogService(),
            SignalBlockPlanner,
            statusStore);

        await interaction.ConnectAsync(TestContext.Current.CancellationToken);
        await interaction.ExecuteOneCycleAsync(TestContext.Current.CancellationToken);

        Assert.Equal(1, plcService.DisconnectCallCount);
        Assert.False(plcService.IsConnected);
        var snapshot = statusStore.GetSnapshot(23);
        Assert.NotNull(snapshot);
        Assert.False(snapshot!.IsConnected);
        Assert.Equal(PlcConnectionState.Retrying, snapshot.ConnectionState);
        Assert.Contains("Transport", snapshot.LastError, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task PlcIoScanTask_WhenTransportConnects_ShouldMarkConnectedImmediately()
    {
        var plcService = new ScriptedPlcService();
        plcService.ConnectOutcomes.Enqueue(true);
        plcService.ReadOutcomes.Enqueue(new ushort[] { 9 });

        var dataStore = new PlcDataStore();
        dataStore.Register(15, readSize: 1, writeSize: 0);
        var statusStore = new PlcConnectionStatusStore();

        var interaction = new PlcIoScanTask(
            plcService,
            dataStore,
            CreateDevice(15, "PLC-PROTOCOL"),
            [CreateIoMapping(15, "Read", "D100", 1)],
            new FakeLogService(),
            SignalBlockPlanner,
            statusStore);

        await interaction.ConnectAsync(TestContext.Current.CancellationToken);

        var snapshotAfterConnect = statusStore.GetSnapshot(15);
        Assert.NotNull(snapshotAfterConnect);
        Assert.True(snapshotAfterConnect!.IsConnected);
        Assert.Equal(PlcConnectionState.Connected, snapshotAfterConnect.ConnectionState);
        Assert.NotNull(snapshotAfterConnect.LatencyMs);

        await interaction.ExecuteOneCycleAsync(TestContext.Current.CancellationToken);

        var snapshotAfterRead = statusStore.GetSnapshot(15);
        Assert.NotNull(snapshotAfterRead);
        Assert.True(snapshotAfterRead!.IsConnected);
        Assert.Equal(PlcConnectionState.Connected, snapshotAfterRead.ConnectionState);
        Assert.NotNull(snapshotAfterRead.LastReadAtUtc);
    }

    [Fact]
    public async Task PlcIoScanTask_WhenLaterReadBlockFails_ShouldIsolateFailedBlock()
    {
        var plcService = new ScriptedPlcService();
        plcService.ConnectOutcomes.Enqueue(true);
        plcService.ReadOutcomes.Enqueue(new ushort[] { 1 });
        plcService.ReadOutcomes.Enqueue(new TimeoutException("second block timeout"));

        var dataStore = new PlcDataStore();
        dataStore.Register(16, readSize: 0, writeSize: 0);
        var statusStore = new PlcConnectionStatusStore();
        statusStore.MarkConnected(
            16,
            "PLC-SPLIT-FAIL",
            "PLC-SPLIT-FAIL",
            11);

        var interaction = new PlcIoScanTask(
            plcService,
            dataStore,
            CreateDevice(16, "PLC-SPLIT-FAIL"),
            [
                CreateIoMapping(16, "Read", "D700", 1, sortOrder: 1),
                CreateIoMapping(16, "Read", "D720", 1, sortOrder: 2)
            ],
            new FakeLogService(),
            SignalBlockPlanner,
            statusStore,
            runtimePolicy: new PlcIoRuntimePolicy(MaxSignalBlockWordCount: 10));

        await interaction.ExecuteOneCycleAsync(TestContext.Current.CancellationToken);

        var snapshot = statusStore.GetSnapshot(16);
        Assert.NotNull(snapshot);
        Assert.True(snapshot!.IsConnected);
        Assert.Equal(PlcConnectionState.Connected, snapshot.ConnectionState);
        Assert.Equal(0, plcService.DisconnectCallCount);

        var buffer = Assert.IsType<PlcBuffer>(dataStore.GetBuffer(16));
        Assert.True(buffer.TryGetReadWords("Read-D700", out var successfulWords));
        Assert.Equal((ushort)1, Assert.Single(successfulWords));
        Assert.False(buffer.TryGetReadWords("Read-D720", out var failedWords));
        Assert.Equal((ushort)0, Assert.Single(failedWords));
    }

    [Fact]
    public async Task PlcIoScanTask_WhenWriteTimesOut_ShouldKeepIntentAndRetryWithoutDisconnecting()
    {
        var plcService = new ScriptedPlcService();
        plcService.ConnectOutcomes.Enqueue(true);
        plcService.ReadOutcomes.Enqueue(new ushort[] { 1 });
        plcService.ReadOutcomes.Enqueue(new ushort[] { 1 });
        plcService.WriteOutcomes.Enqueue(new TimeoutException("write timeout"));
        plcService.WriteOutcomes.Enqueue(null);

        var dataStore = new PlcDataStore();
        dataStore.Register(
            2,
            readSize: 0,
            writeSize: 1,
            [new PlcBufferSignalBinding("Write-D200", "Write", 0, 1)]);
        var statusStore = new PlcConnectionStatusStore();

        var buffer = Assert.IsType<PlcBuffer>(dataStore.GetBuffer(2));
        buffer.SetWriteValue(0, 9);

        var interaction = new PlcIoScanTask(
            plcService,
            dataStore,
            CreateDevice(2, "PLC-B"),
            [
                CreateIoMapping(2, "Read", "D100", 1, sortOrder: 1),
                CreateIoMapping(2, "Write", "D200", 1, sortOrder: 2)
            ],
            new FakeLogService(),
            SignalBlockPlanner,
            statusStore);

        await interaction.ConnectAsync(TestContext.Current.CancellationToken);
        await interaction.ExecuteOneCycleAsync(TestContext.Current.CancellationToken);
        Assert.Equal(0, plcService.DisconnectCallCount);
        Assert.True(plcService.IsConnected);
        Assert.True(statusStore.GetSnapshot(2)?.IsConnected);
        Assert.Equal((ushort)9, Assert.Single(buffer.GetWriteBuffer()));

        await interaction.ExecuteOneCycleAsync(TestContext.Current.CancellationToken);

        Assert.Equal(1, plcService.ConnectAsyncCallCount);
        Assert.Equal(2, plcService.WriteAsyncCallCount);
        Assert.All(plcService.WriteRequests, request =>
            Assert.Equal((ushort)9, Assert.Single(request.Data)));
        Assert.True(statusStore.GetSnapshot(2)?.IsConnected);
    }

    [Fact]
    public async Task PlcIoScanTask_WhenWriteMappingHasNoReadProbe_ShouldWriteAfterTcpConnects()
    {
        var plcService = new ScriptedPlcService();
        plcService.ConnectOutcomes.Enqueue(true);

        var dataStore = new PlcDataStore();
        dataStore.Register(18, readSize: 0, writeSize: 1);
        var buffer = Assert.IsType<PlcBuffer>(dataStore.GetBuffer(18));
        buffer.SetWriteValue("Write-D200", 0, 9);
        var statusStore = new PlcConnectionStatusStore();

        var interaction = new PlcIoScanTask(
            plcService,
            dataStore,
            CreateDevice(18, "PLC-WRITE-NO-PROBE"),
            [CreateIoMapping(18, "Write", "D200", 1)],
            new FakeLogService(),
            SignalBlockPlanner,
            statusStore);

        await interaction.ExecuteOneCycleAsync(TestContext.Current.CancellationToken);

        var request = Assert.Single(plcService.WriteRequests);
        Assert.Equal("D200", request.Address);
        Assert.Equal((ushort)9, Assert.Single(request.Data));
        var snapshot = statusStore.GetSnapshot(18);
        Assert.NotNull(snapshot);
        Assert.True(snapshot!.IsConnected);
        Assert.Equal(PlcConnectionState.Connected, snapshot.ConnectionState);
    }

    [Fact]
    public async Task PlcIoScanTask_WhenSignalAddressesHaveGaps_ShouldReadMinimumCoveringBlockAndBindBySignalKey()
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

        await interaction.ExecuteOneCycleAsync(TestContext.Current.CancellationToken);

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

        await interaction.ExecuteOneCycleAsync(TestContext.Current.CancellationToken);

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

        await interaction.ExecuteOneCycleAsync(TestContext.Current.CancellationToken);

        Assert.Equal(["D700", "D720"], plcService.ReadRequests.Select(static x => x.Address));
        Assert.All(plcService.ReadRequests, request => Assert.Equal((ushort)1, request.Length));
    }

    [Fact]
    public async Task PlcIoScanTask_WhenReadMappingsHaveGap_ShouldMergeWithinWordLimit()
    {
        var plcService = new ScriptedPlcService();
        plcService.ConnectOutcomes.Enqueue(true);
        plcService.ReadOutcomes.Enqueue(
            Enumerable.Range(1, 21).Select(static value => (ushort)value).ToArray());

        var dataStore = new PlcDataStore();
        dataStore.Register(16, readSize: 0, writeSize: 0);

        var interaction = new PlcIoScanTask(
            plcService,
            dataStore,
            CreateDevice(16, "PLC-READ-GAP"),
            [
                CreateIoMapping(16, "Read", "D700", 1, sortOrder: 1),
                CreateIoMapping(16, "Read", "D720", 1, sortOrder: 2)
            ],
            new FakeLogService(),
            SignalBlockPlanner,
            runtimePolicy: new PlcIoRuntimePolicy(
                MaxSignalBlockWordCount: 100,
                WriteGapPolicy: PlcIoWriteGapPolicy.Zero));

        await interaction.ExecuteOneCycleAsync(TestContext.Current.CancellationToken);

        var request = Assert.Single(plcService.ReadRequests);
        Assert.Equal("D700", request.Address);
        Assert.Equal((ushort)21, request.Length);
    }

    [Fact]
    public void SignalBlockPlanner_ReadSpanOf100Words_ShouldMergeBut101WordsShouldSplit()
    {
        var withinLimit = SignalBlockPlanner.Plan(
            [
                CreateScanMapping("First", "D100", 1, sortOrder: 1),
                CreateScanMapping("Last", "D199", 1, sortOrder: 2)
            ],
            maxBlockWordCount: 200,
            PlcIoWriteGapPolicy.Split,
            isWrite: false);
        var merged = Assert.Single(withinLimit);
        Assert.Equal("D100", merged.StartAddress);
        Assert.Equal(100, merged.WordCount);

        var overLimit = SignalBlockPlanner.Plan(
            [
                CreateScanMapping("First", "D100", 1, sortOrder: 1),
                CreateScanMapping("PastLimit", "D200", 1, sortOrder: 2)
            ],
            maxBlockWordCount: 200,
            PlcIoWriteGapPolicy.Zero,
            isWrite: false);
        Assert.Equal(["D100", "D200"], overLimit.Select(static block => block.StartAddress));
        Assert.All(overLimit, static block => Assert.Equal(1, block.WordCount));

        var continuous101 = SignalBlockPlanner.Plan(
            [CreateScanMapping("Continuous", "D100", 101, sortOrder: 1)],
            maxBlockWordCount: 200,
            PlcIoWriteGapPolicy.Zero,
            isWrite: false);
        Assert.Collection(
            continuous101,
            block =>
            {
                Assert.Equal("D100", block.StartAddress);
                Assert.Equal(100, block.WordCount);
                var item = Assert.Single(block.Items);
                Assert.Equal(0, item.MappingWordOffset);
                Assert.Equal(100, item.EffectiveWordCount);
            },
            block =>
            {
                Assert.Equal("D200", block.StartAddress);
                Assert.Equal(1, block.WordCount);
                var item = Assert.Single(block.Items);
                Assert.Equal(100, item.MappingWordOffset);
                Assert.Equal(1, item.EffectiveWordCount);
            });
    }

    [Fact]
    public async Task PlcPeriodicBatchReadTask_WhenSingleMappingIs101Words_ShouldSplitAndReassembleAtomically()
    {
        var plcService = new ScriptedPlcService();
        plcService.ConnectOutcomes.Enqueue(true);
        plcService.ReadOutcomes.Enqueue(
            Enumerable.Range(1, 100).Select(static value => (ushort)value).ToArray());
        plcService.ReadOutcomes.Enqueue(new ushort[] { 101 });
        await plcService.ConnectAsync(TestContext.Current.CancellationToken);
        var dataStore = new PlcDataStore();
        dataStore.Register(
            28,
            readSize: 101,
            writeSize: 0,
            [new PlcBufferSignalBinding("Read-D100", "Read", 0, 101)]);
        var task = new PlcPeriodicBatchReadTask(
            plcService,
            dataStore,
            CreateDevice(28, "PLC-CONTINUOUS-101"),
            [CreateIoMapping(
                28,
                "Read",
                "D100",
                101,
                category: IoMappingOptionCatalog.CategoryContinuousRead)],
            new FakeLogService(),
            SignalBlockPlanner,
            runtimePolicy: new PlcIoRuntimePolicy(MaxSignalBlockWordCount: 200));

        await task.ExecuteOneCycleAsync(TestContext.Current.CancellationToken);

        Assert.Equal(
            [("D100", (ushort)100), ("D200", (ushort)1)],
            plcService.ReadRequests);
        var buffer = Assert.IsType<PlcBuffer>(dataStore.GetBuffer(28));
        Assert.True(buffer.TryGetReadWords("Read-D100", out var words));
        Assert.Equal(101, words.Length);
        Assert.Equal((ushort)1, words[0]);
        Assert.Equal((ushort)100, words[99]);
        Assert.Equal((ushort)101, words[100]);
        Assert.True(buffer.TryCaptureReadSnapshot(["Read-D100"], out var snapshot));
        Assert.Equal(101, snapshot!.Signals["Read-D100"].Words.Count);
    }

    [Fact]
    public async Task PlcPeriodicBatchReadTask_WhenLaterFragmentFails_ShouldFailWholeSignalQuality()
    {
        var plcService = new ScriptedPlcService();
        plcService.ConnectOutcomes.Enqueue(true);
        plcService.ReadOutcomes.Enqueue(Enumerable.Repeat((ushort)9, 100).ToArray());
        plcService.ReadOutcomes.Enqueue(new TimeoutException("fragment timeout"));
        await plcService.ConnectAsync(TestContext.Current.CancellationToken);
        var dataStore = new PlcDataStore();
        dataStore.Register(
            29,
            readSize: 101,
            writeSize: 0,
            [new PlcBufferSignalBinding("Read-D100", "Read", 0, 101)]);
        var task = new PlcPeriodicBatchReadTask(
            plcService,
            dataStore,
            CreateDevice(29, "PLC-CONTINUOUS-FAIL"),
            [CreateIoMapping(
                29,
                "Read",
                "D100",
                101,
                category: IoMappingOptionCatalog.CategoryContinuousRead)],
            new FakeLogService(),
            SignalBlockPlanner,
            runtimePolicy: new PlcIoRuntimePolicy(MaxSignalBlockWordCount: 200));

        await task.ExecuteOneCycleAsync(TestContext.Current.CancellationToken);

        var buffer = Assert.IsType<PlcBuffer>(dataStore.GetBuffer(29));
        Assert.False(buffer.TryGetReadWords("Read-D100", out var words));
        Assert.Equal(101, words.Length);
        Assert.All(words, static word => Assert.Equal((ushort)0, word));
        Assert.True(buffer.TryGetReadSignalState("Read-D100", out var state));
        Assert.False(state.ReadSucceeded);
        Assert.Equal(PlcTaskRuntimeErrorCodes.Timeout, state.FailureReason);
    }

    [Fact]
    public async Task PlcPeriodicBatchReadTask_ShouldExcludeBarcodeButKeepQuantityAndSpeedForUi()
    {
        var plcService = new ScriptedPlcService();
        plcService.ConnectOutcomes.Enqueue(true);
        plcService.ReadOutcomes.Enqueue(new ushort[] { 7, 8 });
        await plcService.ConnectAsync(TestContext.Current.CancellationToken);

        var dataStore = new PlcDataStore();
        dataStore.Register(27, readSize: 0, writeSize: 0);
        var task = new PlcPeriodicBatchReadTask(
            plcService,
            dataStore,
            CreateDevice(27, "PLC-PERIODIC-OWNERSHIP"),
            [
                CreateIoMapping(27, "Read", "D300", 1, category: IoMappingOptionCatalog.CategorySingleRead, sortOrder: 1),
                CreateIoMapping(27, "Read", "D320", 1, category: IoMappingOptionCatalog.CategorySingleRead, sortOrder: 2),
                CreateIoMapping(27, "Read", "D321", 1, category: IoMappingOptionCatalog.CategorySingleRead, sortOrder: 3)
            ],
            new FakeLogService(),
            SignalBlockPlanner,
            periodicReadExcludedSignalKeys: new HashSet<string>(
                ["Read-D300"],
                StringComparer.OrdinalIgnoreCase));

        await task.ExecuteOneCycleAsync(TestContext.Current.CancellationToken);

        var request = Assert.Single(plcService.ReadRequests);
        Assert.Equal("D320", request.Address);
        Assert.Equal((ushort)2, request.Length);
        var buffer = Assert.IsType<PlcBuffer>(dataStore.GetBuffer(27));
        Assert.False(buffer.TryGetReadWords("Read-D300", out _));
        Assert.True(buffer.TryGetReadWords("Read-D320", out var quantityWords));
        Assert.Equal((ushort)7, Assert.Single(quantityWords));
        Assert.True(buffer.TryGetReadWords("Read-D321", out var speedWords));
        Assert.Equal((ushort)8, Assert.Single(speedWords));
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
                CreateIoMapping(7, "Read", "D500", 1, sortOrder: 0),
                CreateIoMapping(7, "Write", "D600", 1, sortOrder: 1),
                CreateIoMapping(7, "Write", "D603", 1, sortOrder: 4)
            ],
            new FakeLogService(),
            SignalBlockPlanner,
            runtimePolicy: new PlcIoRuntimePolicy(WriteGapPolicy: PlcIoWriteGapPolicy.Zero));

        await interaction.ExecuteOneCycleAsync(TestContext.Current.CancellationToken);

        var request = Assert.Single(plcService.WriteRequests);
        Assert.Equal("D600", request.Address);
        Assert.Equal([(ushort)1, (ushort)0, (ushort)0, (ushort)4], request.Data);
    }

    [Fact]
    public async Task PlcIoScanTask_WhenWriteGapPolicyIsSplit_ShouldWriteSeparateBlocks()
    {
        var plcService = new ScriptedPlcService();
        plcService.ConnectOutcomes.Enqueue(true);
        plcService.ReadOutcomes.Enqueue(new ushort[] { 1 });

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
                CreateIoMapping(8, "Read", "D500", 1, sortOrder: 0),
                CreateIoMapping(8, "Write", "D600", 1, sortOrder: 1),
                CreateIoMapping(8, "Write", "D603", 1, sortOrder: 4)
            ],
            new FakeLogService(),
            SignalBlockPlanner,
            runtimePolicy: new PlcIoRuntimePolicy(WriteGapPolicy: PlcIoWriteGapPolicy.Split));

        await interaction.ExecuteOneCycleAsync(TestContext.Current.CancellationToken);

        Assert.Equal(["D600", "D603"], plcService.WriteRequests.Select(static x => x.Address));
        Assert.Equal([(ushort)1], plcService.WriteRequests[0].Data);
        Assert.Equal([(ushort)4], plcService.WriteRequests[1].Data);
    }

    [Fact]
    public async Task PlcSignalInteractionTask_WhenWriteMappingIs101Words_ShouldPreserveIntentAcrossTwoBlocks()
    {
        var plcService = new ScriptedPlcService();
        plcService.ConnectOutcomes.Enqueue(true);
        var dataStore = new PlcDataStore();
        dataStore.Register(
            30,
            readSize: 0,
            writeSize: 101,
            [new PlcBufferSignalBinding("Write-D600", "Write", 0, 101)]);
        var buffer = Assert.IsType<PlcBuffer>(dataStore.GetBuffer(30));
        for (var index = 0; index < 101; index++)
        {
            buffer.SetWriteValue("Write-D600", index, checked((ushort)(index + 1)));
        }

        var task = new PlcSignalInteractionTask(
            plcService,
            dataStore,
            CreateDevice(30, "PLC-WRITE-101"),
            [CreateIoMapping(
                30,
                "Write",
                "D600",
                101)],
            new FakeLogService(),
            SignalBlockPlanner,
            runtimePolicy: new PlcIoRuntimePolicy(MaxSignalBlockWordCount: 200));

        await task.ExecuteOneCycleAsync(TestContext.Current.CancellationToken);

        Assert.Collection(
            plcService.WriteRequests,
            request =>
            {
                Assert.Equal("D600", request.Address);
                Assert.Equal(100, request.Data.Length);
                Assert.Equal((ushort)1, request.Data[0]);
                Assert.Equal((ushort)100, request.Data[99]);
            },
            request =>
            {
                Assert.Equal("D700", request.Address);
                Assert.Equal([(ushort)101], request.Data);
            });
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

        await interaction.ExecuteOneCycleAsync(TestContext.Current.CancellationToken);

        var request = Assert.Single(plcService.ReadRequests);
        Assert.Equal("D700", request.Address);
        Assert.Empty(plcService.WriteRequests);
    }

    [Fact]
    public async Task PlcDataReadScanTask_ShouldOnlyScanReadDataCategoriesAndUpdateBufferValues()
    {
        var plcService = new ScriptedPlcService();
        plcService.ConnectOutcomes.Enqueue(true);
        plcService.ReadOutcomes.Enqueue(new ushort[] { 10, 20, 21 });
        await plcService.ConnectAsync(TestContext.Current.CancellationToken);

        var dataStore = new PlcDataStore();
        dataStore.Register(13, readSize: 0, writeSize: 0);

        var dataReadScan = new PlcDataReadScanTask(
            plcService,
            dataStore,
            CreateDevice(13, "PLC-DATA"),
            [
                CreateIoMapping(13, "Read", "D300", 1, category: IoMappingOptionCatalog.CategorySingleRead, sortOrder: 1),
                CreateIoMapping(13, "Read", "D301", 2, category: IoMappingOptionCatalog.CategoryContinuousRead, sortOrder: 2),
                CreateIoMapping(13, "Read", "D700", 1, category: IoMappingOptionCatalog.CategoryInteraction, sortOrder: 3),
                CreateIoMapping(13, "Write", "D200", 1, category: IoMappingOptionCatalog.CategorySingleWrite, sortOrder: 4)
            ],
            new FakeLogService(),
            SignalBlockPlanner);

        await dataReadScan.ExecuteOneCycleAsync(TestContext.Current.CancellationToken);

        var request = Assert.Single(plcService.ReadRequests);
        Assert.Equal("D300", request.Address);
        Assert.Equal((ushort)3, request.Length);
        Assert.Empty(plcService.WriteRequests);

        var buffer = Assert.IsType<PlcBuffer>(dataStore.GetBuffer(13));
        Assert.True(buffer.TryGetReadWords("Read-D300", out var singleRead));
        Assert.Equal((ushort)10, Assert.Single(singleRead));
        Assert.True(buffer.TryGetReadWords("Read-D301", out var continuousRead));
        Assert.Equal([(ushort)20, (ushort)21], continuousRead);
        Assert.False(buffer.TryGetReadWords("Read-D700", out _));
    }

    [Fact]
    public async Task PlcDataReadScanTask_WhenLaterReadBlockFails_ShouldPublishMixedQualityBatch()
    {
        var plcService = new ScriptedPlcService();
        plcService.ConnectOutcomes.Enqueue(true);
        plcService.ReadOutcomes.Enqueue(new ushort[] { 1 });
        plcService.ReadOutcomes.Enqueue(new TimeoutException("read-data second block timeout"));
        await plcService.ConnectAsync(TestContext.Current.CancellationToken);

        var dataStore = new PlcDataStore();
        dataStore.Register(17, readSize: 0, writeSize: 0);
        var buffer = Assert.IsType<PlcBuffer>(dataStore.GetBuffer(17));
        buffer.UpdateReadSignals(
            new Dictionary<string, ushort[]>
            {
                ["Read-D300"] = [(ushort)9],
                ["Read-D320"] = [(ushort)8]
            });
        var statusStore = new PlcConnectionStatusStore();
        statusStore.MarkConnected(
            17,
            "PLC-DATA-SPLIT-FAIL",
            "PLC-DATA-SPLIT-FAIL",
            15);
        var logger = new FakeLogService();

        var dataReadScan = new PlcDataReadScanTask(
            plcService,
            dataStore,
            CreateDevice(17, "PLC-DATA-SPLIT-FAIL"),
            [
                CreateIoMapping(17, "Read", "D300", 1, category: IoMappingOptionCatalog.CategorySingleRead, sortOrder: 1),
                CreateIoMapping(17, "Read", "D320", 1, category: IoMappingOptionCatalog.CategoryContinuousRead, sortOrder: 2)
            ],
            logger,
            SignalBlockPlanner,
            statusStore,
            runtimePolicy: new PlcIoRuntimePolicy(MaxSignalBlockWordCount: 10));

        await dataReadScan.ExecuteOneCycleAsync(TestContext.Current.CancellationToken);

        var snapshot = statusStore.GetSnapshot(17);
        Assert.NotNull(snapshot);
        Assert.True(snapshot!.IsConnected);
        Assert.Equal(PlcConnectionState.Connected, snapshot.ConnectionState);
        Assert.Equal(0, plcService.DisconnectCallCount);
        Assert.Equal(["D300", "D320"], plcService.ReadRequests.Select(static x => x.Address));

        Assert.True(buffer.TryGetReadWords("Read-D300", out var successfulWords));
        Assert.Equal((ushort)1, Assert.Single(successfulWords));
        Assert.False(buffer.TryGetReadWords("Read-D320", out var failedWords));
        Assert.Equal((ushort)0, Assert.Single(failedWords));
        Assert.True(buffer.TryGetReadSignalState("Read-D300", out var successfulState));
        Assert.True(buffer.TryGetReadSignalState("Read-D320", out var failedState));
        Assert.True(successfulState.ReadSucceeded);
        Assert.False(failedState.ReadSucceeded);
        Assert.Equal(successfulState.BatchId, failedState.BatchId);
        Assert.Equal((ushort)8, Assert.Single(failedState.LastSucceededWords));
        Assert.NotNull(failedState.FailedAtUtc);
        Assert.Contains(
            logger.Entries,
            entry => entry.Message.Contains("地址=D320", StringComparison.Ordinal)
                     && entry.Message.Contains("Read-D320@D320", StringComparison.Ordinal)
                     && entry.Message.Contains("默认值与失败质量", StringComparison.Ordinal));
    }

    [Fact]
    public async Task PlcDataReadScanTask_WhenFirstBlockFails_ShouldContinueUnrelatedBlockWithoutSingleSignalRetry()
    {
        var plcService = new ScriptedPlcService();
        plcService.ConnectOutcomes.Enqueue(true);
        plcService.ReadOutcomes.Enqueue(new TimeoutException("first block timeout"));
        plcService.ReadOutcomes.Enqueue(new ushort[] { 5 });
        await plcService.ConnectAsync(TestContext.Current.CancellationToken);

        var dataStore = new PlcDataStore();
        dataStore.Register(22, readSize: 0, writeSize: 0);
        var statusStore = new PlcConnectionStatusStore();
        statusStore.MarkConnected(
            22,
            "PLC-DATA-FIRST-FAIL",
            "PLC-DATA-FIRST-FAIL",
            10);

        var dataReadScan = new PlcDataReadScanTask(
            plcService,
            dataStore,
            CreateDevice(22, "PLC-DATA-FIRST-FAIL"),
            [
                CreateIoMapping(22, "Read", "D300", 1, category: IoMappingOptionCatalog.CategorySingleRead, sortOrder: 1),
                CreateIoMapping(22, "Read", "D320", 1, category: IoMappingOptionCatalog.CategoryContinuousRead, sortOrder: 2)
            ],
            new FakeLogService(),
            SignalBlockPlanner,
            statusStore,
            runtimePolicy: new PlcIoRuntimePolicy(MaxSignalBlockWordCount: 10));

        await dataReadScan.ExecuteOneCycleAsync(TestContext.Current.CancellationToken);

        Assert.Equal(["D300", "D320"], plcService.ReadRequests.Select(static request => request.Address));
        Assert.Equal(1, plcService.ReadRequests.Count(static request => request.Address == "D300"));
        Assert.True(statusStore.GetSnapshot(22)?.IsConnected);

        var buffer = Assert.IsType<PlcBuffer>(dataStore.GetBuffer(22));
        Assert.False(buffer.TryGetReadWords("Read-D300", out var failedWords));
        Assert.Equal((ushort)0, Assert.Single(failedWords));
        Assert.True(buffer.TryGetReadWords("Read-D320", out var successfulWords));
        Assert.Equal((ushort)5, Assert.Single(successfulWords));
    }

    [Fact]
    public async Task PlcDataReadScanTask_WhenDisconnected_ShouldNotOpenSecondConnection()
    {
        var plcService = new ScriptedPlcService();
        var dataStore = new PlcDataStore();
        dataStore.Register(14, readSize: 0, writeSize: 0);

        var dataReadScan = new PlcDataReadScanTask(
            plcService,
            dataStore,
            CreateDevice(14, "PLC-DATA-DISCONNECTED"),
            [CreateIoMapping(14, "Read", "D300", 1, category: IoMappingOptionCatalog.CategorySingleRead)],
            new FakeLogService(),
            SignalBlockPlanner);

        await dataReadScan.ExecuteOneCycleAsync(TestContext.Current.CancellationToken);

        Assert.False(plcService.IsConnected);
        Assert.Equal(0, plcService.ConnectAsyncCallCount);
        Assert.Equal(0, plcService.ReadAsyncCallCount);
        Assert.Empty(plcService.WriteRequests);
    }

    [Fact]
    public async Task PlcDataReadScanTask_WhenStatusNotConnected_ShouldRecordReadWithoutPromotingConnection()
    {
        var plcService = new ScriptedPlcService();
        plcService.ConnectOutcomes.Enqueue(true);
        plcService.ReadOutcomes.Enqueue(new ushort[] { 11 });
        await plcService.ConnectAsync(TestContext.Current.CancellationToken);

        var dataStore = new PlcDataStore();
        dataStore.Register(19, readSize: 0, writeSize: 0);
        var statusStore = new PlcConnectionStatusStore();
        statusStore.MarkConnecting(
            19,
            "PLC-DATA-READONLY",
            "PLC-DATA-READONLY");

        var dataReadScan = new PlcDataReadScanTask(
            plcService,
            dataStore,
            CreateDevice(19, "PLC-DATA-READONLY"),
            [CreateIoMapping(19, "Read", "D300", 1, category: IoMappingOptionCatalog.CategorySingleRead)],
            new FakeLogService(),
            SignalBlockPlanner,
            statusStore);

        await dataReadScan.ExecuteOneCycleAsync(TestContext.Current.CancellationToken);

        Assert.True(plcService.IsConnected);
        Assert.Equal(1, plcService.ReadAsyncCallCount);
        var firstSnapshot = statusStore.GetSnapshot(19);
        Assert.NotNull(firstSnapshot);
        Assert.False(firstSnapshot!.IsConnected);
        Assert.Equal(PlcConnectionState.Connecting, firstSnapshot.ConnectionState);
        Assert.NotNull(firstSnapshot.LastAttemptAtUtc);
        Assert.NotNull(firstSnapshot.LastReadAtUtc);

        Assert.Equal(1, plcService.ReadAsyncCallCount);
        Assert.False(firstSnapshot.IsConnected);
        Assert.Equal(PlcConnectionState.Connecting, firstSnapshot.ConnectionState);
    }

    [Fact]
    public async Task PlcDataReadScanTask_WhenInteractionMappingExists_ShouldNotUseProtocolReadAsConnectionGate()
    {
        var plcService = new ScriptedPlcService();
        plcService.ConnectOutcomes.Enqueue(true);
        plcService.ReadOutcomes.Enqueue(new ushort[] { 11 });
        await plcService.ConnectAsync(TestContext.Current.CancellationToken);

        var dataStore = new PlcDataStore();
        dataStore.Register(20, readSize: 0, writeSize: 0);
        var statusStore = new PlcConnectionStatusStore();
        statusStore.MarkConnecting(
            20,
            "PLC-DATA-GATED",
            "PLC-DATA-GATED");

        var dataReadScan = new PlcDataReadScanTask(
            plcService,
            dataStore,
            CreateDevice(20, "PLC-DATA-GATED"),
            [
                CreateIoMapping(20, "Read", "D300", 1, category: IoMappingOptionCatalog.CategorySingleRead),
                CreateIoMapping(20, "Read", "D700", 1, category: IoMappingOptionCatalog.CategoryInteraction)
            ],
            new FakeLogService(),
            SignalBlockPlanner,
            statusStore);

        await dataReadScan.ExecuteOneCycleAsync(TestContext.Current.CancellationToken);

        Assert.True(plcService.IsConnected);
        Assert.Equal(1, plcService.ReadAsyncCallCount);
        var buffer = Assert.IsType<PlcBuffer>(dataStore.GetBuffer(20));
        Assert.True(buffer.TryGetReadWords("Read-D300", out var words));
        Assert.Equal((ushort)11, Assert.Single(words));
        Assert.False(statusStore.GetSnapshot(20)?.IsConnected);
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

        Assert.Equal(readCountAfterStop, plcService.ReadAsyncCallCount);
    }

    [Fact]
    public async Task PlcIoScanTask_StartAsync_WhenServiceQuarantines_ShouldPropagateToRuntimeOwner()
    {
        var quarantine = new PlcServiceQuarantinedException(
            nameof(ScriptedPlcService),
            nameof(ScriptedPlcService.ReadDataAsync),
            "sensitive quarantine detail");
        var plcService = new ScriptedPlcService();
        plcService.ConnectOutcomes.Enqueue(true);
        plcService.ReadOutcomes.Enqueue(quarantine);
        var dataStore = new PlcDataStore();
        dataStore.Register(23, readSize: 1, writeSize: 0);
        var logger = new FakeLogService();
        var statusStore = new PlcConnectionStatusStore();
        var connectionStates = new List<bool>();
        var interaction = new PlcIoScanTask(
            plcService,
            dataStore,
            CreateDevice(23, "PLC-QUARANTINED"),
            [CreateIoMapping(23, "Read", "D300", 1)],
            logger,
            SignalBlockPlanner,
            statusStore,
            connectionStateChanged: connectionStates.Add);

        var actual = await Assert.ThrowsAsync<PlcServiceQuarantinedException>(
            () => interaction.StartAsync(TestContext.Current.CancellationToken));

        Assert.Same(quarantine, actual);
        Assert.Equal([false, true, false], connectionStates);
        Assert.Equal(
            PlcServiceQuarantinedException.StableReasonCode,
            statusStore.GetSnapshot(23)?.LastError);
        Assert.Contains(
            logger.Entries,
            entry => entry.Message.Contains(
                PlcServiceQuarantinedException.StableReasonCode,
                StringComparison.Ordinal)
                && entry.Message.Contains(
                    nameof(PlcServiceQuarantinedException),
                    StringComparison.Ordinal));
        Assert.DoesNotContain(
            logger.Entries,
            entry => entry.Message.Contains(
                "sensitive quarantine detail",
                StringComparison.Ordinal));
    }

    [Fact]
    public async Task PlcIoScanTask_StartAsync_WhenProtocolCancelsWithoutRuntimeCancellation_ShouldEnterErrorPath()
    {
        var plcService = new ScriptedPlcService();
        plcService.ReadOutcomes.Enqueue(
            new OperationCanceledException("protocol canceled independently"));
        await plcService.ConnectAsync(TestContext.Current.CancellationToken);
        var dataStore = new PlcDataStore();
        dataStore.Register(21, readSize: 1, writeSize: 0);
        var logger = new FakeLogService();
        var interaction = new PlcIoScanTask(
            plcService,
            dataStore,
            CreateDevice(21, "PLC-INDEPENDENT-CANCEL"),
            [CreateIoMapping(21, "Read", "D300", 1)],
            logger,
            SignalBlockPlanner);
        using var cts = new CancellationTokenSource();

        var runTask = interaction.StartAsync(cts.Token);
        await WaitUntilAsync(() => logger.Entries.Any(entry =>
            entry.Message.Contains("PLC 读取 block 失败", StringComparison.Ordinal)
            && entry.Message.Contains("原因码=TaskFault", StringComparison.Ordinal)
            && entry.Message.Contains("异常类型=OperationCanceledException", StringComparison.Ordinal)));
        Assert.DoesNotContain(
            logger.Entries,
            entry => entry.Message.Contains("protocol canceled independently", StringComparison.Ordinal));

        Assert.False(cts.IsCancellationRequested);
        Assert.False(runTask.IsCompleted);

        await StopInteractionAsync(runTask, cts);
    }

    [Fact]
    public async Task PlcDataReadScanTask_StartAsync_WhenResolverCancelsWithoutRuntimeCancellation_ShouldEnterErrorPath()
    {
        var logger = new FakeLogService();
        var dataReadScan = new PlcDataReadScanTask(
            new ScriptedPlcService(),
            new PlcDataStore(),
            CreateDevice(22, "PLC-RESOLVER-CANCEL"),
            [],
            logger,
            SignalBlockPlanner,
            dataReadLoopIntervalResolver: _ => Task.FromException<int>(
                new OperationCanceledException("resolver canceled independently")));
        using var cts = new CancellationTokenSource();

        var runTask = dataReadScan.StartAsync(cts.Token);
        await WaitUntilAsync(() => logger.Entries.Any(entry =>
            entry.Message.Contains("PLC 只读数据扫描异常", StringComparison.Ordinal)));

        Assert.False(cts.IsCancellationRequested);
        Assert.False(runTask.IsCompleted);

        await StopInteractionAsync(runTask, cts);
    }

    [Fact]
    public void PlcScanTaskApi_ShouldExposeOnlyCancellationAwareCycleEntryPoint()
    {
        foreach (var taskType in new[] { typeof(PlcIoScanTaskBase), typeof(PlcPeriodicBatchReadTask) })
        {
            var cycleMethod = Assert.Single(
                taskType.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly),
                static method => method.Name == "ExecuteOneCycleAsync");
            var parameter = Assert.Single(cycleMethod.GetParameters());
            Assert.Equal(typeof(CancellationToken), parameter.ParameterType);
        }
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

    private static PlcIoScanMapping CreateScanMapping(
        string signalKey,
        string address,
        int addressCount,
        int sortOrder)
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
        static async Task ObserveAsync(Func<bool> observation, CancellationToken cancellationToken)
        {
            while (!observation())
            {
                cancellationToken.ThrowIfCancellationRequested();
                await Task.Yield();
            }
        }

        await ObserveAsync(condition, TestContext.Current.CancellationToken)
            .WaitAsync(TimeSpan.FromMilliseconds(1500), TestContext.Current.CancellationToken);
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

    private sealed class BlockingPlcService : PlcServiceTestDouble
    {
        public TaskCompletionSource ReadEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public override bool IsConnected => true;

        public override async Task<List<T>> ReadDataAsync<T>(
            string address,
            ushort length,
            CancellationToken cancellationToken = default)
        {
            ReadEntered.TrySetResult();
            var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            await completion.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
            return [];
        }
    }

    private sealed class FixedBufferDataStore(IPlcBufferTransport buffer) : IPlcDataStore
    {
        public void Register(int networkDeviceId, int readSize, int writeSize)
        {
        }

        public void Register(
            int networkDeviceId,
            int readSize,
            int writeSize,
            IReadOnlyCollection<PlcBufferSignalBinding> signalBindings)
        {
        }

        public IPlcBufferTransport? GetBuffer(int networkDeviceId)
            => buffer;

        public bool HasDevice(int networkDeviceId)
            => true;
    }

    private sealed class LegacyTransportBuffer : IPlcBufferTransport
    {
        public int UpdateReadSignalCallCount { get; private set; }

        public event EventHandler<PlcSignalBufferChangedEventArgs>? SignalValuesChanged
        {
            add
            {
            }
            remove
            {
            }
        }

        public ushort GetReadValue(int index)
            => 0;

        public bool TryGetReadWords(string signalKey, out ushort[] values)
        {
            values = [];
            return false;
        }

        public bool TryGetWriteWords(string signalKey, out ushort[] values)
        {
            values = [];
            return false;
        }

        public void SetWriteValue(int index, ushort value)
        {
        }

        public void SetWriteValue(string signalKey, int offset, ushort value)
        {
        }

        public void UpdateReadBuffer(ushort[] data)
        {
        }

        public void UpdateReadSignal(string signalKey, IReadOnlyList<ushort> data)
            => UpdateReadSignalCallCount++;

        public ushort[] GetWriteBuffer()
            => [];

        public void SetSignalBindings(IReadOnlyCollection<PlcBufferSignalBinding> bindings)
        {
        }
    }

    private sealed class IndependentlyCancelingIoScanTask : PlcIoScanTaskBase
    {
        public IndependentlyCancelingIoScanTask(
            IPlcService plcService,
            IPlcDataStore dataStore,
            ILogService logger)
            : base(
                plcService,
                dataStore,
                new PlcIoScanDevice(
                    21,
                    "PLC-INDEPENDENT-CANCEL",
                    new TcpPlcEndpoint("127.0.0.1", 102, 3000))
                {
                    PlcCode = "PLC-INDEPENDENT-CANCEL"
                },
                [
                    new PlcIoScanMapping(
                        "Read-D300",
                        "D300",
                        1,
                        "UInt16",
                        "Read",
                        IoMappingOptionCatalog.CategoryInteraction,
                        1)
                ],
                logger,
                SignalBlockPlanner)
        {
        }

        protected override bool IsStableOnline()
            => throw new OperationCanceledException("protocol canceled independently");
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

    private sealed class ScriptedPlcService : PlcServiceTestDouble
    {
        public Queue<object?> ConnectOutcomes { get; } = new();
        public Queue<object?> ReadOutcomes { get; } = new();
        public Queue<object?> WriteOutcomes { get; } = new();
        public List<(string Address, ushort Length)> ReadRequests { get; } = [];
        public List<(string Address, ushort[] Data)> WriteRequests { get; } = [];
        public bool DropConnectionOnReadFailure { get; init; }

        public override bool IsConnected { get; protected set; }
        public PlcEndpoint? Endpoint { get; private set; }
        public int ConnectAsyncCallCount { get; private set; }
        public int DisconnectCallCount { get; private set; }
        public int ReadAsyncCallCount { get; private set; }
        public int WriteAsyncCallCount { get; private set; }

        public override void Init(PlcEndpoint endpoint)
        {
            Endpoint = endpoint;
        }

        public override Task<bool> ConnectAsync(CancellationToken cancellationToken = default)
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

        public override Task DisconnectAsync(CancellationToken cancellationToken = default)
        {
            DisconnectCallCount++;
            IsConnected = false;
            return Task.CompletedTask;
        }

        public override Task<List<T>> ReadDataAsync<T>(
            string address,
            ushort length,
            CancellationToken cancellationToken = default)
        {
            ReadAsyncCallCount++;
            ReadRequests.Add((address, length));

            if (ReadOutcomes.Count > 0)
            {
                var outcome = ReadOutcomes.Dequeue();
                if (outcome is Exception ex)
                {
                    if (DropConnectionOnReadFailure)
                    {
                        IsConnected = false;
                    }

                    throw ex;
                }

                if (outcome is ushort[] values && typeof(T) == typeof(ushort))
                {
                    return Task.FromResult(values.Select(x => (T)(object)x).ToList());
                }
            }

            return Task.FromResult(Enumerable.Repeat((T)(object)(ushort)1, length).ToList());
        }

        public override Task WriteDataAsync<T>(
            string address,
            List<T> data,
            CancellationToken cancellationToken = default)
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
    }

    private sealed class NeverCompletingConnectPlcService(TimeProvider timeProvider) : PlcServiceTestDouble
    {
        public override bool IsConnected => false;

        public PlcEndpoint? Endpoint { get; private set; }

        public int ConnectAsyncCallCount { get; private set; }

        public int DisconnectCallCount { get; private set; }

        public override void Init(PlcEndpoint endpoint)
        {
            Endpoint = endpoint;
        }

        public override async Task<bool> ConnectAsync(CancellationToken cancellationToken = default)
        {
            ConnectAsyncCallCount++;
            var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            return await completion.Task
                .WaitAsync(Endpoint!.ConnectTimeout, timeProvider, cancellationToken)
                .ConfigureAwait(false);
        }

        public override Task DisconnectAsync(CancellationToken cancellationToken = default)
        {
            DisconnectCallCount++;
            return Task.CompletedTask;
        }
    }
}
