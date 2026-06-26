using IIoT.Edge.Application.Abstractions.Config;
using IIoT.Edge.Application.Abstractions.DataPipeline;
using IIoT.Edge.Host.DataPipeline.Services;
using IIoT.Edge.SharedKernel.DataPipeline;

namespace IIoT.Edge.NonUiRegressionTests;

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
            new FakeProcessIntegrationRegistry(),
            new FakeModuleParamRoleProvider(),
            new FakeCriticalPersistenceFallbackWriter(),
            persistenceWriter: null!,
            logger: new FakeLogService());

        var result = await persistence.PersistOverflowAsync(new CellCompletedRecord
        {
            CellData = new TestCellData
            {
                Barcode = "BAR-OVERFLOW-DISABLED"
            }
        });

        Assert.Equal(0, result.PersistedTargetCount);
    }
}
