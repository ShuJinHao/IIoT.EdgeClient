using Dapper;
using IIoT.Edge.Infrastructure.Persistence.Dapper.Connection;
using IIoT.Edge.Infrastructure.Persistence.Dapper.Stores;
using IIoT.Edge.Module.Contracts.DataPipeline;
using IIoT.Edge.Module.Contracts.DataPipeline.CellData;
using Microsoft.Data.Sqlite;

namespace IIoT.Edge.Persistence.Tests;

public sealed class RetryDeadLetterAtomicTransitionBehaviorTests
{
    [Fact]
    public async Task TwentiethFailure_ShouldMoveRetryToSameChannelDeadLetterInOneTransaction()
    {
        var tempDir = CreateTempDirectory();
        try
        {
            var factory = new SqliteConnectionFactory(tempDir);
            var retry = CreateRetryStore(factory);
            var dead = new CloudDeadLetterStore(factory, new FakeLogService());
            await InitializeAsync(factory, retry, dead);
            await retry.SaveAsync(
                CreateRecord("CLIP-EXHAUSTED"),
                "Cloud",
                "attempt-19",
                TestContext.Current.CancellationToken);
            var source = await ReadRetryAsync(factory);

            await retry.MoveExhaustedRetryToDeadLetterAsync(
                source,
                20,
                "http_failure",
                TestContext.Current.CancellationToken);

            Assert.Equal(0, await retry.GetCountAsync());
            var deadLetter = Assert.Single(await dead.GetLatestAsync());
            Assert.Equal("failed_cloud_records", deadLetter.SourceTable);
            Assert.Equal(source.Id, deadLetter.SourceRecordId);
            Assert.Equal("RetryExhausted", deadLetter.FailureStage);
            Assert.Contains("retry_count=20", deadLetter.FailureReason, StringComparison.Ordinal);
            Assert.Equal("PLC-CP-01", deadLetter.PlcCode);
            Assert.Equal("Module.CP.MG2", deadLetter.TaskKey);
        }
        finally
        {
            Cleanup(tempDir);
        }
    }

    [Fact]
    public async Task TwentiethFailure_WhenDeadLetterWriteFails_ShouldKeepOriginalRetry()
    {
        var tempDir = CreateTempDirectory();
        try
        {
            var factory = new SqliteConnectionFactory(tempDir);
            var retry = CreateRetryStore(factory);
            using (var connection = factory.Create(retry.DbName))
            {
                await retry.InitializeTableAsync(connection);
            }
            await retry.SaveAsync(
                CreateRecord("CLIP-DL-FAIL"),
                "Cloud",
                "attempt-19",
                TestContext.Current.CancellationToken);
            var source = await ReadRetryAsync(factory);

            await Assert.ThrowsAnyAsync<Exception>(() => retry.MoveExhaustedRetryToDeadLetterAsync(
                source,
                20,
                "http_failure",
                TestContext.Current.CancellationToken));

            Assert.Equal(1, await retry.GetCountAsync());
            Assert.Equal(source.Id, (await ReadRetryAsync(factory)).Id);
        }
        finally
        {
            Cleanup(tempDir);
        }
    }

    [Fact]
    public async Task ManualRequeue_ShouldWriteRetryRemoveDeadLetterAndAuditInOneTransaction()
    {
        var tempDir = CreateTempDirectory();
        try
        {
            var factory = new SqliteConnectionFactory(tempDir);
            var retry = CreateRetryStore(factory);
            var dead = new CloudDeadLetterStore(factory, new FakeLogService());
            await InitializeAsync(factory, retry, dead);
            await dead.SaveAsync(
                CreateDeadLetter("CLIP-REQUEUE"),
                TestContext.Current.CancellationToken);
            var deadLetterId = Assert.Single(await dead.GetLatestAsync()).Id;

            await retry.RequeueAndRemoveAsync(
                deadLetterId,
                "LOCAL-ADMIN-01",
                "CLIP-REQUEUE",
                TestContext.Current.CancellationToken);

            Assert.Empty(await dead.GetLatestAsync());
            var pending = await ReadRetryAsync(factory);
            Assert.Equal(0, pending.RetryCount);
            Assert.Equal("PLC-CP-01", pending.PlcCode);
            using var connection = factory.Create(retry.DbName);
            var audit = await connection.QuerySingleAsync<RequeueAuditRow>(
                "SELECT * FROM deadletter_cloud_requeue_audit");
            Assert.Equal(deadLetterId, audit.DeadLetterId);
            Assert.Equal("LOCAL-ADMIN-01", audit.OperatorId);
            Assert.Equal("CLIP-REQUEUE", audit.BusinessIdentifier);
            Assert.Equal("Requeued", audit.Result);
        }
        finally
        {
            Cleanup(tempDir);
        }
    }

    [Fact]
    public async Task ManualRequeue_WhenAuditWriteFails_ShouldRollbackRetryAndKeepUniqueDeadLetter()
    {
        var tempDir = CreateTempDirectory();
        try
        {
            var factory = new SqliteConnectionFactory(tempDir);
            var retry = CreateRetryStore(factory);
            var dead = new CloudDeadLetterStore(factory, new FakeLogService());
            await InitializeAsync(factory, retry, dead);
            await dead.SaveAsync(
                CreateDeadLetter("CLIP-ROLLBACK"),
                TestContext.Current.CancellationToken);
            var deadLetterId = Assert.Single(await dead.GetLatestAsync()).Id;
            using (var connection = factory.Create(retry.DbName))
            {
                await connection.ExecuteAsync("DROP TABLE deadletter_cloud_requeue_audit");
            }

            await Assert.ThrowsAnyAsync<Exception>(() => retry.RequeueAndRemoveAsync(
                deadLetterId,
                "LOCAL-ADMIN-01",
                "CLIP-ROLLBACK",
                TestContext.Current.CancellationToken));

            Assert.Equal(0, await retry.GetCountAsync());
            Assert.Single(await dead.GetLatestAsync());
        }
        finally
        {
            Cleanup(tempDir);
        }
    }

    private static CloudRetryRecordStore CreateRetryStore(SqliteConnectionFactory factory)
        => new(factory, new FakeLogService(), CreateSerializer());

    private static ICellDataJsonSerializer CreateSerializer()
    {
        var registry = new CellDataTypeRegistry();
        registry.Register<TestCellData>("OtherProcess");
        return new CellDataJsonSerializer(registry);
    }

    private static CellCompletedRecord CreateRecord(string barcode)
        => new()
        {
            PlcCode = "PLC-CP-01",
            NetworkDeviceId = 29,
            DeviceName = "CP 现场名",
            ModuleId = "Module.CP",
            TaskKey = "Module.CP.MG2",
            TraceBatchNumber = "TRACE-CP-01",
            IdempotencyKeyVersion = CloudIdempotencyKeyVersion.PlcStableV2,
            CellData = new TestCellData
            {
                Barcode = barcode,
                DeviceCode = "PLC-CP-01",
                DeviceName = "CP 现场名",
                PlcDeviceId = 29,
                CompletedTime = DateTime.UtcNow,
                CellResult = true,
                UploadTargets = DataPipelineUploadTargets.Cloud
            }
        };

    private static DeadLetterRecord CreateDeadLetter(string businessIdentifier)
    {
        var serializer = CreateSerializer();
        var source = CreateRecord(businessIdentifier);
        return new DeadLetterRecord
        {
            ProcessType = source.CellData.ProcessType,
            CellDataJson = serializer.Serialize(source.CellData),
            FailedTarget = "Cloud",
            SourceTable = "failed_cloud_records",
            SourceRecordId = 10,
            FailureStage = "RetryExhausted",
            FailureReason = "retry_count=20;http_failure",
            CreatedAt = DateTime.UtcNow,
            PlcCode = source.PlcCode,
            NetworkDeviceId = source.NetworkDeviceId,
            DeviceName = source.DeviceName,
            ModuleId = source.ModuleId,
            TaskKey = source.TaskKey,
            TraceBatchNumber = source.TraceBatchNumber,
            IdempotencyKeyVersion = source.IdempotencyKeyVersion
        };
    }

    private static async Task InitializeAsync(
        SqliteConnectionFactory factory,
        CloudRetryRecordStore retry,
        CloudDeadLetterStore dead)
    {
        using var connection = factory.Create(retry.DbName);
        await retry.InitializeTableAsync(connection);
        await dead.InitializeTableAsync(connection);
    }

    private static async Task<FailedCellRecord> ReadRetryAsync(SqliteConnectionFactory factory)
    {
        using var connection = factory.Create("pipeline_cloud");
        return await connection.QuerySingleAsync<FailedCellRecord>(
            "SELECT * FROM failed_cloud_records");
    }

    private static string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "edge-retry-transition-tests", Guid.NewGuid().ToString("N"));
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

    private sealed class RequeueAuditRow
    {
        public long DeadLetterId { get; init; }
        public string OperatorId { get; init; } = string.Empty;
        public string BusinessIdentifier { get; init; } = string.Empty;
        public string Result { get; init; } = string.Empty;
    }
}
