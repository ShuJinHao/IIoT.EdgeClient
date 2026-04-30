using IIoT.Edge.Application.Abstractions.DataPipeline.Stores;
using IIoT.Edge.Application.Abstractions.Logging;
using IIoT.Edge.Infrastructure.Persistence.Dapper.Connection;

namespace IIoT.Edge.Infrastructure.Persistence.Dapper.Stores;

/// <summary>
/// MES 死信表。保存无法继续自动补传的 CellDataJson，并记录来源表和失败阶段。
/// </summary>
public sealed class MesDeadLetterStore : DeadLetterStoreBase, IMesDeadLetterStore
{
    /// <summary>
    /// MES 死信仍写入 MES 专用库，避免和 Cloud 死信混用。
    /// </summary>
    public override string DbName => "pipeline_mes";

    /// <summary>
    /// MES 死信表；常见来源包括 failed_mes_records、mes_fallback_records、ingress_overflow。
    /// </summary>
    protected override string TableName => "dead_mes_records";

    protected override string CreateTableSql => @"
        CREATE TABLE IF NOT EXISTS dead_mes_records (
            Id            INTEGER PRIMARY KEY AUTOINCREMENT,
            ProcessType   TEXT    NOT NULL,
            CellDataJson  TEXT    NOT NULL,
            FailedTarget  TEXT    NOT NULL,
            SourceTable   TEXT    NOT NULL,
            SourceRecordId INTEGER NULL,
            FailureStage  TEXT    NOT NULL,
            FailureReason TEXT    NOT NULL,
            CreatedAt     TEXT    NOT NULL
        );

        CREATE INDEX IF NOT EXISTS idx_dead_mes_created
            ON dead_mes_records (CreatedAt);
        CREATE INDEX IF NOT EXISTS idx_dead_mes_stage
            ON dead_mes_records (FailureStage, CreatedAt);
    ";

    public MesDeadLetterStore(
        SqliteConnectionFactory connectionFactory,
        ILogService logger)
        : base(connectionFactory, logger)
    {
    }
}
