using Dapper;
using IIoT.Edge.Module.Contracts.Logging;
using IIoT.Edge.Infrastructure.Persistence.Dapper.Connection;
using IIoT.Edge.Infrastructure.Persistence.Dapper.Repository;
using IIoT.Edge.Module.Contracts.DataPipeline;
using IIoT.Edge.Module.Contracts.DataPipeline.CellData;
using System.Data;
using IIoT.Edge.Application.Common.Identity;

namespace IIoT.Edge.Infrastructure.Persistence.Dapper.Stores;

public abstract class FallbackBufferStoreBase<TEntity> : DapperRepositoryBase<TEntity>
    where TEntity : class
{
    private readonly ICellDataJsonSerializer _cellDataJsonSerializer;
    private readonly IDevicePluginRuntimeContext? _runtimeContext;

    protected abstract string ChannelName { get; }

    protected abstract string RetryTableName { get; }

    protected string ChannelDisplayName
        => ChannelName switch
        {
            "Cloud" => "云端",
            "MES" => "MES",
            _ => ChannelName
        };

    protected FallbackBufferStoreBase(
        SqliteConnectionFactory connectionFactory,
        ILogService logger,
        ICellDataJsonSerializer cellDataJsonSerializer,
        IDevicePluginRuntimeContext? runtimeContext = null)
        : base(connectionFactory, logger)
    {
        _cellDataJsonSerializer = cellDataJsonSerializer;
        _runtimeContext = runtimeContext;
    }

    public async Task SaveAsync(
        CellCompletedRecord record,
        string failedTarget,
        string errorMessage,
        CancellationToken cancellationToken = default)
    {
        var cellData = record.CellData;
        var cellDataJson = _cellDataJsonSerializer.Serialize(cellData);
        var context = CreateContextRow(record);

        var sql = $@"
            INSERT INTO {TableName}
                (ClientCode, CompletionId, TypeKey,
                 ProcessType, CellDataJson, FailedTarget, ErrorMessage, CreatedAt,
                 PlcCode, IdempotencyKeyVersion,
                 NetworkDeviceId, DeviceName, ModuleId, TaskKey, PlanSessionId, MainPlanCode, TraceBatchNumber)
            VALUES
                (@ClientCode, @CompletionId, @TypeKey,
                 @ProcessType, @CellDataJson, @FailedTarget, @ErrorMessage, @CreatedAt,
                 @PlcCode, @IdempotencyKeyVersion,
                 @NetworkDeviceId, @DeviceName, @ModuleId, @TaskKey, @PlanSessionId, @MainPlanCode, @TraceBatchNumber)";

        var affectedRows = await SafeExecuteAsync(sql, new
        {
            ProcessType = _runtimeContext?.Current is { IsV3: true } runtime
                ? runtime.ProcessType
                : cellData.ProcessType,
            context.ClientCode,
            context.CompletionId,
            context.TypeKey,
            CellDataJson = cellDataJson,
            FailedTarget = failedTarget,
            ErrorMessage = errorMessage,
            CreatedAt = DateTime.UtcNow.ToString("O"),
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
            throw new InvalidOperationException($"持久化 {ChannelDisplayName} 兜底记录失败。");
        }
    }

    public async Task<List<TEntity>> GetPendingAsync(int batchSize = 50)
    {
        return await SafeQueryListAsync<TEntity>(
            $"""
             SELECT *
             FROM {TableName}
             WHERE TRIM(PlcCode) <> ''
               AND IdempotencyKeyVersion IN (1, 2)
             ORDER BY Id ASC
             LIMIT @BatchSize
             """,
            new { BatchSize = batchSize }).ConfigureAwait(false);
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
            // 链路恢复的同一轮必须能够领取这些记录；批量上限负责限流，不能再人为延迟。
            var nextRetryTime = DateTime.UtcNow.ToString("O");
            await conn.ExecuteAsync(
                $@"
                INSERT OR IGNORE INTO {RetryTableName}
                    (ClientCode, CompletionId, TypeKey,
                     ProcessType, CellDataJson, FailedTarget, ErrorMessage, RetryCount, NextRetryTime, CreatedAt,
                     PlcCode, IdempotencyKeyVersion,
                     NetworkDeviceId, DeviceName, ModuleId, TaskKey, PlanSessionId, MainPlanCode, TraceBatchNumber)
                SELECT
                    ClientCode,
                    CompletionId,
                    TypeKey,
                    ProcessType,
                    CellDataJson,
                    FailedTarget,
                    ErrorMessage,
                    0,
                    @NextRetryTime,
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
                WHERE Id IN @Ids
                  AND TRIM(PlcCode) <> ''
                  AND IdempotencyKeyVersion IN (1, 2)",
                new
                {
                    Ids = idList,
                    NextRetryTime = nextRetryTime
                },
                tx,
                commandTimeout: CommandTimeout).ConfigureAwait(false);

            var uncoveredV3 = await conn.ExecuteScalarAsync<long>(
                $"""
                 SELECT COUNT(*)
                 FROM {TableName} f
                 WHERE f.Id IN @Ids
                   AND TRIM(f.PlcCode) <> ''
                   AND f.IdempotencyKeyVersion IN (1, 2)
                   AND TRIM(f.ClientCode) <> ''
                   AND TRIM(f.CompletionId) <> ''
                   AND NOT EXISTS (
                       SELECT 1
                       FROM {RetryTableName} r
                       WHERE r.ClientCode = f.ClientCode
                         AND r.CompletionId = f.CompletionId)
                 """,
                new { Ids = idList },
                tx,
                commandTimeout: CommandTimeout).ConfigureAwait(false);
            if (uncoveredV3 != 0)
            {
                throw new InvalidOperationException($"移动 {ChannelDisplayName} 兜底记录到补传表后存在未覆盖事实。");
            }

            var deleted = await conn.ExecuteAsync(
                $"""
                 DELETE FROM {TableName}
                 WHERE Id IN @Ids
                   AND TRIM(PlcCode) <> ''
                   AND IdempotencyKeyVersion IN (1, 2)
                 """,
                new { Ids = idList },
                tx,
                commandTimeout: CommandTimeout).ConfigureAwait(false);

            if (deleted <= 0)
            {
                throw new InvalidOperationException($"删除已移动的 {ChannelDisplayName} 兜底记录失败。");
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
            $"""
             DELETE FROM {TableName}
             WHERE Id IN @Ids
               AND TRIM(PlcCode) <> ''
               AND IdempotencyKeyVersion IN (1, 2)
             """,
            new { Ids = idList },
            requireAffectedRows: true,
            failureMessage: $"删除 {ChannelDisplayName} 兜底记录失败。").ConfigureAwait(false);
    }

    public Task<int> GetCountAsync()
        => SafeCountAsync($"SELECT COUNT(*) FROM {TableName}");

    protected override Task AfterInitializeTableAsync(IDbConnection connection)
        => EnsureDataPipelineContextColumnsAsync(connection, TableName);

    private static DataPipelineContextRow CreateContextRow(CellCompletedRecord record)
        => new(
            record.ClientCode,
            record.CompletionId,
            record.TypeKey,
            record.ResolvePlcCode(),
            record.IdempotencyKeyVersion,
            record.ResolveNetworkDeviceId(),
            record.ResolveDeviceName(),
            record.ModuleId,
            record.TaskKey,
            record.PlanSessionId,
            record.MainPlanCode,
            record.TraceBatchNumber);

    private sealed record DataPipelineContextRow(
        string ClientCode,
        string CompletionId,
        string TypeKey,
        string PlcCode,
        CloudIdempotencyKeyVersion IdempotencyKeyVersion,
        int? NetworkDeviceId,
        string DeviceName,
        string ModuleId,
        string TaskKey,
        string PlanSessionId,
        string MainPlanCode,
        string TraceBatchNumber);
}
