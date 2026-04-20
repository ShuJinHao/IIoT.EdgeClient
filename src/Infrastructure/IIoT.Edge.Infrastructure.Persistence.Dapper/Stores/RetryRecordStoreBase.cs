using Dapper;
using IIoT.Edge.Application.Abstractions.DataPipeline.Stores;
using IIoT.Edge.Application.Abstractions.Logging;
using IIoT.Edge.Infrastructure.Persistence.Dapper.Connection;
using IIoT.Edge.Infrastructure.Persistence.Dapper.Repository;
using IIoT.Edge.SharedKernel.DataPipeline;
using IIoT.Edge.SharedKernel.DataPipeline.CellData;

namespace IIoT.Edge.Infrastructure.Persistence.Dapper.Stores;

public abstract class RetryRecordStoreBase : DapperRepositoryBase<FailedCellRecord>
{
    private static readonly DateTime AbandonedRetryTimeUtc = DateTime.SpecifyKind(DateTime.MaxValue, DateTimeKind.Utc);
    private static readonly TimeSpan ClaimTimeout = TimeSpan.FromMinutes(10);

    protected abstract string ChannelName { get; }
    protected abstract string ClaimTableName { get; }

    protected RetryRecordStoreBase(
        SqliteConnectionFactory connectionFactory,
        ILogService logger)
        : base(connectionFactory, logger)
    {
    }

    public async Task SaveAsync(
        CellCompletedRecord record,
        string failedTarget,
        string errorMessage)
    {
        var cellData = record.CellData;
        var cellDataJson = CellDataJsonSerializer.Serialize(cellData);
        var nowUtc = DateTime.UtcNow;

        var sql = $@"
            INSERT INTO {TableName}
                (ProcessType, CellDataJson, FailedTarget, ErrorMessage,
                 RetryCount, NextRetryTime, CreatedAt)
            VALUES
                (@ProcessType, @CellDataJson, @FailedTarget, @ErrorMessage,
                 0, @NextRetryTime, @CreatedAt)";

        var affectedRows = await SafeExecuteAsync(sql, new
        {
            ProcessType = cellData.ProcessType,
            CellDataJson = cellDataJson,
            FailedTarget = failedTarget,
            ErrorMessage = errorMessage,
            NextRetryTime = nowUtc.AddSeconds(30).ToString("O"),
            CreatedAt = nowUtc.ToString("O")
        });

        if (affectedRows <= 0)
        {
            throw new InvalidOperationException($"Failed to persist the {ChannelName} retry record.");
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
                CreatedAt
            FROM {TableName}
            WHERE NextRetryTime <= @Now
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
        return await ExecuteInTransactionAsync<ClaimedFailedCellBatch?>(async (conn, tx) =>
        {
            var nowUtc = DateTime.UtcNow;
            await conn.ExecuteAsync(
                $"DELETE FROM {ClaimTableName} WHERE ClaimedAt <= @ExpiredAt",
                new { ExpiredAt = nowUtc.Subtract(ClaimTimeout).ToString("O") },
                tx,
                commandTimeout: CommandTimeout);

            var ids = (await conn.QueryAsync<long>(
                $@"
                SELECT r.Id
                FROM {TableName} r
                LEFT JOIN {ClaimTableName} c ON c.RecordId = r.Id
                WHERE c.RecordId IS NULL
                  AND r.NextRetryTime <= @Now
                ORDER BY r.NextRetryTime ASC, r.Id ASC
                LIMIT @BatchSize",
                new
                {
                    Now = nowUtc.ToString("O"),
                    BatchSize = batchSize
                },
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
                ClaimedAt = nowUtc.ToString("O")
            });

            await conn.ExecuteAsync(
                $"INSERT INTO {ClaimTableName} (RecordId, ClaimToken, ClaimedAt) VALUES (@RecordId, @ClaimToken, @ClaimedAt)",
                claimRows,
                tx,
                commandTimeout: CommandTimeout);

            var records = (await conn.QueryAsync<FailedCellRecord>(
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
                    r.CreatedAt
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
                commandTimeout: CommandTimeout)).ToList();

            if (records.Count == 0)
            {
                await conn.ExecuteAsync(
                    $"DELETE FROM {ClaimTableName} WHERE ClaimToken = @ClaimToken",
                    new { ClaimToken = claimToken },
                    tx,
                    commandTimeout: CommandTimeout);
                return null;
            }

            return new ClaimedFailedCellBatch
            {
                ClaimToken = claimToken,
                Records = records
            };
        }).ConfigureAwait(false);
    }

    public async Task DeleteClaimedBatchAsync(string claimToken)
    {
        await ExecuteInTransactionAsync<int>(async (conn, tx) =>
        {
            var ids = (await conn.QueryAsync<long>(
                $"SELECT RecordId FROM {ClaimTableName} WHERE ClaimToken = @ClaimToken",
                new { ClaimToken = claimToken },
                tx,
                commandTimeout: CommandTimeout)).ToList();

            if (ids.Count == 0)
            {
                throw new InvalidOperationException($"No claimed {ChannelName} retry rows found for claim {claimToken}.");
            }

            await conn.ExecuteAsync(
                $"DELETE FROM {TableName} WHERE Id IN @Ids",
                new { Ids = ids },
                tx,
                commandTimeout: CommandTimeout);

            await conn.ExecuteAsync(
                $"DELETE FROM {ClaimTableName} WHERE RecordId IN @Ids",
                new { Ids = ids },
                tx,
                commandTimeout: CommandTimeout);

            return ids.Count;
        }).ConfigureAwait(false);
    }

    public async Task ReleaseClaimAsync(string claimToken)
    {
        await StrictExecuteAsync(
            $"DELETE FROM {ClaimTableName} WHERE ClaimToken = @ClaimToken",
            new { ClaimToken = claimToken },
            requireAffectedRows: true,
            failureMessage: $"Failed to release {ChannelName} retry claim {claimToken}.").ConfigureAwait(false);
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
                throw new InvalidOperationException($"Failed to update retry metadata for {ChannelName} record {id}.");
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
                throw new InvalidOperationException($"Failed to delete {ChannelName} retry record {id}.");
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
            WHERE NextRetryTime = @MaxTime";

        await StrictExecuteAsync(sql, new
        {
            Now = DateTime.UtcNow.ToString("O"),
            MaxTime = AbandonedRetryTimeUtc.ToString("O")
        });
    }

    public async Task<int> DeleteExpiredAbandonedAsync(DateTime olderThanUtc)
    {
        var sql = $@"
            DELETE FROM {TableName}
            WHERE NextRetryTime = @MaxTime
              AND CreatedAt < @OlderThanUtc";

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
