using Dapper;
using IIoT.Edge.Module.Contracts.DataPipeline.Stores;
using IIoT.Edge.Module.Contracts.Logging;
using IIoT.Edge.Infrastructure.Persistence.Dapper.Connection;
using IIoT.Edge.Module.Contracts.DataPipeline;
using IIoT.Edge.Module.Contracts.DataPipeline.CellData;
using System.Data;

namespace IIoT.Edge.Infrastructure.Persistence.Dapper.Stores;

public abstract class RetryRecordStoreBase : ClaimBufferStoreBase<FailedCellRecord>
{
    private static readonly DateTime AbandonedRetryTimeUtc = DateTime.SpecifyKind(DateTime.MaxValue, DateTimeKind.Utc);
    private readonly ICellDataJsonSerializer _cellDataJsonSerializer;

    protected abstract string ChannelName { get; }
    protected abstract string ClaimTableName { get; }

    protected string ChannelDisplayName
        => ChannelName switch
        {
            "Cloud" => "云端",
            "MES" => "MES",
            _ => ChannelName
        };

    protected RetryRecordStoreBase(
        SqliteConnectionFactory connectionFactory,
        ILogService logger,
        ICellDataJsonSerializer cellDataJsonSerializer)
        : base(connectionFactory, logger)
    {
        _cellDataJsonSerializer = cellDataJsonSerializer;
    }

    public async Task SaveAsync(
        CellCompletedRecord record,
        string failedTarget,
        string errorMessage,
        CancellationToken cancellationToken = default)
    {
        var cellData = record.CellData;
        var cellDataJson = _cellDataJsonSerializer.Serialize(cellData);
        await SaveRawCoreAsync(
            cellData.ProcessType,
            cellDataJson,
            failedTarget,
            errorMessage,
            CreateContextRow(record),
            cancellationToken).ConfigureAwait(false);
    }

    public Task SaveRawAsync(
        string processType,
        string cellDataJson,
        string failedTarget,
        string errorMessage)
        => SaveRawCoreAsync(
            processType,
            cellDataJson,
            failedTarget,
            errorMessage,
            DataPipelineContextRow.Empty);

    protected Task SaveRequeuedCoreAsync(DeadLetterRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);
        return SaveRawCoreAsync(
            record.ProcessType,
            record.CellDataJson,
            record.FailedTarget,
            $"manual_requeue:{record.FailureStage}:{record.FailureReason}",
            new DataPipelineContextRow(
                record.PlcCode,
                record.IdempotencyKeyVersion,
                record.NetworkDeviceId,
                record.DeviceName,
                record.ModuleId,
                record.TaskKey,
                record.PlanSessionId,
                record.MainPlanCode,
                record.TraceBatchNumber));
    }

    private async Task SaveRawCoreAsync(
        string processType,
        string cellDataJson,
        string failedTarget,
        string errorMessage,
        DataPipelineContextRow context,
        CancellationToken cancellationToken = default)
    {
        var nowUtc = DateTime.UtcNow;

        var sql = $@"
            INSERT INTO {TableName}
                (ProcessType, CellDataJson, FailedTarget, ErrorMessage,
                 RetryCount, NextRetryTime, CreatedAt,
                 PlcCode, IdempotencyKeyVersion,
                 NetworkDeviceId, DeviceName, ModuleId, TaskKey, PlanSessionId, MainPlanCode, TraceBatchNumber)
            VALUES
                (@ProcessType, @CellDataJson, @FailedTarget, @ErrorMessage,
                 0, @NextRetryTime, @CreatedAt,
                 @PlcCode, @IdempotencyKeyVersion,
                 @NetworkDeviceId, @DeviceName, @ModuleId, @TaskKey, @PlanSessionId, @MainPlanCode, @TraceBatchNumber)";

        var affectedRows = await SafeExecuteAsync(sql, new
        {
            ProcessType = processType,
            CellDataJson = cellDataJson,
            FailedTarget = failedTarget,
            ErrorMessage = errorMessage,
            NextRetryTime = nowUtc.AddSeconds(30).ToString("O"),
            CreatedAt = nowUtc.ToString("O"),
            context.PlcCode,
            IdempotencyKeyVersion = (int)context.IdempotencyKeyVersion,
            context.NetworkDeviceId,
            context.DeviceName,
            context.ModuleId,
            context.TaskKey,
            context.PlanSessionId,
            context.MainPlanCode,
            context.TraceBatchNumber
        }, cancellationToken).ConfigureAwait(false);

        if (affectedRows <= 0)
        {
            throw new InvalidOperationException($"持久化 {ChannelDisplayName} 补传记录失败。");
        }
    }

    public async Task<List<FailedCellRecord>> GetPendingAsync(int batchSize = 10)
    {
        var sql = $@"
            SELECT
                Id,
                @Channel AS Channel,
                ProcessType,
                CellDataJson,
                FailedTarget,
                ErrorMessage,
                RetryCount,
                NextRetryTime,
                CreatedAt,
                PlcCode,
                IdempotencyKeyVersion,
                NetworkDeviceId,
                DeviceName,
                ModuleId,
                TaskKey,
                PlanSessionId,
                MainPlanCode,
                TraceBatchNumber
            FROM {TableName}
            WHERE NextRetryTime <= @Now
              AND TRIM(PlcCode) <> ''
              AND IdempotencyKeyVersion IN (1, 2)
            ORDER BY NextRetryTime ASC
            LIMIT @BatchSize";

        var result = await SafeQueryAsync(sql, new
        {
            Channel = ChannelName,
            Now = DateTime.UtcNow.ToString("O"),
            BatchSize = batchSize
        });

        return result.ToList();
    }

    public async Task<ClaimedFailedCellBatch?> ClaimPendingBatchAsync(int batchSize = 10)
    {
        return await ClaimBatchCoreAsync<FailedCellRecord, ClaimedFailedCellBatch>(
            ClaimTableName,
            batchSize,
            async (conn, tx, size, nowUtc) => (await conn.QueryAsync<long>(
                $@"
                SELECT r.Id
                FROM {TableName} r
                LEFT JOIN {ClaimTableName} c ON c.RecordId = r.Id
                WHERE c.RecordId IS NULL
                  AND r.NextRetryTime <= @Now
                  AND TRIM(r.PlcCode) <> ''
                  AND r.IdempotencyKeyVersion IN (1, 2)
                ORDER BY r.NextRetryTime ASC, r.Id ASC
                LIMIT @BatchSize",
                new
                {
                    Now = nowUtc.ToString("O"),
                    BatchSize = size
                },
                tx,
                commandTimeout: CommandTimeout)).ToList(),
            async (conn, tx, claimToken) => (await conn.QueryAsync<FailedCellRecord>(
                $@"
                SELECT
                    r.Id,
                    @Channel AS Channel,
                    r.ProcessType,
                    r.CellDataJson,
                    r.FailedTarget,
                    r.ErrorMessage,
                    r.RetryCount,
                    r.NextRetryTime,
                    r.CreatedAt,
                    r.PlcCode,
                    r.IdempotencyKeyVersion,
                    r.NetworkDeviceId,
                    r.DeviceName,
                    r.ModuleId,
                    r.TaskKey,
                    r.PlanSessionId,
                    r.MainPlanCode,
                    r.TraceBatchNumber
                FROM {TableName} r
                INNER JOIN {ClaimTableName} c ON c.RecordId = r.Id
                WHERE c.ClaimToken = @ClaimToken
                ORDER BY r.NextRetryTime ASC, r.Id ASC",
                new
                {
                    Channel = ChannelName,
                    ClaimToken = claimToken
                },
                tx,
                commandTimeout: CommandTimeout)).ToList(),
            (claimToken, records) => new ClaimedFailedCellBatch
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
            $"未找到领取标记 {claimToken} 对应的 {ChannelDisplayName} 补传记录。").ConfigureAwait(false);
    }

    public async Task ReleaseClaimAsync(string claimToken)
    {
        await ReleaseClaimCoreAsync(
            ClaimTableName,
            claimToken,
            $"释放 {ChannelDisplayName} 补传领取标记 {claimToken} 失败。").ConfigureAwait(false);
    }

    public async Task UpdateRetryAsync(
        long id,
        int retryCount,
        string errorMessage,
        DateTime nextRetryTime)
    {
        await ExecuteInTransactionAsync<int>(async (conn, tx) =>
        {
            var sql = $@"
                UPDATE {TableName}
                SET RetryCount = @RetryCount,
                    ErrorMessage = @ErrorMessage,
                    NextRetryTime = @NextRetryTime
                WHERE Id = @Id";

            var affectedRows = await conn.ExecuteAsync(
                sql,
                new
                {
                    Id = id,
                    RetryCount = retryCount,
                    ErrorMessage = errorMessage,
                    NextRetryTime = EnsureUtc(nextRetryTime).ToString("O")
                },
                tx,
                commandTimeout: CommandTimeout);

            if (affectedRows <= 0)
            {
                throw new InvalidOperationException($"更新 {ChannelDisplayName} 补传记录 {id} 的重试元数据失败。");
            }

            await conn.ExecuteAsync(
                $"DELETE FROM {ClaimTableName} WHERE RecordId = @Id",
                new { Id = id },
                tx,
                commandTimeout: CommandTimeout);

            return affectedRows;
        }).ConfigureAwait(false);
    }

    public async Task DeleteAsync(long id)
    {
        await ExecuteInTransactionAsync<int>(async (conn, tx) =>
        {
            await conn.ExecuteAsync(
                $"DELETE FROM {ClaimTableName} WHERE RecordId = @Id",
                new { Id = id },
                tx,
                commandTimeout: CommandTimeout);

            var affectedRows = await conn.ExecuteAsync(
                $"DELETE FROM {TableName} WHERE Id = @Id",
                new { Id = id },
                tx,
                commandTimeout: CommandTimeout);

            if (affectedRows <= 0)
            {
                throw new InvalidOperationException($"删除 {ChannelName} 补传记录 {id} 失败。");
            }

            return affectedRows;
        }).ConfigureAwait(false);
    }

    public async Task<int> GetCountAsync()
        => await SafeCountAsync($"SELECT COUNT(*) FROM {TableName}");

    public async Task<int> GetCountAsync(string processType)
        => await SafeCountAsync(
            $"SELECT COUNT(*) FROM {TableName} WHERE ProcessType = @ProcessType",
            new { ProcessType = processType });

    public async Task ResetAllAbandonedAsync()
    {
        var sql = $@"
            UPDATE {TableName}
            SET RetryCount = 0,
                NextRetryTime = @Now
            WHERE NextRetryTime = @MaxTime
              AND TRIM(PlcCode) <> ''
              AND IdempotencyKeyVersion IN (1, 2)";

        await StrictExecuteAsync(sql, new
        {
            Now = DateTime.UtcNow.ToString("O"),
            MaxTime = AbandonedRetryTimeUtc.ToString("O")
        });
    }

    protected override async Task AfterInitializeTableAsync(IDbConnection connection)
    {
        await EnsureDataPipelineContextColumnsAsync(connection, TableName).ConfigureAwait(false);
        await connection.ExecuteAsync(
                $"CREATE INDEX IF NOT EXISTS idx_{TableName}_device_created ON {TableName} (NetworkDeviceId, CreatedAt);",
                commandTimeout: CommandTimeout)
            .ConfigureAwait(false);
        await connection.ExecuteAsync(
                $"CREATE INDEX IF NOT EXISTS idx_{TableName}_plan_session ON {TableName} (PlanSessionId);",
                commandTimeout: CommandTimeout)
            .ConfigureAwait(false);
    }

    private static DataPipelineContextRow CreateContextRow(CellCompletedRecord sourceRecord)
        => new(
            sourceRecord.ResolvePlcCode(),
            sourceRecord.IdempotencyKeyVersion,
            sourceRecord.ResolveNetworkDeviceId(),
            sourceRecord.ResolveDeviceName(),
            sourceRecord.ModuleId,
            sourceRecord.TaskKey,
            sourceRecord.PlanSessionId,
            sourceRecord.MainPlanCode,
            sourceRecord.TraceBatchNumber);

    private sealed record DataPipelineContextRow(
        string PlcCode,
        CloudIdempotencyKeyVersion IdempotencyKeyVersion,
        int? NetworkDeviceId,
        string DeviceName,
        string ModuleId,
        string TaskKey,
        string PlanSessionId,
        string MainPlanCode,
        string TraceBatchNumber)
    {
        public static DataPipelineContextRow Empty { get; } = new(
            string.Empty,
            CloudIdempotencyKeyVersion.LegacyV1,
            null,
            string.Empty,
            string.Empty,
            string.Empty,
            string.Empty,
            string.Empty,
            string.Empty);
    }

    public async Task<int> DeleteExpiredAbandonedAsync(DateTime olderThanUtc)
    {
        var sql = $@"
            DELETE FROM {TableName}
            WHERE NextRetryTime = @MaxTime
              AND CreatedAt < @OlderThanUtc
              AND TRIM(PlcCode) <> ''
              AND IdempotencyKeyVersion IN (1, 2)";

        return await StrictExecuteAsync(sql, new
        {
            MaxTime = AbandonedRetryTimeUtc.ToString("O"),
            OlderThanUtc = EnsureUtc(olderThanUtc).ToString("O")
        });
    }

    private static DateTime EnsureUtc(DateTime value)
    {
        return value.Kind switch
        {
            DateTimeKind.Utc => value,
            DateTimeKind.Local => value.ToUniversalTime(),
            _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
        };
    }
}
