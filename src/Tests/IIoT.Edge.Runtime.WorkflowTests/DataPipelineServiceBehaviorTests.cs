using IIoT.Edge.Module.Contracts.DataPipeline;
using IIoT.Edge.Host.DataPipeline.Services;
using IIoT.Edge.Module.Contracts.DataPipeline;

namespace IIoT.Edge.Runtime.WorkflowTests;

public sealed class DataPipelineServiceBehaviorTests
{
    [Fact]
    public async Task EnqueueAsync_WhenPlcContextMissing_ShouldRejectRecord()
    {
        var overflowPersistence = new FakeIngressOverflowPersistence();
        var logger = new FakeLogService();
        var pipeline = new DataPipelineService(overflowPersistence, logger);

        var result = await pipeline.EnqueueAsync(
            new CellCompletedRecord
            {
                CellData = new TestProcessCellData
                {
                    Barcode = "BC-MISSING",
                    WorkOrderNo = "WO-MISSING",
                    CompletedTime = DateTime.UtcNow
                }
            },
            TestContext.Current.CancellationToken);

        Assert.False(result.IsDurablyAccepted);
        Assert.Equal("missing_plc_context", result.ReasonCode);
        Assert.Equal(0, pipeline.PendingCount);
        Assert.Empty(overflowPersistence.Records);
        Assert.Contains(
            logger.Entries,
            entry => entry.Level == "Warn"
                     && entry.Message.Contains("缺少 PLC 上下文", StringComparison.Ordinal));
    }

    [Fact]
    public async Task EnqueueAsync_WhenQueueOverflows_ShouldPersistOverflowAndTrackCounters()
    {
        var overflowPersistence = new FakeIngressOverflowPersistence
        {
            Result = DataPipelineEnqueueResult.OverflowPersisted(1, 1)
        };
        var pipeline = new DataPipelineService(overflowPersistence, new FakeLogService());

        DataPipelineEnqueueResult overflowResult = DataPipelineEnqueueResult.Rejected("not_reached");
        for (var i = 0; i < 6000; i++)
        {
            var result = await pipeline.EnqueueAsync(
                CreateRecord($"BC-{i:D4}"),
                TestContext.Current.CancellationToken);
            if (!result.WasOverflow)
            {
                continue;
            }

            overflowResult = result;
            break;
        }

        Assert.True(overflowResult.WasOverflow);
        Assert.Equal(1, overflowResult.PersistedTargetCount);
        Assert.Single(overflowPersistence.Records);
        Assert.True(pipeline.PendingCount <= 5000);
        Assert.Equal(1, pipeline.OverflowCount);
        Assert.Equal(1, pipeline.SpillCount);
    }

    private static CellCompletedRecord CreateRecord(string barcode)
        => new()
        {
            NetworkDeviceId = 1,
            DeviceName = "PLC-A",
            ModuleId = "TestProcess",
            TaskKey = "TestProcess.Realtime",
            CellData = new TestProcessCellData
            {
                PlcDeviceId = 1,
                DeviceName = "PLC-A",
                DeviceCode = "PLC-A",
                Barcode = barcode,
                WorkOrderNo = $"WO-{barcode}",
                CompletedTime = DateTime.UtcNow
            }
        };
}
