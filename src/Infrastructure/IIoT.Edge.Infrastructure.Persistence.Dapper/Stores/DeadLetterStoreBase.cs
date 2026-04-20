using IIoT.Edge.Application.Abstractions.Logging;
using IIoT.Edge.Infrastructure.Persistence.Dapper.Connection;
using IIoT.Edge.Infrastructure.Persistence.Dapper.Repository;
using IIoT.Edge.SharedKernel.DataPipeline;

namespace IIoT.Edge.Infrastructure.Persistence.Dapper.Stores;

public abstract class DeadLetterStoreBase : DapperRepositoryBase<DeadLetterRecord>
{
    protected DeadLetterStoreBase(
        SqliteConnectionFactory connectionFactory,
        ILogService logger)
        : base(connectionFactory, logger)
    {
    }

    public async Task SaveAsync(DeadLetterRecord record)
    {
        var sql = $@"
            INSERT INTO {TableName}
                (ProcessType, CellDataJson, FailedTarget, SourceTable, SourceRecordId,
                 FailureStage, FailureReason, CreatedAt)
            VALUES
                (@ProcessType, @CellDataJson, @FailedTarget, @SourceTable, @SourceRecordId,
                 @FailureStage, @FailureReason, @CreatedAt)";

        var affectedRows = await SafeExecuteAsync(sql, new
        {
            record.ProcessType,
            record.CellDataJson,
            record.FailedTarget,
            record.SourceTable,
            record.SourceRecordId,
            record.FailureStage,
            record.FailureReason,
            CreatedAt = record.CreatedAt.ToString("O")
        });

        if (affectedRows <= 0)
        {
            throw new InvalidOperationException($"Failed to persist dead-letter record into {TableName}.");
        }
    }

    public Task<int> GetCountAsync()
        => SafeCountAsync($"SELECT COUNT(*) FROM {TableName}");
}
