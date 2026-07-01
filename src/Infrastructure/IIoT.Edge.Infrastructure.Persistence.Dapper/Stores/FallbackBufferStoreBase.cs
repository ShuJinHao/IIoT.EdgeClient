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
    private static readonly TimeSpan InitialRetryDelay = TimeSpan.FromSeconds(30);
    private readonly ICellDataJsonSerializer _cellDataJsonSerializer;

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
        ICellDataJsonSerializer cellDataJsonSerializer)
        : base(connectionFactory, logger)
    {
        _cellDataJsonSerializer = cellDataJsonSerializer;
    }

    public async Task SaveAsync(CellCompletedRecord record, string failedTarget, string errorMessage)
    {
        var cellData = record.CellData;
        var cellDataJson = _cellDataJsonSerializer.Serialize(cellData);

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
            throw new InvalidOperationException($"持久化 {ChannelDisplayName} 兜底记录失败。");
        }
    }

    public async Task<List<TEntity>> GetPendingAsync(int batchSize = 50)
    {
        return await SafeQueryByIdAscendingAsync<TEntity>(batchSize).ConfigureAwait(false);
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
            // FallbackBuffer 只负责把异常落盘数据交还给正式重试表；初始延迟避免恢复后立即打满上传通道，
            // 后续失败退避由 CloudRetryTask/MesRetryTask 的重试策略继续接管。
            var nextRetryTime = DateTime.UtcNow.Add(InitialRetryDelay).ToString("O");
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
                throw new InvalidOperationException($"移动 {ChannelDisplayName} 兜底记录到补传表失败。");
            }

            var deleted = await conn.ExecuteAsync(
                $"DELETE FROM {TableName} WHERE Id IN @Ids",
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
            $"DELETE FROM {TableName} WHERE Id IN @Ids",
            new { Ids = idList },
            requireAffectedRows: true,
            failureMessage: $"删除 {ChannelDisplayName} 兜底记录失败。").ConfigureAwait(false);
    }

    public Task<int> GetCountAsync()
        => SafeCountAsync($"SELECT COUNT(*) FROM {TableName}");
}
