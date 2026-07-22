using Dapper;
using IIoT.Edge.Module.Contracts.DataPipeline.Stores;
using IIoT.Edge.Module.Contracts.Logging;
using IIoT.Edge.Infrastructure.Persistence.Dapper.Connection;
using IIoT.Edge.Module.Contracts.DataPipeline.DeviceLog;

namespace IIoT.Edge.Infrastructure.Persistence.Dapper.Stores;

public class DeviceLogBufferStore : ClaimBufferStoreBase<DeviceLogRecord>, IDeviceLogBufferStore
{
    private const string ClaimTableName = "device_log_buffer_claims";

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
        return await SafeQueryByIdAscendingAsync<DeviceLogRecord>(batchSize).ConfigureAwait(false);
    }

    public async Task<ClaimedDeviceLogBatch?> ClaimPendingBatchAsync(int batchSize = 100)
    {
        return await ClaimBatchCoreAsync<DeviceLogRecord, ClaimedDeviceLogBatch>(
            ClaimTableName,
            batchSize,
            (conn, tx, size, _) => SelectUnclaimedIdsByIdAscendingAsync(conn, tx, ClaimTableName, size),
            async (conn, tx, claimToken) => (await conn.QueryAsync<DeviceLogRecord>(
                @"
                SELECT b.*
                FROM device_log_buffer b
                INNER JOIN device_log_buffer_claims c ON c.RecordId = b.Id
                WHERE c.ClaimToken = @ClaimToken
                ORDER BY b.Id ASC",
                new { ClaimToken = claimToken },
                tx,
                commandTimeout: CommandTimeout)).ToList(),
            (claimToken, records) => new ClaimedDeviceLogBatch
            {
                ClaimToken = claimToken,
                Records = records
            }).ConfigureAwait(false);
    }

    public async Task DeleteClaimedBatchAsync(string claimToken)
    {
        await DeleteClaimedRowsByClaimAsync(
            ClaimTableName,
            claimToken,
            $"未找到领取标记 {claimToken} 对应的设备日志记录。").ConfigureAwait(false);
    }

    public async Task ReleaseClaimAsync(string claimToken)
        => await ReleaseClaimCoreAsync(
            ClaimTableName,
            claimToken,
            $"释放设备日志领取标记 {claimToken} 失败。");

    public async Task DeleteBatchAsync(IEnumerable<long> ids)
    {
        await DeleteRowsByIdsInChunksAsync(ids).ConfigureAwait(false);
    }

    public async Task<int> GetCountAsync()
        => await SafeCountAsync($"SELECT COUNT(*) FROM {TableName}");
}
