using Dapper;
using IIoT.Edge.Application.Abstractions.DataPipeline.Stores;
using IIoT.Edge.Application.Abstractions.Logging;
using IIoT.Edge.Infrastructure.Persistence.Dapper.Connection;
using IIoT.Edge.Infrastructure.Persistence.Dapper.Repository;
using IIoT.Edge.SharedKernel.DataPipeline.Capacity;

namespace IIoT.Edge.Infrastructure.Persistence.Dapper.Stores;

public class CapacityBufferStore : DapperRepositoryBase<CapacityRecord>, ICapacityBufferStore
{
    private static readonly TimeSpan ClaimTimeout = TimeSpan.FromMinutes(10);

    public override string DbName => "pipeline_cloud";
    protected override string TableName => "capacity_buffer";

    protected override string CreateTableSql => @"
        CREATE TABLE IF NOT EXISTS capacity_buffer (
            Id            INTEGER PRIMARY KEY AUTOINCREMENT,
            Barcode       TEXT    NOT NULL,
            CellResult    INTEGER NOT NULL,
            ShiftCode     TEXT    NOT NULL,
            CompletedTime TEXT    NOT NULL,
            CreatedAt     TEXT    NOT NULL,
            PlcName       TEXT    NOT NULL DEFAULT ''
        );
        CREATE INDEX IF NOT EXISTS idx_buffer_completed
            ON capacity_buffer (CompletedTime);
        CREATE INDEX IF NOT EXISTS idx_buffer_plcname
            ON capacity_buffer (PlcName);

        CREATE TABLE IF NOT EXISTS capacity_buffer_claims (
            RecordId    INTEGER PRIMARY KEY,
            ClaimToken  TEXT    NOT NULL,
            ClaimedAt   TEXT    NOT NULL
        );
        CREATE INDEX IF NOT EXISTS idx_capacity_buffer_claim_token
            ON capacity_buffer_claims (ClaimToken);
        CREATE INDEX IF NOT EXISTS idx_capacity_buffer_claim_time
            ON capacity_buffer_claims (ClaimedAt);
    ";

    public CapacityBufferStore(
        SqliteConnectionFactory connectionFactory,
        ILogService logger)
        : base(connectionFactory, logger)
    {
    }

    public async Task SaveAsync(CapacityRecord record)
    {
        const string sql = @"
            INSERT INTO capacity_buffer
                (Barcode, CellResult, ShiftCode, CompletedTime, CreatedAt, PlcName)
            VALUES
                (@Barcode, @CellResult, @ShiftCode, @CompletedTime, @CreatedAt, @PlcName)";

        await SafeExecuteAsync(sql, new
        {
            record.Barcode,
            record.CellResult,
            CompletedTime = record.CompletedTime.ToString("O"),
            record.ShiftCode,
            CreatedAt = DateTime.UtcNow.ToString("O"),
            record.PlcName
        });
    }

    public async Task SaveBatchAsync(IEnumerable<CapacityRecord> records)
    {
        const string sql = @"
            INSERT INTO capacity_buffer
                (Barcode, CellResult, ShiftCode, CompletedTime, CreatedAt, PlcName)
            VALUES
                (@Barcode, @CellResult, @ShiftCode, @CompletedTime, @CreatedAt, @PlcName)";

        var now = DateTime.UtcNow.ToString("O");
        var rows = records.Select(r => new
        {
            r.Barcode,
            r.CellResult,
            r.ShiftCode,
            CompletedTime = r.CompletedTime.ToString("O"),
            CreatedAt = now,
            r.PlcName
        }).ToList();

        if (rows.Count == 0)
        {
            return;
        }

        await ExecuteInTransactionAsync<int>(async (conn, tx) =>
        {
            await conn.ExecuteAsync(sql, rows, tx, commandTimeout: CommandTimeout);
            return rows.Count;
        });

        Logger.Info($"[CapacityBuffer] Batch saved: {rows.Count} row(s).");
    }

    public async Task<List<BufferSummaryDto>> GetShiftSummaryAsync()
    {
        const string sql = @"
            SELECT
                substr(CompletedTime, 1, 10)                     AS Date,
                ShiftCode,
                COUNT(*)                                         AS Total,
                SUM(CASE WHEN CellResult = 1 THEN 1 ELSE 0 END) AS OkCount,
                SUM(CASE WHEN CellResult = 0 THEN 1 ELSE 0 END) AS NgCount
            FROM capacity_buffer
            GROUP BY substr(CompletedTime, 1, 10), ShiftCode
            ORDER BY Date ASC, ShiftCode ASC";

        return await SafeQueryListAsync<BufferSummaryDto>(sql);
    }

    public async Task<List<BufferHourlySummaryDto>> GetHourlySummaryAsync()
    {
        const string sql = @"
            SELECT
                substr(CompletedTime, 1, 10)                          AS Date,
                CAST(substr(CompletedTime, 12, 2) AS INTEGER)         AS Hour,
                CASE
                    WHEN CAST(substr(CompletedTime, 15, 2) AS INTEGER) >= 30
                    THEN 30 ELSE 0
                END                                                   AS MinuteBucket,
                ShiftCode,
                PlcName,
                COUNT(*)                                              AS Total,
                SUM(CASE WHEN CellResult = 1 THEN 1 ELSE 0 END)       AS OkCount,
                SUM(CASE WHEN CellResult = 0 THEN 1 ELSE 0 END)       AS NgCount
            FROM capacity_buffer
            GROUP BY
                substr(CompletedTime, 1, 10),
                CAST(substr(CompletedTime, 12, 2) AS INTEGER),
                CASE
                    WHEN CAST(substr(CompletedTime, 15, 2) AS INTEGER) >= 30
                    THEN 30 ELSE 0
                END,
                ShiftCode,
                PlcName
            ORDER BY Date ASC, Hour ASC, MinuteBucket ASC, ShiftCode ASC";

        return await SafeQueryListAsync<BufferHourlySummaryDto>(sql);
    }

    public async Task<ClaimedCapacityBufferBatch?> ClaimHourlySummaryBatchAsync(int batchSize = 200)
    {
        return await ExecuteInTransactionAsync<ClaimedCapacityBufferBatch?>(async (conn, tx) =>
        {
            var now = DateTime.UtcNow;
            await conn.ExecuteAsync(
                "DELETE FROM capacity_buffer_claims WHERE ClaimedAt <= @ExpiredAt",
                new { ExpiredAt = now.Subtract(ClaimTimeout).ToString("O") },
                tx,
                commandTimeout: CommandTimeout);

            var ids = (await conn.QueryAsync<long>(
                @"
                SELECT b.Id
                FROM capacity_buffer b
                LEFT JOIN capacity_buffer_claims c ON c.RecordId = b.Id
                WHERE c.RecordId IS NULL
                ORDER BY b.Id ASC
                LIMIT @BatchSize",
                new { BatchSize = batchSize },
                tx,
                commandTimeout: CommandTimeout)).ToList();

            if (ids.Count == 0)
            {
                return null;
            }

            var claimToken = Guid.NewGuid().ToString("N");
            var claimRows = ids.Select(id => new
            {
                RecordId = id,
                ClaimToken = claimToken,
                ClaimedAt = now.ToString("O")
            });

            await conn.ExecuteAsync(
                "INSERT INTO capacity_buffer_claims (RecordId, ClaimToken, ClaimedAt) VALUES (@RecordId, @ClaimToken, @ClaimedAt)",
                claimRows,
                tx,
                commandTimeout: CommandTimeout);

            var summaries = (await conn.QueryAsync<BufferHourlySummaryDto>(
                @"
                SELECT
                    substr(b.CompletedTime, 1, 10)                          AS Date,
                    CAST(substr(b.CompletedTime, 12, 2) AS INTEGER)         AS Hour,
                    CASE
                        WHEN CAST(substr(b.CompletedTime, 15, 2) AS INTEGER) >= 30
                        THEN 30 ELSE 0
                    END                                                     AS MinuteBucket,
                    b.ShiftCode,
                    b.PlcName,
                    COUNT(*)                                                AS Total,
                    SUM(CASE WHEN b.CellResult = 1 THEN 1 ELSE 0 END)       AS OkCount,
                    SUM(CASE WHEN b.CellResult = 0 THEN 1 ELSE 0 END)       AS NgCount
                FROM capacity_buffer b
                INNER JOIN capacity_buffer_claims c ON c.RecordId = b.Id
                WHERE c.ClaimToken = @ClaimToken
                GROUP BY
                    substr(b.CompletedTime, 1, 10),
                    CAST(substr(b.CompletedTime, 12, 2) AS INTEGER),
                    CASE
                        WHEN CAST(substr(b.CompletedTime, 15, 2) AS INTEGER) >= 30
                        THEN 30 ELSE 0
                    END,
                    b.ShiftCode,
                    b.PlcName
                ORDER BY Date ASC, Hour ASC, MinuteBucket ASC, b.ShiftCode ASC",
                new { ClaimToken = claimToken },
                tx,
                commandTimeout: CommandTimeout)).ToList();

            if (summaries.Count == 0)
            {
                await conn.ExecuteAsync(
                    "DELETE FROM capacity_buffer_claims WHERE ClaimToken = @ClaimToken",
                    new { ClaimToken = claimToken },
                    tx,
                    commandTimeout: CommandTimeout);
                return null;
            }

            return new ClaimedCapacityBufferBatch
            {
                ClaimToken = claimToken,
                Summaries = summaries
            };
        });
    }

    public async Task DeleteClaimedSummaryAsync(string claimToken, string date, int hour, int minuteBucket, string shiftCode, string plcName)
    {
        await ExecuteInTransactionAsync<int>(async (conn, tx) =>
        {
            var ids = (await conn.QueryAsync<long>(
                @"
                SELECT b.Id
                FROM capacity_buffer b
                INNER JOIN capacity_buffer_claims c ON c.RecordId = b.Id
                WHERE c.ClaimToken = @ClaimToken
                  AND substr(b.CompletedTime, 1, 10) = @Date
                  AND CAST(substr(b.CompletedTime, 12, 2) AS INTEGER) = @Hour
                  AND CASE
                          WHEN CAST(substr(b.CompletedTime, 15, 2) AS INTEGER) >= 30
                          THEN 30 ELSE 0
                      END = @MinuteBucket
                  AND b.ShiftCode = @ShiftCode
                  AND b.PlcName = @PlcName",
                new
                {
                    ClaimToken = claimToken,
                    Date = date,
                    Hour = hour,
                    MinuteBucket = minuteBucket,
                    ShiftCode = shiftCode,
                    PlcName = plcName
                },
                tx,
                commandTimeout: CommandTimeout)).ToList();

            if (ids.Count == 0)
            {
                throw new InvalidOperationException($"No claimed capacity rows found for claim {claimToken}.");
            }

            await conn.ExecuteAsync(
                "DELETE FROM capacity_buffer WHERE Id IN @Ids",
                new { Ids = ids },
                tx,
                commandTimeout: CommandTimeout);

            await conn.ExecuteAsync(
                "DELETE FROM capacity_buffer_claims WHERE RecordId IN @Ids",
                new { Ids = ids },
                tx,
                commandTimeout: CommandTimeout);

            return ids.Count;
        });
    }

    public async Task ReleaseClaimAsync(string claimToken)
        => await StrictExecuteAsync(
            "DELETE FROM capacity_buffer_claims WHERE ClaimToken = @ClaimToken",
            new { ClaimToken = claimToken },
            requireAffectedRows: true,
            failureMessage: $"Failed to release capacity claim {claimToken}.");

    public async Task ClearAllAsync()
    {
        await SafeExecuteAsync("DELETE FROM capacity_buffer_claims");
        await SafeExecuteAsync($"DELETE FROM {TableName}");
    }

    public async Task<int> GetCountAsync()
        => await SafeCountAsync($"SELECT COUNT(*) FROM {TableName}");
}
