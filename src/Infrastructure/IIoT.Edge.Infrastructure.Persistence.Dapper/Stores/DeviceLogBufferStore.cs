using Dapper;
using IIoT.Edge.Application.Abstractions.DataPipeline.Stores;
using IIoT.Edge.Application.Abstractions.Logging;
using IIoT.Edge.Infrastructure.Persistence.Dapper.Connection;
using IIoT.Edge.Infrastructure.Persistence.Dapper.Repository;
using IIoT.Edge.SharedKernel.DataPipeline.DeviceLog;

namespace IIoT.Edge.Infrastructure.Persistence.Dapper.Stores;

public class DeviceLogBufferStore : DapperRepositoryBase<DeviceLogRecord>, IDeviceLogBufferStore
{
    private static readonly TimeSpan ClaimTimeout = TimeSpan.FromMinutes(10);

    public override string DbName => "pipeline_cloud";
    protected override string TableName => "device_log_buffer";

    protected override string CreateTableSql => @"
        CREATE TABLE IF NOT EXISTS device_log_buffer (
            Id          INTEGER PRIMARY KEY AUTOINCREMENT,
            Level       TEXT    NOT NULL,
            Message     TEXT    NOT NULL,
            LogTime     TEXT    NOT NULL,
            CreatedAt   TEXT    NOT NULL
        );
        CREATE INDEX IF NOT EXISTS idx_device_log_buffer_id
            ON device_log_buffer (Id);

        CREATE TABLE IF NOT EXISTS device_log_buffer_claims (
            RecordId    INTEGER PRIMARY KEY,
            ClaimToken  TEXT    NOT NULL,
            ClaimedAt   TEXT    NOT NULL
        );
        CREATE INDEX IF NOT EXISTS idx_device_log_claim_token
            ON device_log_buffer_claims (ClaimToken);
        CREATE INDEX IF NOT EXISTS idx_device_log_claim_time
            ON device_log_buffer_claims (ClaimedAt);
    ";

    public DeviceLogBufferStore(
        SqliteConnectionFactory connectionFactory,
        ILogService logger)
        : base(connectionFactory, logger)
    {
    }

    public async Task SaveBatchAsync(IEnumerable<DeviceLogRecord> records)
    {
        const string sql = @"
            INSERT INTO device_log_buffer (Level, Message, LogTime, CreatedAt)
            VALUES (@Level, @Message, @LogTime, @CreatedAt)";

        var rows = records.ToList();
        if (rows.Count == 0)
        {
            return;
        }

        await ExecuteInTransactionAsync<int>(async (conn, tx) =>
        {
            await conn.ExecuteAsync(sql, rows, tx, commandTimeout: CommandTimeout);
            return rows.Count;
        });
    }

    public async Task<List<DeviceLogRecord>> GetPendingAsync(int batchSize = 100)
    {
        var sql = $@"
            SELECT * FROM {TableName}
            ORDER BY Id ASC
            LIMIT @BatchSize";

        var result = await SafeQueryAsync(sql, new { BatchSize = batchSize });
        return result.ToList();
    }

    public async Task<ClaimedDeviceLogBatch?> ClaimPendingBatchAsync(int batchSize = 100)
    {
        return await ExecuteInTransactionAsync<ClaimedDeviceLogBatch?>(async (conn, tx) =>
        {
            var now = DateTime.UtcNow;
            await conn.ExecuteAsync(
                "DELETE FROM device_log_buffer_claims WHERE ClaimedAt <= @ExpiredAt",
                new { ExpiredAt = now.Subtract(ClaimTimeout).ToString("O") },
                tx,
                commandTimeout: CommandTimeout);

            var ids = (await conn.QueryAsync<long>(
                @"
                SELECT b.Id
                FROM device_log_buffer b
                LEFT JOIN device_log_buffer_claims c ON c.RecordId = b.Id
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
                "INSERT INTO device_log_buffer_claims (RecordId, ClaimToken, ClaimedAt) VALUES (@RecordId, @ClaimToken, @ClaimedAt)",
                claimRows,
                tx,
                commandTimeout: CommandTimeout);

            var records = (await conn.QueryAsync<DeviceLogRecord>(
                @"
                SELECT b.*
                FROM device_log_buffer b
                INNER JOIN device_log_buffer_claims c ON c.RecordId = b.Id
                WHERE c.ClaimToken = @ClaimToken
                ORDER BY b.Id ASC",
                new { ClaimToken = claimToken },
                tx,
                commandTimeout: CommandTimeout)).ToList();

            if (records.Count == 0)
            {
                await conn.ExecuteAsync(
                    "DELETE FROM device_log_buffer_claims WHERE ClaimToken = @ClaimToken",
                    new { ClaimToken = claimToken },
                    tx,
                    commandTimeout: CommandTimeout);
                return null;
            }

            return new ClaimedDeviceLogBatch
            {
                ClaimToken = claimToken,
                Records = records
            };
        });
    }

    public async Task DeleteClaimedBatchAsync(string claimToken)
    {
        await ExecuteInTransactionAsync<int>(async (conn, tx) =>
        {
            var ids = (await conn.QueryAsync<long>(
                "SELECT RecordId FROM device_log_buffer_claims WHERE ClaimToken = @ClaimToken",
                new { ClaimToken = claimToken },
                tx,
                commandTimeout: CommandTimeout)).ToList();

            if (ids.Count == 0)
            {
                throw new InvalidOperationException($"No claimed device log rows found for claim {claimToken}.");
            }

            await conn.ExecuteAsync(
                "DELETE FROM device_log_buffer WHERE Id IN @Ids",
                new { Ids = ids },
                tx,
                commandTimeout: CommandTimeout);

            await conn.ExecuteAsync(
                "DELETE FROM device_log_buffer_claims WHERE RecordId IN @Ids",
                new { Ids = ids },
                tx,
                commandTimeout: CommandTimeout);

            return ids.Count;
        });
    }

    public async Task ReleaseClaimAsync(string claimToken)
        => await StrictExecuteAsync(
            "DELETE FROM device_log_buffer_claims WHERE ClaimToken = @ClaimToken",
            new { ClaimToken = claimToken },
            requireAffectedRows: true,
            failureMessage: $"Failed to release device log claim {claimToken}.");

    public async Task DeleteBatchAsync(IEnumerable<long> ids)
    {
        var idList = ids.ToList();
        if (idList.Count == 0)
        {
            return;
        }

        foreach (var batch in ChunkBy(idList, 500))
        {
            await SafeExecuteAsync($"DELETE FROM {TableName} WHERE Id IN @Ids", new { Ids = batch });
        }
    }

    public async Task<int> GetCountAsync()
        => await SafeCountAsync($"SELECT COUNT(*) FROM {TableName}");

    private static IEnumerable<List<T>> ChunkBy<T>(List<T> source, int chunkSize)
    {
        for (var i = 0; i < source.Count; i += chunkSize)
        {
            yield return source.GetRange(i, Math.Min(chunkSize, source.Count - i));
        }
    }
}
