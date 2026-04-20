using IIoT.Edge.Application.Abstractions.Device;
using IIoT.Edge.Application.Abstractions.Plc.Store;
using IIoT.Edge.Runtime.Scan.Implementations;
using IIoT.Edge.SharedKernel.Context;

namespace IIoT.Edge.NonUiRegressionTests;

public sealed class LoadingScanTaskBehaviorTests
{
    [Fact]
    public async Task ExecuteStep10_WhenBarcodeReadTimesOut_ShouldWriteNgAndAdvanceToResetStep()
    {
        var buffer = new FakePlcBuffer();
        var context = new ProductionContext { DeviceName = "PLC-A" };
        var logger = new FakeLogService();
        var barcodeReader = new BlockingBarcodeReader();
        var task = new TestableLoadingScanTask(buffer, context, logger, barcodeReader);

        context.SetStep(task.TaskName, 10);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await task.ExecuteCoreAsync(cts.Token);

        Assert.Equal(12, buffer.GetWrittenValue(1));
        Assert.Equal(30, context.GetStep(task.TaskName));
        Assert.False(task.LastResult);
        Assert.Contains(logger.Entries, entry => entry.Message.Contains("timed out", StringComparison.OrdinalIgnoreCase));
    }

    private sealed class TestableLoadingScanTask(
        IPlcBuffer buffer,
        ProductionContext context,
        FakeLogService logger,
        IBarcodeReader barcodeReader)
        : LoadingScanTask(
            buffer,
            context,
            logger,
            taskName: "LoadingScan",
            triggerIndex: 0,
            responseIndex: 1,
            barcodeReader: barcodeReader,
            localDuplicateChecker: _ => Task.FromResult(false))
    {
        public async Task ExecuteCoreAsync(CancellationToken cancellationToken)
        {
            SetTaskCancellationToken(cancellationToken);
            await DoCoreAsync();
        }
    }

    private sealed class BlockingBarcodeReader : IBarcodeReader
    {
        public async Task<string[]> ReadAsync(CancellationToken cancellationToken = default)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return [];
        }
    }

    private sealed class FakePlcBuffer : IPlcBuffer
    {
        private readonly Dictionary<int, ushort> _readValues = new();
        private readonly Dictionary<int, ushort> _writtenValues = new();

        public ushort GetReadValue(int index)
            => _readValues.TryGetValue(index, out var value) ? value : (ushort)0;

        public void SetWriteValue(int index, ushort value)
            => _writtenValues[index] = value;

        public ushort GetWrittenValue(int index)
            => _writtenValues.TryGetValue(index, out var value) ? value : (ushort)0;
    }
}
