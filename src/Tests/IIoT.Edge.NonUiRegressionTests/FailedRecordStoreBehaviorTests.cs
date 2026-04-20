using IIoT.Edge.Application.Common.Persistence;
using IIoT.Edge.Infrastructure.Persistence.Dapper.Connection;
using IIoT.Edge.Infrastructure.Persistence.Dapper.Stores;
using IIoT.Edge.Module.Injection.Payload;
using IIoT.Edge.SharedKernel.DataPipeline;
using IIoT.Edge.SharedKernel.DataPipeline.CellData;
using Microsoft.Data.Sqlite;

namespace IIoT.Edge.NonUiRegressionTests;

public sealed class FailedRecordStoreBehaviorTests
{
    [Fact]
    public async Task GetPendingAsync_WhenDatabaseOpenFails_ShouldThrowPersistenceAccessException()
    {
        CellDataTypeRegistry.Register<InjectionCellData>("Injection");

        var tempDir = Path.Combine(Path.GetTempPath(), "edge-failed-store-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try
        {
            Directory.CreateDirectory(Path.Combine(tempDir, "pipeline_cloud.db"));

            var logger = new FakeLogService();
            var connectionFactory = new SqliteConnectionFactory(tempDir);
            var store = new CloudRetryRecordStore(connectionFactory, logger);

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
        CellDataTypeRegistry.Register<InjectionCellData>("Injection");

        var tempDir = Path.Combine(Path.GetTempPath(), "edge-failed-store-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try
        {
            var logger = new FakeLogService();
            var connectionFactory = new SqliteConnectionFactory(tempDir);
            var store = new CloudRetryRecordStore(connectionFactory, logger);

            using (var connection = connectionFactory.Create(store.DbName))
            {
                await store.InitializeTableAsync(connection);
            }

            await store.SaveAsync(CreateRecord("OLD"), "Cloud-Old", "seed");
            await store.SaveAsync(CreateRecord("RECENT"), "Cloud-Recent", "seed");
            await store.SaveAsync(CreateRecord("ACTIVE"), "Cloud-Active", "seed");

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
        CellDataTypeRegistry.Register<InjectionCellData>("Injection");

        var tempDir = Path.Combine(Path.GetTempPath(), "edge-failed-store-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try
        {
            var logger = new FakeLogService();
            var connectionFactory = new SqliteConnectionFactory(tempDir);
            var store = new CloudRetryRecordStore(connectionFactory, logger);

            using (var connection = connectionFactory.Create(store.DbName))
            {
                await store.InitializeTableAsync(connection);
            }

            await store.SaveAsync(CreateRecord("CLAIM-A"), "Cloud-Claim-A", "seed");
            await store.SaveAsync(CreateRecord("CLAIM-B"), "Cloud-Claim-B", "seed");

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
        CellDataTypeRegistry.Register<InjectionCellData>("Injection");

        var tempDir = Path.Combine(Path.GetTempPath(), "edge-failed-store-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try
        {
            var logger = new FakeLogService();
            var connectionFactory = new SqliteConnectionFactory(tempDir);
            var retryStore = new CloudRetryRecordStore(connectionFactory, logger);
            var fallbackStore = new CloudFallbackBufferStore(connectionFactory, logger);

            using (var connection = connectionFactory.Create(retryStore.DbName))
            {
                await retryStore.InitializeTableAsync(connection);
                await fallbackStore.InitializeTableAsync(connection);
            }

            await fallbackStore.SaveAsync(CreateRecord("MOVE-1"), "Cloud-Move", "seed");
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

    private static CellCompletedRecord CreateRecord(string barcode)
    {
        return new CellCompletedRecord
        {
            CellData = new InjectionCellData
            {
                Barcode = barcode,
                WorkOrderNo = $"WO-{barcode}",
                CompletedTime = DateTime.UtcNow
            }
        };
    }

    private static async Task UpdateFailedRecordAsync(
        SqliteConnectionFactory connectionFactory,
        string failedTarget,
        string nextRetryTime,
        string createdAt)
    {
        await using var connection = (SqliteConnection)await connectionFactory.CreateAsync("pipeline_cloud");
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
        await using var connection = (SqliteConnection)await connectionFactory.CreateAsync("pipeline_cloud");
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM failed_cloud_records WHERE FailedTarget = $failedTarget";
        command.Parameters.AddWithValue("$failedTarget", failedTarget);
        var scalar = await command.ExecuteScalarAsync();
        return Convert.ToInt32(scalar);
    }

    private static async Task<int> CountTableRowsAsync(SqliteConnectionFactory connectionFactory, string tableName)
    {
        await using var connection = (SqliteConnection)await connectionFactory.CreateAsync("pipeline_cloud");
        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT COUNT(*) FROM {tableName}";
        var scalar = await command.ExecuteScalarAsync();
        return Convert.ToInt32(scalar);
    }
}
