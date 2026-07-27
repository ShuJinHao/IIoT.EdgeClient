using IIoT.Edge.Infrastructure.DeviceComm.Plc.Store;

namespace IIoT.Edge.Plc.ContractTests;

public sealed class PlcBufferConcurrencyTests
{
    [Fact]
    public void GetWriteBuffer_ShouldReturnSnapshotInsteadOfLiveMutableArray()
    {
        var buffer = new PlcBuffer(readSize: 4, writeSize: 2);

        buffer.SetWriteValue(0, 1);
        var snapshot1 = buffer.GetWriteBuffer();

        buffer.SetWriteValue(0, 2);
        var snapshot2 = buffer.GetWriteBuffer();

        Assert.Equal((ushort)1, snapshot1[0]);
        Assert.Equal((ushort)2, snapshot2[0]);
    }

    [Fact]
    public async Task ConcurrentReadUpdates_ShouldNotThrowAndShouldKeepLatestLength()
    {
        var buffer = new PlcBuffer(readSize: 16, writeSize: 2);

        var writer = Task.Run(() =>
        {
            for (var i = 0; i < 2000; i++)
            {
                buffer.UpdateReadBuffer(Enumerable.Repeat((ushort)(i % 10), 16).ToArray());
            }
        }, TestContext.Current.CancellationToken);

        var reader = Task.Run(() =>
        {
            for (var i = 0; i < 2000; i++)
            {
                for (var j = 0; j < 16; j++)
                {
                    _ = buffer.GetReadValue(j);
                }
            }
        }, TestContext.Current.CancellationToken);

        await Task.WhenAll(writer, reader);

        Assert.True(buffer.Matches(16, 2));
    }

    [Fact]
    public void UpdateReadSignals_ShouldPublishCompleteBatchBeforeNotifications()
    {
        var buffer = new PlcBuffer(
            readSize: 2,
            writeSize: 0,
            [
                new("Signal.A", "Read", 0, 1),
                new("Signal.B", "Read", 1, 1)
            ]);
        var observed = new List<(ushort A, ushort B)>();
        buffer.SignalValuesChanged += (_, _) =>
        {
            Assert.True(buffer.TryGetReadWords("Signal.A", out var a));
            Assert.True(buffer.TryGetReadWords("Signal.B", out var b));
            observed.Add((Assert.Single(a), Assert.Single(b)));
        };

        buffer.UpdateReadSignals(new Dictionary<string, ushort[]>
        {
            ["Signal.A"] = [(ushort)11],
            ["Signal.B"] = [(ushort)22]
        });

        Assert.Equal(2, observed.Count);
        Assert.All(observed, snapshot => Assert.Equal(((ushort)11, (ushort)22), snapshot));
        Assert.Equal((ushort)11, buffer.GetReadValue(0));
        Assert.Equal((ushort)22, buffer.GetReadValue(1));
    }

    [Fact]
    public void PublishReadBatch_WhenSignalFails_ShouldExposeDefaultWithFailureQualityAndKeepDiagnosticSuccess()
    {
        var buffer = new PlcBuffer(
            readSize: 2,
            writeSize: 0,
            [
                new("Signal.A", "Read", 0, 1),
                new("Signal.B", "Read", 1, 1)
            ]);
        buffer.UpdateReadSignals(new Dictionary<string, ushort[]>
        {
            ["Signal.A"] = [(ushort)11],
            ["Signal.B"] = [(ushort)22]
        });

        var batchId = Guid.NewGuid();
        var attemptedAtUtc = DateTimeOffset.UtcNow;
        buffer.PublishReadBatch(
            new Dictionary<string, PlcReadSignalUpdate>
            {
                ["Signal.A"] = new(
                    [(ushort)33],
                    ReadSucceeded: true,
                    batchId,
                    attemptedAtUtc,
                    FailureReason: null),
                ["Signal.B"] = new(
                    [(ushort)0],
                    ReadSucceeded: false,
                    batchId,
                    attemptedAtUtc,
                    FailureReason: "read timeout")
            });

        Assert.True(buffer.TryGetReadWords("Signal.A", out var successfulWords));
        Assert.Equal((ushort)33, Assert.Single(successfulWords));
        Assert.False(buffer.TryGetReadWords("Signal.B", out var failedWords));
        Assert.Equal((ushort)0, Assert.Single(failedWords));
        Assert.Equal((ushort)0, buffer.GetReadValue(1));

        Assert.True(buffer.TryGetReadSignalState("Signal.A", out var successfulState));
        Assert.True(buffer.TryGetReadSignalState("Signal.B", out var failedState));
        Assert.Equal(batchId, successfulState.BatchId);
        Assert.Equal(batchId, failedState.BatchId);
        Assert.True(successfulState.ReadSucceeded);
        Assert.False(failedState.ReadSucceeded);
        Assert.Equal((ushort)22, Assert.Single(failedState.LastSucceededWords));
        Assert.NotNull(failedState.LastSucceededAtUtc);
        Assert.Equal(attemptedAtUtc, failedState.FailedAtUtc);
        Assert.Equal("read timeout", failedState.FailureReason);
    }

    [Fact]
    public void TryGetReadWords_WhenSignalHasNeverBeenRead_ShouldReturnFailureQualityAndDefaultValue()
    {
        var buffer = new PlcBuffer(
            readSize: 1,
            writeSize: 0,
            [new("Signal.A", "Read", 0, 1)]);

        Assert.False(buffer.TryGetReadWords("Signal.A", out var words));
        Assert.Equal((ushort)0, Assert.Single(words));
    }

    [Fact]
    public void PlcDataStore_RegisterWithDifferentSize_ShouldReplaceBuffer()
    {
        var store = new PlcDataStore();

        store.Register(1, readSize: 2, writeSize: 2);
        var original = store.GetBuffer(1);

        store.Register(1, readSize: 4, writeSize: 4);
        var replaced = store.GetBuffer(1);

        Assert.NotNull(original);
        Assert.NotNull(replaced);
        Assert.NotSame(original, replaced);
    }
}
