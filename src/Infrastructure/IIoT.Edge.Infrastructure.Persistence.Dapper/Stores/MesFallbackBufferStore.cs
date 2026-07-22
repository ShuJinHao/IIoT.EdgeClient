using IIoT.Edge.Module.Contracts.DataPipeline.Stores;
using IIoT.Edge.Module.Contracts.Logging;
using IIoT.Edge.Infrastructure.Persistence.Dapper.Connection;
using IIoT.Edge.Module.Contracts.DataPipeline;
using IIoT.Edge.Module.Contracts.DataPipeline.CellData;

using IIoT.Edge.Module.Contracts.Mes;
namespace IIoT.Edge.Infrastructure.Persistence.Dapper.Stores;

/// <summary>
/// MES fallback 缓冲表。仅在主 retry 表暂时不可写时使用，数据仍然属于 MES 独立补偿链路。
/// </summary>
public class MesFallbackBufferStore : FallbackBufferStoreBase<MesFallbackRecord>, IMesFallbackBufferStore
{
    /// <summary>
    /// 和 MES retry/deadletter 使用同一个 MES 专用库，不进入 Cloud 数据库。
    /// </summary>
    public override string DbName => "pipeline_mes";

    /// <summary>
    /// MES 临时缓冲表；MesRetryTask 心跳恢复后会尝试搬回 failed_mes_records。
    /// </summary>
    protected override string TableName => "mes_fallback_records";

    protected override string ChannelName => "MES";

    /// <summary>
    /// fallback 恢复目标表，保持 MES fallback 只回到 MES retry。
    /// </summary>
    protected override string RetryTableName => "failed_mes_records";

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
        ILogService logger,
        ICellDataJsonSerializer cellDataJsonSerializer)
        : base(connectionFactory, logger, cellDataJsonSerializer)
    {
    }
}
