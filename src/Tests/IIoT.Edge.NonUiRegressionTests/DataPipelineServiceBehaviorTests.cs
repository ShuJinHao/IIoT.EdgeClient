using IIoT.Edge.Application.Abstractions.DataPipeline;
using IIoT.Edge.Module.Injection.Payload;
using IIoT.Edge.Runtime.DataPipeline.Services;
using IIoT.Edge.SharedKernel.DataPipeline;

namespace IIoT.Edge.NonUiRegressionTests;

public sealed class DataPipelineServiceBehaviorTests
{
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
            var result = await pipeline.EnqueueAsync(CreateRecord($"BC-{i:D4}"));
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
            CellData = new InjectionCellData
            {
                Barcode = barcode,
                WorkOrderNo = $"WO-{barcode}",
                CompletedTime = DateTime.UtcNow
            }
        };
}
