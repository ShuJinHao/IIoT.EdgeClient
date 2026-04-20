using Dapper;
using IIoT.Edge.Application.Abstractions.Logging;
using IIoT.Edge.Infrastructure.Persistence.Dapper.Connection;
using IIoT.Edge.Infrastructure.Persistence.Dapper.Repository;
using IIoT.Edge.SharedKernel.DataPipeline;
using IIoT.Edge.SharedKernel.DataPipeline.CellData;

namespace IIoT.Edge.Infrastructure.Persistence.Dapper.Stores;

public abstract class RetryRecordStoreBase : DapperRepositoryBase<FailedCellRecord>
{
    private static readonly DateTime AbandonedRetryTimeUtc = DateTime.SpecifyKind(DateTime.MaxValue, DateTimeKind.Utc);

    protected abstract string ChannelName { get; }

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

    public async Task UpdateRetryAsync(
        long id,
        int retryCount,
        string errorMessage,
        DateTime nextRetryTime)
    {
        var sql = $@"
            UPDATE {TableName}
            SET RetryCount = @RetryCount,
                ErrorMessage = @ErrorMessage,
                NextRetryTime = @NextRetryTime
            WHERE Id = @Id";

        await StrictExecuteAsync(
            sql,
            new
            {
                Id = id,
                RetryCount = retryCount,
                ErrorMessage = errorMessage,
                NextRetryTime = EnsureUtc(nextRetryTime).ToString("O")
            },
            requireAffectedRows: true,
            failureMessage: $"Failed to update retry metadata for {ChannelName} record {id}.");
    }

    public async Task DeleteAsync(long id)
        => await StrictExecuteAsync(
            $"DELETE FROM {TableName} WHERE Id = @Id",
            new { Id = id },
            requireAffectedRows: true,
            failureMessage: $"Failed to delete {ChannelName} retry record {id}.");

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
