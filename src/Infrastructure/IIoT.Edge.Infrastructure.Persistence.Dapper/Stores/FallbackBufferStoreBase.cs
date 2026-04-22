using Dapper;
using IIoT.Edge.Application.Abstractions.Logging;
using IIoT.Edge.Infrastructure.Persistence.Dapper.Connection;
using IIoT.Edge.Infrastructure.Persistence.Dapper.Repository;
using IIoT.Edge.SharedKernel.DataPipeline;
using IIoT.Edge.SharedKernel.DataPipeline.CellData;

namespace IIoT.Edge.Infrastructure.Persistence.Dapper.Stores;

public abstract class FallbackBufferStoreBase<TEntity> : DapperRepositoryBase<TEntity>
    where TEntity : class
{
    protected abstract string ChannelName { get; }

    protected abstract string RetryTableName { get; }

    protected FallbackBufferStoreBase(
        SqliteConnectionFactory connectionFactory,
        ILogService logger)
        : base(connectionFactory, logger)
    {
    }

    public async Task SaveAsync(CellCompletedRecord record, string failedTarget, string errorMessage)
    {
        var cellData = record.CellData;
        var cellDataJson = CellDataJsonSerializer.Serialize(cellData);

        var sql = $@"
            INSERT INTO {TableName}
                (ProcessType, CellDataJson, FailedTarget, ErrorMessage, CreatedAt)
            VALUES
                (@ProcessType, @CellDataJson, @FailedTarget, @ErrorMessage, @CreatedAt)";

        var affectedRows = await SafeExecuteAsync(sql, new
        {
            ProcessType = cellData.ProcessType,
            CellDataJson = cellDataJson,
            FailedTarget = failedTarget,
            ErrorMessage = errorMessage,
            CreatedAt = DateTime.UtcNow.ToString("O")
        }).ConfigureAwait(false);

        if (affectedRows <= 0)
        {
            throw new InvalidOperationException($"Failed to persist the {ChannelName} fallback record.");
        }
    }

    public async Task<List<TEntity>> GetPendingAsync(int batchSize = 50)
    {
        var sql = $@"
            SELECT * FROM {TableName}
            ORDER BY Id ASC
            LIMIT @BatchSize";

        var result = await SafeQueryAsync(sql, new { BatchSize = batchSize }).ConfigureAwait(false);
        return result.ToList();
    }

    public async Task MovePendingToRetryAsync(IEnumerable<long> ids)
    {
        var idList = ids.Distinct().ToList();
        if (idList.Count == 0)
        {
            return;
        }

        await ExecuteInTransactionAsync<int>(async (conn, tx) =>
        {
            var nextRetryTime = DateTime.UtcNow.AddSeconds(30).ToString("O");
            var inserted = await conn.ExecuteAsync(
                $@"
                INSERT INTO {RetryTableName}
                    (ProcessType, CellDataJson, FailedTarget, ErrorMessage, RetryCount, NextRetryTime, CreatedAt)
                SELECT
                    ProcessType,
                    CellDataJson,
                    FailedTarget,
                    ErrorMessage,
                    0,
                    @NextRetryTime,
                    CreatedAt
                FROM {TableName}
                WHERE Id IN @Ids",
                new
                {
                    Ids = idList,
                    NextRetryTime = nextRetryTime
                },
                tx,
                commandTimeout: CommandTimeout).ConfigureAwait(false);

            if (inserted <= 0)
            {
                throw new InvalidOperationException($"Failed to move {ChannelName} fallback records into the retry store.");
            }

            var deleted = await conn.ExecuteAsync(
                $"DELETE FROM {TableName} WHERE Id IN @Ids",
                new { Ids = idList },
                tx,
                commandTimeout: CommandTimeout).ConfigureAwait(false);

            if (deleted <= 0)
            {
                throw new InvalidOperationException($"Failed to delete moved {ChannelName} fallback records.");
            }

            return deleted;
        }).ConfigureAwait(false);
    }

    public async Task DeleteBatchAsync(IEnumerable<long> ids)
    {
        var idList = ids.ToList();
        if (idList.Count == 0)
        {
            return;
        }

        await StrictExecuteAsync(
            $"DELETE FROM {TableName} WHERE Id IN @Ids",
            new { Ids = idList },
            requireAffectedRows: true,
            failureMessage: $"Failed to delete {ChannelName} fallback records.").ConfigureAwait(false);
    }

    public Task<int> GetCountAsync()
        => SafeCountAsync($"SELECT COUNT(*) FROM {TableName}");
}
