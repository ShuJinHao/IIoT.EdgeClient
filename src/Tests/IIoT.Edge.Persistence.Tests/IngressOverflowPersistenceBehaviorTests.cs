using IIoT.Edge.Module.Contracts.Config;
using IIoT.Edge.Module.Contracts.DataPipeline;
using IIoT.Edge.Host.DataPipeline.Services;
using IIoT.Edge.Module.Contracts.DataPipeline;

namespace IIoT.Edge.Persistence.Tests;

public sealed class IngressOverflowPersistenceBehaviorTests
{
    [Fact]
    public async Task PersistOverflowAsync_WhenCloudAndMesDisabled_ShouldNotWriteExternalBacklog()
    {
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
            new FakeCriticalPersistenceFallbackWriter(),
            persistenceWriter: null!,
            logger: new FakeLogService());

        var result = await persistence.PersistOverflowAsync(new CellCompletedRecord
        {
            CellData = new TestCellData
            {
                Barcode = "BAR-OVERFLOW-DISABLED"
            }
        }, TestContext.Current.CancellationToken);

        Assert.Equal(0, result.PersistedTargetCount);
    }
}
