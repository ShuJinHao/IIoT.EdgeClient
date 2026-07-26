using IIoT.Edge.Module.Contracts.Config;
using IIoT.Edge.Module.Contracts.DataPipeline;
using IIoT.Edge.Module.Contracts.DataPipeline.CellData;
using IIoT.Edge.Module.Contracts.Modules;
using IIoT.Edge.Host.DataPipeline.Services;
using Microsoft.Extensions.Options;

namespace IIoT.Edge.Persistence.Tests;

public sealed class IngressOverflowPersistenceBehaviorTests
{
    [Fact]
    public async Task PersistOverflowAsync_WhenCloudAndMesDisabled_ShouldPersistFrozenMesTargetOnly()
    {
        var logger = new FakeLogService();
        var cloudRetryStore = new FakeFailedRecordStore();
        var mesRetryStore = new FakeFailedRecordStore();
        var cloudFallbackStore = new FakeCloudFallbackBufferStore();
        var mesFallbackStore = new FakeMesFallbackBufferStore();
        var cloudDeadLetterStore = new FakeCloudDeadLetterStore();
        var mesDeadLetterStore = new FakeMesDeadLetterStore();
        var criticalWriter = new FakeCriticalPersistenceFallbackWriter();
        var capacityGuard = new DataPipelineCapacityGuard(
            Options.Create(new DataPipelineCapacityOptions()),
            cloudRetryStore,
            mesRetryStore,
            cloudFallbackStore,
            mesFallbackStore,
            new FakeCloudDiagnosticsStore(),
            new FakeMesRetryDiagnosticsStore(),
            logger);
        var persistenceWriter = new DataPipelineCascadingPersistenceWriter(
            cloudRetryStore,
            mesRetryStore,
            cloudFallbackStore,
            mesFallbackStore,
            cloudDeadLetterStore,
            mesDeadLetterStore,
            criticalWriter,
            capacityGuard,
            logger,
            new CellDataJsonSerializer(new CellDataTypeRegistry()));
        var persistence = new IngressOverflowPersistence(
            [
                new FakeCellDataConsumer("Cloud", 10, "Cloud", result: false, ConsumerFailureMode.Durable),
                new FakeCellDataConsumer("MES", 20, "Mes", result: false, ConsumerFailureMode.Durable)
            ],
            new FakeLocalSystemRuntimeConfigService
            {
                Current = SystemRuntimeConfigSnapshot.Default with
                {
                    SystemCloudEnabled = false,
                    MesUploadEnabled = false
                }
            },
            criticalWriter,
            persistenceWriter,
            logger);

        var result = await persistence.PersistOverflowAsync(new CellCompletedRecord
        {
            CellData = new TestCellData
            {
                Barcode = "BAR-OVERFLOW-DISABLED",
                UploadTargets = DataPipelineUploadTargets.All
            }
        }, TestContext.Current.CancellationToken);

        Assert.Equal(1, result.PersistedTargetCount);
        Assert.Empty(cloudRetryStore.PendingRecords);
        var mesRetry = Assert.Single(mesRetryStore.PendingRecords);
        Assert.Equal("MES", mesRetry.Channel);
        Assert.Equal("MES", mesRetry.FailedTarget);
        Assert.Empty(cloudFallbackStore.Records);
        Assert.Empty(mesFallbackStore.Records);
        Assert.Empty(cloudDeadLetterStore.Records);
        Assert.Empty(mesDeadLetterStore.Records);
        Assert.Empty(criticalWriter.Writes);
    }
}
