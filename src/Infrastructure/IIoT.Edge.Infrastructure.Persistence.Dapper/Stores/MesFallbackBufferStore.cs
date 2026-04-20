using IIoT.Edge.Application.Abstractions.DataPipeline.Stores;
using IIoT.Edge.Application.Abstractions.Logging;
using IIoT.Edge.Infrastructure.Persistence.Dapper.Connection;
using IIoT.Edge.Infrastructure.Persistence.Dapper.Repository;
using IIoT.Edge.SharedKernel.DataPipeline;
using Dapper;
using System.Text.Json;

namespace IIoT.Edge.Infrastructure.Persistence.Dapper.Stores;

public class MesFallbackBufferStore : DapperRepositoryBase<MesFallbackRecord>, IMesFallbackBufferStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public override string DbName => "pipeline_mes";
    protected override string TableName => "mes_fallback_records";

    protected override string CreateTableSql => @"
        CREATE TABLE IF NOT EXISTS mes_fallback_records (
            Id            INTEGER PRIMARY KEY AUTOINCREMENT,
            ProcessType   TEXT    NOT NULL,
            CellDataJson  TEXT    NOT NULL,
            FailedTarget  TEXT    NOT NULL,
            ErrorMessage  TEXT    NOT NULL,
            CreatedAt     TEXT    NOT NULL
        );

        CREATE INDEX IF NOT EXISTS idx_mes_fallback_created
            ON mes_fallback_records (CreatedAt);
    ";

    public MesFallbackBufferStore(
        SqliteConnectionFactory connectionFactory,
        ILogService logger)
        : base(connectionFactory, logger)
    {
    }

    public async Task SaveAsync(CellCompletedRecord record, string failedTarget, string errorMessage)
    {
        var cellData = record.CellData;
        var cellDataJson = JsonSerializer.Serialize(cellData, cellData.GetType(), JsonOptions);

        const string sql = @"
            INSERT INTO mes_fallback_records
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
        });

        if (affectedRows <= 0)
        {
            throw new InvalidOperationException("Failed to persist the MES fallback record.");
        }
    }

    public async Task<List<MesFallbackRecord>> GetPendingAsync(int batchSize = 50)
    {
        var sql = $@"
            SELECT * FROM {TableName}
            ORDER BY Id ASC
            LIMIT @BatchSize";

        var result = await SafeQueryAsync(sql, new { BatchSize = batchSize });
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
                @"
                INSERT INTO failed_mes_records
                    (ProcessType, CellDataJson, FailedTarget, ErrorMessage, RetryCount, NextRetryTime, CreatedAt)
                SELECT
                    ProcessType,
                    CellDataJson,
                    FailedTarget,
                    ErrorMessage,
                    0,
                    @NextRetryTime,
                    CreatedAt
                FROM mes_fallback_records
                WHERE Id IN @Ids",
                new
                {
                    Ids = idList,
                    NextRetryTime = nextRetryTime
                },
                tx,
                commandTimeout: CommandTimeout);

            if (inserted <= 0)
            {
                throw new InvalidOperationException("Failed to move MES fallback records into the retry store.");
            }

            var deleted = await conn.ExecuteAsync(
                "DELETE FROM mes_fallback_records WHERE Id IN @Ids",
                new { Ids = idList },
                tx,
                commandTimeout: CommandTimeout);

            if (deleted <= 0)
            {
                throw new InvalidOperationException("Failed to delete moved MES fallback records.");
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
            failureMessage: "Failed to delete MES fallback records.");
    }

    public async Task<int> GetCountAsync()
        => await SafeCountAsync($"SELECT COUNT(*) FROM {TableName}");
}
