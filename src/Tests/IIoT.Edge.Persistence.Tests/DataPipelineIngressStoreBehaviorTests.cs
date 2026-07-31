using Dapper;
using IIoT.Edge.Application.Common.DataPipeline;
using IIoT.Edge.Infrastructure.Persistence.Dapper.Connection;
using IIoT.Edge.Infrastructure.Persistence.Dapper.Stores;
using IIoT.Edge.Module.Contracts.DataPipeline;
using IIoT.Edge.Module.Contracts.DataPipeline.CellData;
using Microsoft.Data.Sqlite;

namespace IIoT.Edge.Persistence.Tests;

public sealed class DataPipelineIngressStoreBehaviorTests
{
    [Fact]
    public async Task Ingress_ShouldRoundTripEnvelopeTrackEachConsumerAndKeepCompletionTombstone()
    {
        var tempDir = CreateTempDirectory();
        try
        {
            var factory = new SqliteConnectionFactory(tempDir);
            var store = CreateIngressStore(factory);
            await InitializeAsync(factory, store);
            var record = CreateRecord("CLIP-INGRESS-001");

            var accepted = await store.AcceptAsync(record, TestContext.Current.CancellationToken);
            var restored = Assert.Single(await store.GetPendingAsync(10, TestContext.Current.CancellationToken));

            Assert.Equal(accepted.CompletionId, restored.CompletionId);
            Assert.Equal("PLC-AP-01", restored.Record.PlcCode);
            Assert.Equal("Module.AP.MG1", restored.Record.TaskKey);
            Assert.Equal("CLIP-INGRESS-001", Assert.IsType<TestCellData>(restored.Record.CellData).Barcode);
            Assert.False(await store.CompleteIfAllConsumersFinishedAsync(
                restored.CompletionId,
                ["NONE:CAPACITY", "CLOUD:CLOUD"],
                TestContext.Current.CancellationToken));

            await store.MarkConsumerCompletedAsync(
                restored.CompletionId,
                "NONE:CAPACITY",
                TestContext.Current.CancellationToken);
            Assert.False(await store.CompleteIfAllConsumersFinishedAsync(
                restored.CompletionId,
                ["NONE:CAPACITY", "CLOUD:CLOUD"],
                TestContext.Current.CancellationToken));
            await store.MarkConsumerCompletedAsync(
                restored.CompletionId,
                "CLOUD:CLOUD",
                TestContext.Current.CancellationToken);
            Assert.True(await store.CompleteIfAllConsumersFinishedAsync(
                restored.CompletionId,
                ["NONE:CAPACITY", "CLOUD:CLOUD"],
                TestContext.Current.CancellationToken));

            Assert.Empty(await store.GetPendingAsync(10, TestContext.Current.CancellationToken));
            var duplicate = await store.AcceptAsync(record, TestContext.Current.CancellationToken);
            Assert.True(duplicate.AlreadyCompleted);
            Assert.Empty(await store.GetPendingAsync(10, TestContext.Current.CancellationToken));
        }
        finally
        {
            Cleanup(tempDir);
        }
    }

    [Fact]
    public async Task Ingress_WhenDisplayNameAndLocalRowChange_ShouldReuseOriginalStableEnvelope()
    {
        var tempDir = CreateTempDirectory();
        try
        {
            var factory = new SqliteConnectionFactory(tempDir);
            var store = CreateIngressStore(factory);
            await InitializeAsync(factory, store);
            var original = CreateRecord("CLIP-STABLE-001");
            var first = await store.AcceptAsync(original, TestContext.Current.CancellationToken);

            var renamed = CreateRecord("CLIP-STABLE-001");
            renamed.DeviceName = "改名后的设备";
            renamed.NetworkDeviceId = 999;
            renamed.CellData.DeviceName = "改名后的设备";
            renamed.CellData.PlcDeviceId = 999;
            var second = await store.AcceptAsync(renamed, TestContext.Current.CancellationToken);

            Assert.Equal(first.CompletionId, second.CompletionId);
            Assert.Equal("AP 现场名", second.Record.DeviceName);
            Assert.Equal(17, second.Record.NetworkDeviceId);
            Assert.Single(await store.GetPendingAsync(10, TestContext.Current.CancellationToken));
        }
        finally
        {
            Cleanup(tempDir);
        }
    }

    [Fact]
    public async Task Ingress_NewStoreInstance_ShouldRecoverPendingEnvelopeAndConsumerState()
    {
        var tempDir = CreateTempDirectory();
        try
        {
            var factory = new SqliteConnectionFactory(tempDir);
            var firstStore = CreateIngressStore(factory);
            await InitializeAsync(factory, firstStore);
            var accepted = await firstStore.AcceptAsync(
                CreateRecord("CLIP-RESTART-001"),
                TestContext.Current.CancellationToken);
            await firstStore.MarkConsumerCompletedAsync(
                accepted.CompletionId,
                "NONE:CAPACITY",
                TestContext.Current.CancellationToken);

            var restartedStore = CreateIngressStore(factory);
            var pending = Assert.Single(await restartedStore.GetPendingAsync(
                10,
                TestContext.Current.CancellationToken));

            Assert.Contains("NONE:CAPACITY", pending.CompletedConsumerKeys);
            Assert.Equal("CLIP-RESTART-001", Assert.IsType<TestCellData>(pending.Record.CellData).Barcode);
        }
        finally
        {
            Cleanup(tempDir);
        }
    }

    private static DataPipelineIngressStore CreateIngressStore(SqliteConnectionFactory factory)
        => new(factory, new FakeLogService(), CreateSerializer());

    private static ICellDataJsonSerializer CreateSerializer()
    {
        var registry = new CellDataTypeRegistry();
        registry.Register<TestCellData>("OtherProcess");
        return new CellDataJsonSerializer(registry);
    }

    private static CellCompletedRecord CreateRecord(string businessIdentifier)
        => new()
        {
            PlcCode = "PLC-AP-01",
            NetworkDeviceId = 17,
            DeviceName = "AP 现场名",
            ModuleId = "Module.AP",
            TaskKey = "Module.AP.MG1",
            PlanSessionId = "SESSION-001",
            MainPlanCode = "PLAN-001",
            TraceBatchNumber = "TRACE-001",
            IdempotencyKeyVersion = CloudIdempotencyKeyVersion.PlcStableV2,
            CreatedAtUtc = new DateTime(2026, 7, 31, 8, 0, 0, DateTimeKind.Utc),
            CellData = new TestCellData
            {
                Barcode = businessIdentifier,
                DeviceCode = "PLC-AP-01",
                DeviceName = "AP 现场名",
                PlcDeviceId = 17,
                CompletedTime = new DateTime(2026, 7, 31, 7, 59, 0, DateTimeKind.Utc),
                CellResult = true,
                UploadTargets = DataPipelineUploadTargets.Cloud
            }
        };

    private static async Task InitializeAsync(
        SqliteConnectionFactory factory,
        DataPipelineIngressStore store)
    {
        using var connection = factory.Create(store.DbName);
        await store.InitializeTableAsync(connection);
    }

    private static string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "edge-ingress-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static void Cleanup(string path)
    {
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive: true);
        }
    }
}
