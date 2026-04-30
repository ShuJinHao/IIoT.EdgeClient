using IIoT.Edge.Application.Abstractions.DataPipeline.Stores;
using IIoT.Edge.Application.Abstractions.Logging;
using IIoT.Edge.Infrastructure.Persistence.Dapper.Connection;

namespace IIoT.Edge.Infrastructure.Persistence.Dapper.Stores;

/// <summary>
/// MES 主补传表。只保存完整 CellDataJson 和失败目标，不为插件业务字段建列。
/// </summary>
public sealed class MesRetryRecordStore : RetryRecordStoreBase, IMesRetryRecordStore
{
    /// <summary>
    /// MES 使用独立 SQLite 数据库文件，避免和 Cloud 补偿链路混库。
    /// </summary>
    public override string DbName => "pipeline_mes";

    /// <summary>
    /// MES 正常补传队列表；MesRetryTask 从这里领取记录并调用 MES consumer。
    /// </summary>
    protected override string TableName => "failed_mes_records";

    protected override string ChannelName => "MES";

    /// <summary>
    /// 领取锁表，防止多个任务实例重复补传同一条 MES 记录。
    /// </summary>
    protected override string ClaimTableName => "failed_mes_record_claims";

    protected override string CreateTableSql => @"
        CREATE TABLE IF NOT EXISTS failed_mes_records (
            Id              INTEGER PRIMARY KEY AUTOINCREMENT,
            ProcessType     TEXT    NOT NULL,
            CellDataJson    TEXT    NOT NULL,
            FailedTarget    TEXT    NOT NULL,
            ErrorMessage    TEXT    NOT NULL,
            RetryCount      INTEGER NOT NULL DEFAULT 0,
            NextRetryTime   TEXT    NOT NULL,
            CreatedAt       TEXT    NOT NULL
        );

        CREATE INDEX IF NOT EXISTS idx_failed_mes_retry
            ON failed_mes_records (NextRetryTime);
        CREATE INDEX IF NOT EXISTS idx_failed_mes_target_retry
            ON failed_mes_records (FailedTarget, NextRetryTime);
        CREATE INDEX IF NOT EXISTS idx_failed_mes_process_retry
            ON failed_mes_records (ProcessType, NextRetryTime);

        CREATE TABLE IF NOT EXISTS failed_mes_record_claims (
            RecordId    INTEGER PRIMARY KEY,
            ClaimToken  TEXT    NOT NULL,
            ClaimedAt   TEXT    NOT NULL
        );
        CREATE INDEX IF NOT EXISTS idx_failed_mes_claim_token
            ON failed_mes_record_claims (ClaimToken);
        CREATE INDEX IF NOT EXISTS idx_failed_mes_claim_time
            ON failed_mes_record_claims (ClaimedAt);
    ";

    public MesRetryRecordStore(
        SqliteConnectionFactory connectionFactory,
        ILogService logger)
        : base(connectionFactory, logger)
    {
    }
}
