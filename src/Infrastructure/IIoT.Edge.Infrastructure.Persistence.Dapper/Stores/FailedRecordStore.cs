using Dapper;
using IIoT.Edge.Application.Abstractions.DataPipeline.Stores;
using IIoT.Edge.Application.Abstractions.Logging;
using IIoT.Edge.Infrastructure.Persistence.Dapper.Connection;
using IIoT.Edge.Infrastructure.Persistence.Dapper.Repository;
using IIoT.Edge.SharedKernel.DataPipeline;
using System.Text.Json;

namespace IIoT.Edge.Infrastructure.Persistence.Dapper.Stores;

public class FailedRecordStore : DapperRepositoryBase<FailedCellRecord>, IFailedRecordStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public override string DbName => "pipeline";
    protected override string TableName => "failed_cell_records";

    protected override string CreateTableSql => @"
        CREATE TABLE IF NOT EXISTS failed_cell_records (
            Id              INTEGER PRIMARY KEY AUTOINCREMENT,
            Channel         TEXT    NOT NULL,
            ProcessType     TEXT    NOT NULL,
            CellDataJson    TEXT    NOT NULL,
            FailedTarget    TEXT    NOT NULL,
            ErrorMessage    TEXT    NOT NULL,
            RetryCount      INTEGER NOT NULL DEFAULT 0,
            NextRetryTime   TEXT    NOT NULL,
            CreatedAt       TEXT    NOT NULL
        );

        CREATE INDEX IF NOT EXISTS idx_failed_channel_retry
            ON failed_cell_records (Channel, NextRetryTime);
        CREATE INDEX IF NOT EXISTS idx_failed_channel_target_retry
            ON failed_cell_records (Channel, FailedTarget, NextRetryTime);
    ";

    public FailedRecordStore(
        SqliteConnectionFactory connectionFactory,
        ILogService logger)
        : base(connectionFactory, logger)
    {
    }

    public async Task SaveAsync(
        CellCompletedRecord record,
        string failedTarget,
        string errorMessage,
        string channel)
    {
        var cellData = record.CellData;
        var cellDataJson = JsonSerializer.Serialize(cellData, cellData.GetType(), JsonOptions);

        const string sql = @"
            INSERT INTO failed_cell_records
                (Channel, ProcessType, CellDataJson, FailedTarget, ErrorMessage,
                 RetryCount, NextRetryTime, CreatedAt)
            VALUES
                (@Channel, @ProcessType, @CellDataJson, @FailedTarget, @ErrorMessage,
                 0, @NextRetryTime, @CreatedAt)";

        var affectedRows = await SafeExecuteAsync(sql, new
        {
            Channel = channel,
            ProcessType = cellData.ProcessType,
            CellDataJson = cellDataJson,
            FailedTarget = failedTarget,
            ErrorMessage = errorMessage,
            NextRetryTime = DateTime.Now.AddSeconds(30).ToString("O"),
            CreatedAt = DateTime.Now.ToString("O")
        });

        if (affectedRows <= 0)
        {
            throw new InvalidOperationException("Failed to persist the retry record.");
        }
    }

    public async Task<List<FailedCellRecord>> GetPendingAsync(string channel, int batchSize = 10)
    {
        const string sql = @"
            SELECT * FROM failed_cell_records
            WHERE Channel = @Channel
              AND NextRetryTime <= @Now
            ORDER BY NextRetryTime ASC
            LIMIT @BatchSize";

        var result = await SafeQueryAsync(sql, new
        {
            Channel = channel,
            Now = DateTime.Now.ToString("O"),
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
        const string sql = @"
            UPDATE failed_cell_records
            SET RetryCount = @RetryCount,
                ErrorMessage = @ErrorMessage,
                NextRetryTime = @NextRetryTime
            WHERE Id = @Id";

        await SafeExecuteAsync(sql, new
        {
            Id = id,
            RetryCount = retryCount,
            ErrorMessage = errorMessage,
            NextRetryTime = nextRetryTime.ToString("O")
        });
    }

    public async Task DeleteAsync(long id)
        => await SafeExecuteAsync($"DELETE FROM {TableName} WHERE Id = @Id", new { Id = id });

    public async Task<int> GetCountAsync()
        => await SafeCountAsync($"SELECT COUNT(*) FROM {TableName}");

    public async Task<int> GetCountAsync(string channel)
        => await SafeCountAsync(
            $"SELECT COUNT(*) FROM {TableName} WHERE Channel = @Channel",
            new { Channel = channel });

    public async Task<int> GetCountAsync(string channel, string processType)
        => await SafeCountAsync(
            $"SELECT COUNT(*) FROM {TableName} WHERE Channel = @Channel AND ProcessType = @ProcessType",
            new
            {
                Channel = channel,
                ProcessType = processType
            });

    public async Task ResetAllAbandonedAsync()
    {
        const string sql = @"
            UPDATE failed_cell_records
            SET RetryCount = 0,
                NextRetryTime = @Now
            WHERE NextRetryTime = @MaxTime";

        await SafeExecuteAsync(sql, new
        {
            Now = DateTime.Now.ToString("O"),
            MaxTime = DateTime.MaxValue.ToString("O")
        });
    }
}
