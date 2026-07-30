using IIoT.Edge.Module.Contracts.Config;
using IIoT.Edge.Module.Contracts.DataPipeline.Stores;
using IIoT.Edge.Infrastructure.Integration.Capacity;
using IIoT.Edge.Module.Contracts.DataPipeline;
using IIoT.Edge.Module.Contracts.DataPipeline.Capacity;
using MediatR;

namespace IIoT.Edge.Runtime.WorkflowTests;

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
            PlcCode = "P1-AP01",
            NetworkDeviceId = 1,
            DeviceName = "当前显示名",
            ModuleId = "TestModule",
            TaskKey = "TestModule.Capacity",
            CellData = new TestCellData
            {
                Barcode = "BAR-CLOUD-OFF",
                DeviceCode = "P1-AP01",
                DeviceName = "当前显示名",
                PlcDeviceId = 1,
                CompletedTime = DateTime.UtcNow,
                CellResult = true
            }
        }, TestContext.Current.CancellationToken);

        Assert.True(result);
        Assert.Equal(1, todayCapacity.IncrementCount);
        Assert.Equal("P1-AP01", todayCapacity.LastPlcCode);
        Assert.Empty(capacityBuffer.Records);
    }

    [Fact]
    public async Task ProcessAsync_WhenCloudIsUnavailable_ShouldBufferStablePlcCodeInsteadOfDisplayName()
    {
        var todayCapacity = new CapturingTodayCapacityStore();
        var capacityBuffer = new FakeCapacityBufferStore();
        var consumer = new CapacityConsumer(
            todayCapacity,
            new FakeDeviceService(),
            new FakeLocalSystemRuntimeConfigService
            {
                Current = SystemRuntimeConfigSnapshot.Default with { SystemCloudEnabled = true }
            },
            capacityBuffer,
            new NoopPublisher(),
            new FakeLogService(),
            new FakeProductionTimeProvider());

        var result = await consumer.ProcessAsync(new CellCompletedRecord
        {
            PlcCode = "P1-CP01",
            NetworkDeviceId = 7,
            DeviceName = "改名后的 CP",
            ModuleId = "TestModule",
            TaskKey = "TestModule.Capacity",
            CellData = new TestCellData
            {
                Barcode = "BAR-BUFFER",
                DeviceCode = "P1-CP01",
                DeviceName = "改名后的 CP",
                PlcDeviceId = 7,
                CompletedTime = DateTime.UtcNow,
                CellResult = true
            }
        }, TestContext.Current.CancellationToken);

        Assert.True(result);
        var buffered = Assert.Single(capacityBuffer.Records);
        Assert.Equal("P1-CP01", buffered.PlcName);
    }

    private sealed class CapturingTodayCapacityStore : ITodayCapacityStore
    {
        public int IncrementCount { get; private set; }

        public string? LastPlcCode { get; private set; }

        public string Increment(string plcCode, DateTime completedTime, bool isOk)
        {
            IncrementCount++;
            LastPlcCode = plcCode;
            return "D";
        }

        public TodayCapacity GetSnapshot(string plcCode) => new();

        public void Reset(string plcCode)
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
