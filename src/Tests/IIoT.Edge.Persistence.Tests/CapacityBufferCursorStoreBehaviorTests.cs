using IIoT.Edge.Application.Common.DataPipeline;
using IIoT.Edge.Infrastructure.Persistence.Dapper.Connection;
using IIoT.Edge.Infrastructure.Persistence.Dapper.Stores;
using IIoT.Edge.Module.Contracts.DataPipeline.Capacity;
using Microsoft.Data.Sqlite;

namespace IIoT.Edge.Persistence.Tests;

public sealed class CapacityBufferCursorStoreBehaviorTests
{
    [Fact]
    public async Task CursorClaim_WhenRawRowsCollapseIntoSummary_ShouldAdvanceByRawRecordId()
    {
        var tempDirectory = Path.Combine(
            Path.GetTempPath(),
            "edge-capacity-cursor-tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDirectory);

        try
        {
            var store = new CapacityBufferStore(
                new SqliteConnectionFactory(tempDirectory),
                new FakeLogService());
            using (var connection = new SqliteConnectionFactory(tempDirectory).Create(store.DbName))
            {
                await store.InitializeTableAsync(connection);
            }

            var completedAt = new DateTime(2026, 7, 31, 8, 0, 0, DateTimeKind.Utc);
            await store.SaveBatchAsync(
                Enumerable.Range(0, 200)
                    .Select(index => CreateRecord($"BLOCKED-{index:D3}", "历史名称", completedAt))
                    .Concat(
                    [
                        CreateRecord("VALID-001", "P1-AP01", completedAt.AddHours(1)),
                        CreateRecord("VALID-002", "P1-AP01", completedAt.AddHours(1))
                    ]));

            var first = Assert.IsType<ClaimedCapacityBufferCursorBatch>(
                await store.ClaimHourlySummaryBatchAfterAsync(0, 200));
            Assert.Equal(200, first.ClaimedRecordCount);
            Assert.Equal(200, first.LastRecordId);
            Assert.Equal(200, Assert.Single(first.Summaries).Total);
            await store.ReleaseClaimAsync(first.ClaimToken);

            var second = Assert.IsType<ClaimedCapacityBufferCursorBatch>(
                await store.ClaimHourlySummaryBatchAfterAsync(first.LastRecordId, 200));
            Assert.Equal(2, second.ClaimedRecordCount);
            Assert.Equal(202, second.LastRecordId);
            Assert.Equal(2, Assert.Single(second.Summaries).Total);
            await store.ReleaseClaimAsync(second.ClaimToken);

            Assert.Null(await store.ClaimHourlySummaryBatchAfterAsync(second.LastRecordId, 200));
            var wrapped = Assert.IsType<ClaimedCapacityBufferCursorBatch>(
                await store.ClaimHourlySummaryBatchAfterAsync(0, 200));
            Assert.Equal(200, wrapped.LastRecordId);
            await store.ReleaseClaimAsync(wrapped.ClaimToken);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            Directory.Delete(tempDirectory, recursive: true);
        }
    }

    private static CapacityRecord CreateRecord(
        string barcode,
        string plcName,
        DateTime completedAt)
        => new()
        {
            Barcode = barcode,
            CellResult = true,
            ShiftCode = "D",
            CompletedTime = completedAt,
            CreatedAt = completedAt,
            PlcName = plcName
        };
}
