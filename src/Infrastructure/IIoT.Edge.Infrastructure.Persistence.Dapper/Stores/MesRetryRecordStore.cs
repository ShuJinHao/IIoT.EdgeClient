using IIoT.Edge.Application.Abstractions.DataPipeline.Stores;
using IIoT.Edge.Application.Abstractions.Logging;
using IIoT.Edge.Infrastructure.Persistence.Dapper.Connection;

namespace IIoT.Edge.Infrastructure.Persistence.Dapper.Stores;

public sealed class MesRetryRecordStore : RetryRecordStoreBase, IMesRetryRecordStore
{
    public override string DbName => "pipeline_mes";
    protected override string TableName => "failed_mes_records";
    protected override string ChannelName => "MES";

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
    ";

    public MesRetryRecordStore(
        SqliteConnectionFactory connectionFactory,
        ILogService logger)
        : base(connectionFactory, logger)
    {
    }
}
