using IIoT.Edge.Application.Abstractions.Logging;
using IIoT.Edge.Infrastructure.Persistence.Dapper.Connection;
using IIoT.Edge.Infrastructure.Persistence.Dapper.Repository;
using IIoT.Edge.SharedKernel.DataPipeline;
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

    public async Task SaveAsync(DeadLetterRecord record)
    {
        var sql = $@"
            INSERT INTO {TableName}
                (ProcessType, CellDataJson, FailedTarget, SourceTable, SourceRecordId,
                 FailureStage, FailureReason, CreatedAt,
                 NetworkDeviceId, DeviceName, ModuleId, TaskKey, PlanSessionId, MainPlanCode, TraceBatchNumber)
            VALUES
                (@ProcessType, @CellDataJson, @FailedTarget, @SourceTable, @SourceRecordId,
                 @FailureStage, @FailureReason, @CreatedAt,
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
            record.NetworkDeviceId,
            record.DeviceName,
            record.ModuleId,
            record.TaskKey,
            record.PlanSessionId,
            record.MainPlanCode,
            record.TraceBatchNumber
        });

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

    public async Task DeleteAsync(long id)
    {
        var affectedRows = await DeleteByIdAsync(id).ConfigureAwait(false);
        if (affectedRows <= 0)
        {
            throw new InvalidOperationException($"未找到要删除的死信记录：{TableName}/{id}。");
        }
    }

    protected override Task AfterInitializeTableAsync(IDbConnection connection)
        => EnsureDataPipelineContextColumnsAsync(connection, TableName);
}
