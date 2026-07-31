using IIoT.Edge.Module.Contracts.Logging;
using IIoT.Edge.Infrastructure.Persistence.Dapper.Connection;
using IIoT.Edge.Infrastructure.Persistence.Dapper.Repository;
using IIoT.Edge.Module.Contracts.DataPipeline;
using System.Data;

namespace IIoT.Edge.Infrastructure.Persistence.Dapper.Stores;

public abstract class DeadLetterStoreBase : DapperRepositoryBase<DeadLetterRecord>
{
    protected DeadLetterStoreBase(
        SqliteConnectionFactory connectionFactory,
        ILogService logger)
        : base(connectionFactory, logger)
    {
    }

    public async Task SaveAsync(
        DeadLetterRecord record,
        CancellationToken cancellationToken = default)
    {
        var sql = $@"
            INSERT INTO {TableName}
                (ProcessType, CellDataJson, FailedTarget, SourceTable, SourceRecordId,
                 FailureStage, FailureReason, CreatedAt,
                 PlcCode, IdempotencyKeyVersion,
                 NetworkDeviceId, DeviceName, ModuleId, TaskKey, PlanSessionId, MainPlanCode, TraceBatchNumber)
            VALUES
                (@ProcessType, @CellDataJson, @FailedTarget, @SourceTable, @SourceRecordId,
                 @FailureStage, @FailureReason, @CreatedAt,
                 @PlcCode, @IdempotencyKeyVersion,
                 @NetworkDeviceId, @DeviceName, @ModuleId, @TaskKey, @PlanSessionId, @MainPlanCode, @TraceBatchNumber)";

        var affectedRows = await SafeExecuteAsync(sql, new
        {
            record.ProcessType,
            record.CellDataJson,
            record.FailedTarget,
            record.SourceTable,
            record.SourceRecordId,
            record.FailureStage,
            record.FailureReason,
            CreatedAt = record.CreatedAt.ToString("O"),
            record.PlcCode,
            IdempotencyKeyVersion = (int)record.IdempotencyKeyVersion,
            record.NetworkDeviceId,
            record.DeviceName,
            record.ModuleId,
            record.TaskKey,
            record.PlanSessionId,
            record.MainPlanCode,
            record.TraceBatchNumber
        }, cancellationToken).ConfigureAwait(false);

        if (affectedRows <= 0)
        {
            throw new InvalidOperationException($"持久化死信记录到 {TableName} 失败。");
        }
    }

    public Task<int> GetCountAsync()
        => SafeCountAsync($"SELECT COUNT(*) FROM {TableName}");

    public new Task<DeadLetterRecord?> GetByIdAsync(long id)
        => base.GetByIdAsync(id);

    public async Task<IReadOnlyList<DeadLetterGroupSummary>> GetGroupSummaryAsync()
    {
        var sql = $@"
            SELECT
                ProcessType,
                FailureStage,
                COUNT(*) AS Count,
                MAX(CreatedAt) AS LastCreatedAt
            FROM {TableName}
            GROUP BY ProcessType, FailureStage
            ORDER BY Count DESC, LastCreatedAt DESC";

        return await SafeQueryListAsync<DeadLetterGroupSummary>(sql).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<DeadLetterRecord>> GetLatestAsync(int count = 20)
    {
        var sql = $@"
            SELECT *
            FROM {TableName}
            ORDER BY CreatedAt DESC, Id DESC
            LIMIT @Count";

        return await SafeQueryListAsync<DeadLetterRecord>(
            sql,
            new { Count = Math.Clamp(count, 1, 100) }).ConfigureAwait(false);
    }

    public Task DeleteAsync(long id)
        => throw new NotSupportedException(
            "未成功死信禁止人工硬删除；只能通过本通道原子重入队转移。");

    protected override Task AfterInitializeTableAsync(IDbConnection connection)
        => EnsureDataPipelineContextColumnsAsync(connection, TableName);
}
