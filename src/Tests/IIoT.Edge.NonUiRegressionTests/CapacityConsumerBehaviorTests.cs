using IIoT.Edge.Application.Abstractions.Config;
using IIoT.Edge.Application.Abstractions.DataPipeline.Stores;
using IIoT.Edge.Infrastructure.Integration.Capacity;
using IIoT.Edge.SharedKernel.DataPipeline;
using IIoT.Edge.SharedKernel.DataPipeline.Capacity;
using MediatR;

namespace IIoT.Edge.NonUiRegressionTests;

public sealed class CapacityConsumerBehaviorTests
{
    [Fact]
    public async Task ProcessAsync_WhenCloudUploadDisabled_ShouldKeepLocalCapacityAndSkipCloudBuffer()
    {
        var todayCapacity = new CapturingTodayCapacityStore();
        var capacityBuffer = new FakeCapacityBufferStore();
        var runtimeConfig = new FakeLocalSystemRuntimeConfigService
        {
            Current = SystemRuntimeConfigSnapshot.Default with { SystemCloudEnabled = false }
        };
        var consumer = new CapacityConsumer(
            todayCapacity,
            new FakeDeviceService(),
            runtimeConfig,
            capacityBuffer,
            new NoopPublisher(),
            new FakeLogService(),
            new FakeProductionTimeProvider());

        var result = await consumer.ProcessAsync(new CellCompletedRecord
        {
            CellData = new TestCellData
            {
                Barcode = "BAR-CLOUD-OFF",
                DeviceName = "PLC-CLOUD-OFF",
                CompletedTime = DateTime.UtcNow,
                CellResult = true
            }
        }, TestContext.Current.CancellationToken);

        Assert.True(result);
        Assert.Equal(1, todayCapacity.IncrementCount);
        Assert.Empty(capacityBuffer.Records);
    }

    private sealed class CapturingTodayCapacityStore : ITodayCapacityStore
    {
        public int IncrementCount { get; private set; }

        public string Increment(string deviceName, DateTime completedTime, bool isOk)
        {
            IncrementCount++;
            return "D";
        }

        public TodayCapacity GetSnapshot(string deviceName) => new();

        public void Reset(string deviceName)
        {
        }
    }

    private sealed class NoopPublisher : IPublisher
    {
        public Task Publish(object notification, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task Publish<TNotification>(TNotification notification, CancellationToken cancellationToken = default)
            where TNotification : INotification
            => Task.CompletedTask;
    }
}
