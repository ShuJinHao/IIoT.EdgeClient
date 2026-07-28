using IIoT.Edge.Infrastructure.DeviceComm.Plc.Store;
using IIoT.Edge.Module.Contracts.Plc.Store;

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
                    [(ushort)999],
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

        Assert.True(
            buffer.TryCaptureReadSnapshot(
                ["Signal.A", "Signal.B"],
                out var businessSnapshot));
        Assert.NotNull(businessSnapshot);
        Assert.Equal(batchId, businessSnapshot!.BatchId);
        Assert.True(businessSnapshot.TryGetSignal("Signal.A", out var successfulSignal));
        Assert.True(businessSnapshot.TryGetSignal("Signal.B", out var failedSignal));
        Assert.True(successfulSignal.ReadSucceeded);
        Assert.False(failedSignal.ReadSucceeded);
        Assert.Equal((ushort)0, Assert.Single(failedSignal.Words));
        Assert.Equal("read timeout", failedSignal.FailureReason);
        Assert.Null(typeof(PlcReadSignalSnapshot).GetProperty("LastSucceededWords"));
    }

    [Fact]
    public void TryCaptureReadSnapshot_ShouldRequireCompleteGenerationAndIgnoreManualDisplayUpdates()
    {
        var buffer = new PlcBuffer(
            readSize: 3,
            writeSize: 0,
            [
                new("Signal.A", "Read", 0, 1),
                new("Signal.B", "Read", 1, 1),
                new("Signal.C", "Read", 2, 1)
            ]);
        buffer.UpdateReadSignals(new Dictionary<string, ushort[]>
        {
            ["Signal.A"] = [(ushort)11],
            ["Signal.B"] = [(ushort)22]
        });

        Assert.True(
            buffer.TryCaptureReadSnapshot(
                ["Signal.A", "Signal.B"],
                out var firstSnapshot));
        Assert.NotNull(firstSnapshot);
        Assert.All(
            firstSnapshot!.Signals.Values,
            signal =>
            {
                Assert.Equal(firstSnapshot.Generation, signal.Generation);
                Assert.Equal(firstSnapshot.BatchId, signal.BatchId);
                Assert.Equal(firstSnapshot.CapturedAtUtc, signal.CapturedAtUtc);
            });
        Assert.False(buffer.TryCaptureReadSnapshot(["Signal.A", "Signal.C"], out _));
        Assert.False(buffer.TryCaptureReadSnapshot(["Signal.A", "signal.a"], out _));

        buffer.UpdateReadSignal("Signal.A", [(ushort)33]);
        buffer.UpdateReadSignal("Signal.B", [(ushort)44]);

        Assert.True(buffer.TryGetReadWords("Signal.A", out var manualWords));
        Assert.Equal((ushort)33, Assert.Single(manualWords));
        Assert.True(buffer.TryGetReadWords("Signal.B", out var secondManualWords));
        Assert.Equal((ushort)44, Assert.Single(secondManualWords));
        Assert.Equal((ushort)33, buffer.GetReadValue(0));
        Assert.Equal((ushort)44, buffer.GetReadValue(1));
        Assert.True(
            buffer.TryCaptureReadSnapshot(
                ["Signal.A", "Signal.B"],
                out var snapshotAfterManualRead));
        Assert.NotNull(snapshotAfterManualRead);
        Assert.Equal(firstSnapshot.Generation, snapshotAfterManualRead!.Generation);
        Assert.Equal(firstSnapshot.BatchId, snapshotAfterManualRead.BatchId);
        Assert.Equal((ushort)11, Assert.Single(snapshotAfterManualRead.Signals["Signal.A"].Words));
        Assert.Equal((ushort)22, Assert.Single(snapshotAfterManualRead.Signals["Signal.B"].Words));
        Assert.Equal((ushort)11, Assert.Single(firstSnapshot.Signals["Signal.A"].Words));

        buffer.UpdateReadSignals(new Dictionary<string, ushort[]>
        {
            ["Signal.A"] = [(ushort)44],
            ["Signal.B"] = [(ushort)55]
        });

        Assert.True(buffer.TryCaptureReadSnapshot(["Signal.A", "Signal.B"], out var nextSnapshot));
        Assert.NotNull(nextSnapshot);
        Assert.True(nextSnapshot!.Generation > firstSnapshot.Generation);
        Assert.Equal((ushort)44, Assert.Single(nextSnapshot.Signals["Signal.A"].Words));
        Assert.True(buffer.TryGetReadWords("Signal.A", out var refreshedWords));
        Assert.Equal((ushort)44, Assert.Single(refreshedWords));
    }

    [Fact]
    public async Task TryCaptureReadSnapshot_DuringConcurrentPublishing_ShouldNeverMixGenerations()
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
            ["Signal.A"] = [(ushort)0],
            ["Signal.B"] = [ushort.MaxValue]
        });

        var writer = Task.Run(
            () =>
            {
                for (var index = 1; index <= 1000; index++)
                {
                    var value = (ushort)index;
                    buffer.UpdateReadSignals(new Dictionary<string, ushort[]>
                    {
                        ["Signal.A"] = [value],
                        ["Signal.B"] = [(ushort)(ushort.MaxValue - value)]
                    });
                }
            },
            TestContext.Current.CancellationToken);

        var reader = Task.Run(
            async () =>
            {
                for (var index = 0; index < 2000 || !writer.IsCompleted; index++)
                {
                    Assert.True(
                        buffer.TryCaptureReadSnapshot(
                            ["Signal.A", "Signal.B"],
                            out var snapshot));
                    Assert.NotNull(snapshot);
                    var signalA = snapshot!.Signals["Signal.A"];
                    var signalB = snapshot.Signals["Signal.B"];
                    Assert.Equal(snapshot.Generation, signalA.Generation);
                    Assert.Equal(snapshot.Generation, signalB.Generation);
                    Assert.Equal(
                        (int)ushort.MaxValue,
                        Assert.Single(signalA.Words) + Assert.Single(signalB.Words));
                    await Task.Yield();
                }
            },
            TestContext.Current.CancellationToken);

        await Task.WhenAll(writer, reader);
    }

    [Fact]
    public void PublishReadBatch_WhenMetadataIsMixed_ShouldRejectEntireBatch()
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
        Assert.True(buffer.TryCaptureReadSnapshot(["Signal.A", "Signal.B"], out var before));

        var attemptedAtUtc = DateTimeOffset.UtcNow;
        Assert.Throws<ArgumentException>(
            () => buffer.PublishReadBatch(
                new Dictionary<string, PlcReadSignalUpdate>
                {
                    ["Signal.A"] = new(
                        [(ushort)33],
                        ReadSucceeded: true,
                        Guid.NewGuid(),
                        attemptedAtUtc,
                        FailureReason: null),
                    ["Signal.B"] = new(
                        [(ushort)44],
                        ReadSucceeded: true,
                        Guid.NewGuid(),
                        attemptedAtUtc,
                        FailureReason: null)
                }));

        Assert.True(buffer.TryCaptureReadSnapshot(["Signal.A", "Signal.B"], out var after));
        Assert.NotNull(before);
        Assert.NotNull(after);
        Assert.Equal(before!.Generation, after!.Generation);
        Assert.Equal((ushort)11, Assert.Single(after.Signals["Signal.A"].Words));
        Assert.Equal((ushort)22, Assert.Single(after.Signals["Signal.B"].Words));
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

    [Fact]
    public void PlcDataStore_RegisterWithSameSize_ShouldInvalidatePreviousRuntimeSnapshot()
    {
        var store = new PlcDataStore();
        store.Register(
            2,
            readSize: 2,
            writeSize: 0,
            [
                new("Signal.A", "Read", 0, 1),
                new("Signal.B", "Read", 1, 1)
            ]);
        var buffer = Assert.IsType<PlcBuffer>(store.GetBuffer(2));
        buffer.UpdateReadSignals(new Dictionary<string, ushort[]>
        {
            ["Signal.A"] = [(ushort)11],
            ["Signal.B"] = [(ushort)22]
        });
        Assert.True(buffer.TryCaptureReadSnapshot(["Signal.A", "Signal.B"], out var previous));
        Assert.NotNull(previous);
        var invalidatedSignals = new List<string>();
        EventHandler<PlcSignalBufferChangedEventArgs> invalidationHandler = (sender, args) =>
        {
            _ = sender;
            if (!string.Equals(args.Direction, "Read", StringComparison.Ordinal))
            {
                return;
            }

            Assert.False(buffer.TryCaptureReadSnapshot(["Signal.A", "Signal.B"], out _));
            invalidatedSignals.Add(args.SignalKey);
        };
        buffer.SignalValuesChanged += invalidationHandler;

        store.Register(
            2,
            readSize: 2,
            writeSize: 0,
            [
                new("Signal.A", "Read", 1, 1),
                new("Signal.B", "Read", 0, 1)
            ]);
        buffer.SignalValuesChanged -= invalidationHandler;

        var rebound = Assert.IsType<PlcBuffer>(store.GetBuffer(2));
        Assert.Same(buffer, rebound);
        Assert.False(rebound.TryCaptureReadSnapshot(["Signal.A", "Signal.B"], out _));
        Assert.False(rebound.TryGetReadSignalState("Signal.A", out _));
        Assert.False(rebound.TryGetReadWords("Signal.A", out var unavailableWords));
        Assert.Equal((ushort)0, Assert.Single(unavailableWords));
        Assert.Equal((ushort)0, rebound.GetReadValue(0));
        Assert.Equal((ushort)0, rebound.GetReadValue(1));
        Assert.Equal(
            ["Signal.A", "Signal.B"],
            invalidatedSignals.OrderBy(static key => key, StringComparer.Ordinal).ToArray());

        rebound.UpdateReadSignals(new Dictionary<string, ushort[]>
        {
            ["Signal.A"] = [(ushort)33],
            ["Signal.B"] = [(ushort)44]
        });

        Assert.True(rebound.TryCaptureReadSnapshot(["Signal.A", "Signal.B"], out var current));
        Assert.NotNull(current);
        Assert.True(current!.Generation > previous!.Generation);
        Assert.Equal((ushort)33, Assert.Single(current.Signals["Signal.A"].Words));
        Assert.Equal((ushort)44, Assert.Single(current.Signals["Signal.B"].Words));
    }
}
