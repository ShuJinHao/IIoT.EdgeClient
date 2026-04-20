using IIoT.Edge.Application.Abstractions.Plc;
using IIoT.Edge.Domain.Hardware.Aggregates;
using IIoT.Edge.Infrastructure.DeviceComm.Barcode.Readers;
using IIoT.Edge.Infrastructure.DeviceComm.Plc.Store;
using IIoT.Edge.Infrastructure.DeviceComm.Signals;
using IIoT.Edge.SharedKernel.Enums;
using System.Diagnostics;

namespace IIoT.Edge.NonUiRegressionTests;

public sealed class SignalInteractionBehaviorTests
{
    [Fact]
    public async Task PlcBarcodeReader_WhenCancellationRequested_ShouldPropagateCancellation()
    {
        var plcService = new BlockingPlcService();
        var reader = new PlcBarcodeReader(plcService, "D100", codeCount: 1, wordsPerCode: 1);
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => reader.ReadAsync(cts.Token));
    }

    [Fact]
    public async Task SignalInteraction_ConnectAsync_WhenConnectTimesOut_ShouldLogAndStayDisconnected()
    {
        var plcService = new ScriptedPlcService();
        plcService.ConnectOutcomes.Enqueue(new TimeoutException("connect timeout"));

        var logger = new FakeLogService();
        var interaction = new SignalInteraction(
            plcService,
            new PlcDataStore(),
            CreateDevice(1, "PLC-A"),
            [],
            logger);

        await interaction.ConnectAsync();

        Assert.False(plcService.IsConnected);
        Assert.Equal(1, plcService.ConnectAsyncCallCount);
        Assert.Contains(logger.Entries, x => x.Message.Contains("Connect exception", StringComparison.Ordinal));
    }

    [Fact]
    public async Task SignalInteraction_WhenReadTimesOut_ShouldDisconnectAndReconnectBeforeRecovering()
    {
        var plcService = new ScriptedPlcService();
        plcService.ConnectOutcomes.Enqueue(true);
        plcService.ConnectOutcomes.Enqueue(true);
        plcService.ReadOutcomes.Enqueue(new TimeoutException("read timeout"));
        plcService.ReadOutcomes.Enqueue(new ushort[] { 7 });

        var dataStore = new PlcDataStore();
        dataStore.Register(1, readSize: 1, writeSize: 0);

        var interaction = new SignalInteraction(
            plcService,
            dataStore,
            CreateDevice(1, "PLC-A"),
            [CreateIoMapping(1, "Read", "D100", 1)],
            new FakeLogService());

        var buffer = Assert.IsType<PlcBuffer>(dataStore.GetBuffer(1));
        await interaction.ConnectAsync();
        await Assert.ThrowsAsync<Exception>(() => interaction.ExecuteOneCycleAsync());
        Assert.Equal(1, plcService.DisconnectCallCount);
        Assert.False(plcService.IsConnected);

        await interaction.ConnectAsync();
        await interaction.ExecuteOneCycleAsync();

        Assert.True(plcService.ConnectAsyncCallCount >= 2);
        Assert.True(plcService.ReadAsyncCallCount >= 2);
        Assert.Equal((ushort)7, buffer.GetReadValue(0));
    }

    [Fact]
    public async Task SignalInteraction_WhenWriteTimesOut_ShouldDisconnectAndReconnectBeforeRecovering()
    {
        var plcService = new ScriptedPlcService();
        plcService.ConnectOutcomes.Enqueue(true);
        plcService.ConnectOutcomes.Enqueue(true);
        plcService.WriteOutcomes.Enqueue(new TimeoutException("write timeout"));
        plcService.WriteOutcomes.Enqueue(null);

        var dataStore = new PlcDataStore();
        dataStore.Register(2, readSize: 0, writeSize: 1);

        var buffer = Assert.IsType<PlcBuffer>(dataStore.GetBuffer(2));
        buffer.SetWriteValue(0, 9);

        var interaction = new SignalInteraction(
            plcService,
            dataStore,
            CreateDevice(2, "PLC-B"),
            [CreateIoMapping(2, "Write", "D200", 1)],
            new FakeLogService());

        await interaction.ConnectAsync();
        await Assert.ThrowsAsync<Exception>(() => interaction.ExecuteOneCycleAsync());
        Assert.Equal(1, plcService.DisconnectCallCount);
        Assert.False(plcService.IsConnected);

        await interaction.ConnectAsync();
        await interaction.ExecuteOneCycleAsync();

        Assert.True(plcService.ConnectAsyncCallCount >= 2);
        Assert.True(plcService.WriteAsyncCallCount >= 2);
    }

    [Fact]
    public async Task SignalInteraction_StartAsync_WhenCanceled_ShouldStopPolling()
    {
        var plcService = new ScriptedPlcService();
        plcService.ConnectOutcomes.Enqueue(true);

        var dataStore = new PlcDataStore();
        dataStore.Register(3, readSize: 1, writeSize: 0);

        var interaction = new SignalInteraction(
            plcService,
            dataStore,
            CreateDevice(3, "PLC-C"),
            [CreateIoMapping(3, "Read", "D300", 1)],
            new FakeLogService());

        using var cts = new CancellationTokenSource();
        var runTask = interaction.StartAsync(cts.Token);

        await WaitUntilAsync(() => plcService.ReadAsyncCallCount >= 2);
        var readCountBeforeCancel = plcService.ReadAsyncCallCount;

        await StopInteractionAsync(runTask, cts);
        await Task.Delay(80);

        Assert.Equal(readCountBeforeCancel, plcService.ReadAsyncCallCount);
    }

    private static NetworkDeviceEntity CreateDevice(int id, string deviceName)
        => new(deviceName, DeviceType.PLC, "127.0.0.1", 102)
        {
            Id = id
        };

    private static IoMappingEntity CreateIoMapping(int deviceId, string direction, string address, int addressCount)
        => new(deviceId, $"{direction}-{address}", address, addressCount, "UInt16", direction)
        {
            SortOrder = 1
        };

    private static async Task WaitUntilAsync(Func<bool> condition, int timeoutMs = 1500)
    {
        var stopwatch = Stopwatch.StartNew();
        while (stopwatch.ElapsedMilliseconds < timeoutMs)
        {
            if (condition())
            {
                return;
            }

            await Task.Delay(20);
        }

        throw new TimeoutException("Condition was not met within the test timeout.");
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

        public void Init(string ip, int port)
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

    private sealed class ScriptedPlcService : IPlcService
    {
        public Queue<object?> ConnectOutcomes { get; } = new();
        public Queue<object?> ReadOutcomes { get; } = new();
        public Queue<object?> WriteOutcomes { get; } = new();

        public bool IsConnected { get; private set; }
        public int ConnectAsyncCallCount { get; private set; }
        public int DisconnectCallCount { get; private set; }
        public int ReadAsyncCallCount { get; private set; }
        public int WriteAsyncCallCount { get; private set; }

        public void Init(string ip, int port)
        {
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
}
