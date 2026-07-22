using Dapper;
using IIoT.Edge.Application.Common.Persistence;
using IIoT.Edge.Infrastructure.Persistence.Dapper.Connection;
using IIoT.Edge.Infrastructure.Persistence.Dapper.Stores;
using IIoT.Edge.Module.Contracts.DataPipeline;
using IIoT.Edge.Module.Contracts.DataPipeline.CellData;
using Microsoft.Data.Sqlite;

namespace IIoT.Edge.Persistence.Tests;

public sealed class FailedRecordStoreBehaviorTests
{
    [Fact]
    public async Task GetPendingAsync_WhenDatabaseOpenFails_ShouldThrowPersistenceAccessException()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "edge-failed-store-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try
        {
            Directory.CreateDirectory(Path.Combine(tempDir, "pipeline_cloud.db"));

            var logger = new FakeLogService();
            var connectionFactory = new SqliteConnectionFactory(tempDir);
            var store = new CloudRetryRecordStore(connectionFactory, logger, CreateCellDataJsonSerializer());

            var exception = await Assert.ThrowsAsync<PersistenceAccessException>(
                () => store.GetPendingAsync());

            Assert.Contains("查询失败", exception.Message, StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                try
                {
                    Directory.Delete(tempDir, recursive: true);
                }
                catch (IOException)
                {
                }
            }
        }
    }

    [Fact]
    public async Task DeleteExpiredAbandonedAsync_ShouldDeleteOnlyExpiredAbandonedRecords()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "edge-failed-store-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try
        {
            var logger = new FakeLogService();
            var connectionFactory = new SqliteConnectionFactory(tempDir);
            var store = new CloudRetryRecordStore(connectionFactory, logger, CreateCellDataJsonSerializer());

            using (var connection = connectionFactory.Create(store.DbName))
            {
                await store.InitializeTableAsync(connection);
            }

            await store.SaveAsync(CreateRecord("OLD"), "Cloud-Old", "seed", TestContext.Current.CancellationToken);
            await store.SaveAsync(CreateRecord("RECENT"), "Cloud-Recent", "seed", TestContext.Current.CancellationToken);
            await store.SaveAsync(CreateRecord("ACTIVE"), "Cloud-Active", "seed", TestContext.Current.CancellationToken);

            var abandonedTimeUtc = DateTime.SpecifyKind(DateTime.MaxValue, DateTimeKind.Utc).ToString("O");
            await UpdateFailedRecordAsync(
                connectionFactory,
                "Cloud-Old",
                abandonedTimeUtc,
                DateTime.UtcNow.AddDays(-40).ToString("O"));
            await UpdateFailedRecordAsync(
                connectionFactory,
                "Cloud-Recent",
                abandonedTimeUtc,
                DateTime.UtcNow.AddDays(-5).ToString("O"));
            await UpdateFailedRecordAsync(
                connectionFactory,
                "Cloud-Active",
                DateTime.UtcNow.AddMinutes(-1).ToString("O"),
                DateTime.UtcNow.AddDays(-40).ToString("O"));

            var deleted = await store.DeleteExpiredAbandonedAsync(DateTime.UtcNow.AddDays(-30));

            Assert.Equal(1, deleted);
            Assert.Equal(0, await CountByFailedTargetAsync(connectionFactory, "Cloud-Old"));
            Assert.Equal(1, await CountByFailedTargetAsync(connectionFactory, "Cloud-Recent"));
            Assert.Equal(1, await CountByFailedTargetAsync(connectionFactory, "Cloud-Active"));
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                try
                {
                    Directory.Delete(tempDir, recursive: true);
                }
                catch (IOException)
                {
                }
            }
        }
    }

    [Fact]
    public async Task ClaimPendingBatchAsync_ShouldRespectReleaseAndDeleteLifecycle()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "edge-failed-store-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try
        {
            var logger = new FakeLogService();
            var connectionFactory = new SqliteConnectionFactory(tempDir);
            var store = new CloudRetryRecordStore(connectionFactory, logger, CreateCellDataJsonSerializer());

            using (var connection = connectionFactory.Create(store.DbName))
            {
                await store.InitializeTableAsync(connection);
            }

            await store.SaveAsync(CreateRecord("CLAIM-A"), "Cloud-Claim-A", "seed", TestContext.Current.CancellationToken);
            await store.SaveAsync(CreateRecord("CLAIM-B"), "Cloud-Claim-B", "seed", TestContext.Current.CancellationToken);

            await UpdateFailedRecordAsync(
                connectionFactory,
                "Cloud-Claim-A",
                DateTime.UtcNow.AddMinutes(-1).ToString("O"),
                DateTime.UtcNow.AddMinutes(-5).ToString("O"));
            await UpdateFailedRecordAsync(
                connectionFactory,
                "Cloud-Claim-B",
                DateTime.UtcNow.AddMinutes(-1).ToString("O"),
                DateTime.UtcNow.AddMinutes(-4).ToString("O"));

            var firstClaim = await store.ClaimPendingBatchAsync(batchSize: 1);
            Assert.NotNull(firstClaim);
            Assert.Single(firstClaim!.Records);

            var secondClaim = await store.ClaimPendingBatchAsync(batchSize: 10);
            Assert.NotNull(secondClaim);
            Assert.Single(secondClaim!.Records);
            Assert.NotEqual(firstClaim.Records[0].Id, secondClaim.Records[0].Id);

            await store.ReleaseClaimAsync(firstClaim.ClaimToken);

            var releasedClaim = await store.ClaimPendingBatchAsync(batchSize: 1);
            Assert.NotNull(releasedClaim);
            Assert.Equal(firstClaim.Records[0].Id, releasedClaim!.Records[0].Id);

            await store.DeleteClaimedBatchAsync(releasedClaim.ClaimToken);

            Assert.Equal(1, await CountTableRowsAsync(connectionFactory, "failed_cloud_records"));
            Assert.Equal(1, await CountTableRowsAsync(connectionFactory, "failed_cloud_record_claims"));
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                try
                {
                    Directory.Delete(tempDir, recursive: true);
                }
                catch (IOException)
                {
                }
            }
        }
    }

    [Fact]
    public async Task MovePendingToRetryAsync_ShouldMoveFallbackRowsIntoRetryTable()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "edge-failed-store-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try
        {
            var logger = new FakeLogService();
            var connectionFactory = new SqliteConnectionFactory(tempDir);
            var cellDataJsonSerializer = CreateCellDataJsonSerializer();
            var retryStore = new CloudRetryRecordStore(connectionFactory, logger, cellDataJsonSerializer);
            var fallbackStore = new CloudFallbackBufferStore(connectionFactory, logger, cellDataJsonSerializer);

            using (var connection = connectionFactory.Create(retryStore.DbName))
            {
                await retryStore.InitializeTableAsync(connection);
                await fallbackStore.InitializeTableAsync(connection);
            }

            await fallbackStore.SaveAsync(CreateRecord("MOVE-1"), "Cloud-Move", "seed", TestContext.Current.CancellationToken);
            var pendingFallback = await fallbackStore.GetPendingAsync();
            var fallbackId = Assert.Single(pendingFallback).Id;

            await fallbackStore.MovePendingToRetryAsync([fallbackId]);

            Assert.Empty(await fallbackStore.GetPendingAsync());
            Assert.Equal(1, await CountTableRowsAsync(connectionFactory, "failed_cloud_records"));
            Assert.Equal(0, await CountTableRowsAsync(connectionFactory, "cloud_fallback_records"));
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                try
                {
                    Directory.Delete(tempDir, recursive: true);
                }
                catch (IOException)
                {
                }
            }
        }
    }

    [Fact]
    public async Task MovePendingToRetryAsync_WhenDeleteFails_ShouldRollbackInsertedRetryRow()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "edge-failed-store-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try
        {
            var logger = new FakeLogService();
            var connectionFactory = new SqliteConnectionFactory(tempDir);
            var cellDataJsonSerializer = CreateCellDataJsonSerializer();
            var retryStore = new CloudRetryRecordStore(connectionFactory, logger, cellDataJsonSerializer);
            var fallbackStore = new CloudFallbackBufferStore(connectionFactory, logger, cellDataJsonSerializer);

            using (var connection = connectionFactory.Create(retryStore.DbName))
            {
                await retryStore.InitializeTableAsync(connection);
                await fallbackStore.InitializeTableAsync(connection);
                await connection.ExecuteAsync(@"
                    CREATE TRIGGER fail_cloud_fallback_delete
                    BEFORE DELETE ON cloud_fallback_records
                    BEGIN
                        SELECT RAISE(ABORT, 'forced fallback delete failure');
                    END;");
            }

            await fallbackStore.SaveAsync(
                CreateRecord("MOVE-ROLLBACK"),
                "Cloud-Move-Rollback",
                "seed",
                TestContext.Current.CancellationToken);
            var fallbackId = Assert.Single(await fallbackStore.GetPendingAsync()).Id;

            var exception = await Assert.ThrowsAsync<PersistenceAccessException>(
                () => fallbackStore.MovePendingToRetryAsync([fallbackId]));

            Assert.Contains("事务执行失败", exception.Message, StringComparison.Ordinal);
            Assert.Equal(0, await CountTableRowsAsync(connectionFactory, "failed_cloud_records"));
            Assert.Equal(1, await CountTableRowsAsync(connectionFactory, "cloud_fallback_records"));
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, recursive: true);
            }
        }
    }

    [Fact]
    public async Task SaveAsync_WhenCallerIsPreCanceled_ShouldPersistNoRetryFallbackOrDeadLetterRows()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "edge-failed-store-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try
        {
            var logger = new FakeLogService();
            var connectionFactory = new SqliteConnectionFactory(tempDir);
            var cellDataJsonSerializer = CreateCellDataJsonSerializer();
            var retryStore = new CloudRetryRecordStore(connectionFactory, logger, cellDataJsonSerializer);
            var fallbackStore = new CloudFallbackBufferStore(connectionFactory, logger, cellDataJsonSerializer);
            var deadLetterStore = new CloudDeadLetterStore(connectionFactory, logger);

            using (var connection = connectionFactory.Create(retryStore.DbName))
            {
                await retryStore.InitializeTableAsync(connection);
                await fallbackStore.InitializeTableAsync(connection);
                await deadLetterStore.InitializeTableAsync(connection);
            }

            using var cancellation = new CancellationTokenSource();
            await cancellation.CancelAsync();
            var record = CreateRecord("CANCEL-SQLITE");
            var operations = new Func<CancellationToken, Task>[]
            {
                token => retryStore.SaveAsync(record, "Cloud", "cancel", token),
                token => fallbackStore.SaveAsync(record, "Cloud", "cancel", token),
                token => deadLetterStore.SaveAsync(new DeadLetterRecord
                {
                    ProcessType = record.CellData.ProcessType,
                    CellDataJson = cellDataJsonSerializer.Serialize(record.CellData),
                    FailedTarget = "Cloud",
                    SourceTable = "failed_cloud_records",
                    FailureStage = "RetryPersist",
                    FailureReason = "cancel",
                    CreatedAt = DateTime.UtcNow
                }, token)
            };

            foreach (var operation in operations)
            {
                var actual = await Assert.ThrowsAnyAsync<OperationCanceledException>(
                    () => operation(cancellation.Token));
                Assert.Equal(cancellation.Token, actual.CancellationToken);
            }

            Assert.Equal(0, await CountTableRowsAsync(connectionFactory, "failed_cloud_records"));
            Assert.Equal(0, await CountTableRowsAsync(connectionFactory, "cloud_fallback_records"));
            Assert.Equal(0, await CountTableRowsAsync(connectionFactory, "dead_cloud_records"));
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, recursive: true);
            }
        }
    }

    private static CellCompletedRecord CreateRecord(string barcode)
    {
        return new CellCompletedRecord
        {
            CellData = new TestProcessCellData
            {
                Barcode = barcode,
                WorkOrderNo = $"WO-{barcode}",
                CompletedTime = DateTime.UtcNow
            }
        };
    }

    private static ICellDataJsonSerializer CreateCellDataJsonSerializer()
        => new CellDataJsonSerializer(new CellDataTypeRegistry());

    private static async Task UpdateFailedRecordAsync(
        SqliteConnectionFactory connectionFactory,
        string failedTarget,
        string nextRetryTime,
        string createdAt)
    {
        using var connection = (SqliteConnection)connectionFactory.Create("pipeline_cloud");
        await using var command = connection.CreateCommand();
        command.CommandText = @"
            UPDATE failed_cloud_records
            SET NextRetryTime = $nextRetryTime,
                CreatedAt = $createdAt
            WHERE FailedTarget = $failedTarget";
        command.Parameters.AddWithValue("$nextRetryTime", nextRetryTime);
        command.Parameters.AddWithValue("$createdAt", createdAt);
        command.Parameters.AddWithValue("$failedTarget", failedTarget);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<int> CountByFailedTargetAsync(SqliteConnectionFactory connectionFactory, string failedTarget)
    {
        using var connection = (SqliteConnection)connectionFactory.Create("pipeline_cloud");
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM failed_cloud_records WHERE FailedTarget = $failedTarget";
        command.Parameters.AddWithValue("$failedTarget", failedTarget);
        var scalar = await command.ExecuteScalarAsync();
        return Convert.ToInt32(scalar);
    }

    private static async Task<int> CountTableRowsAsync(SqliteConnectionFactory connectionFactory, string tableName)
    {
        using var connection = (SqliteConnection)connectionFactory.Create("pipeline_cloud");
        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT COUNT(*) FROM {tableName}";
        var scalar = await command.ExecuteScalarAsync();
        return Convert.ToInt32(scalar);
    }
}
