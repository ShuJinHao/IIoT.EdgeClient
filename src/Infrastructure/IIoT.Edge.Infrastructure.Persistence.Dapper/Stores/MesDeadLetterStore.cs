using IIoT.Edge.Application.Abstractions.DataPipeline.Stores;
using IIoT.Edge.Application.Abstractions.Logging;
using IIoT.Edge.Infrastructure.Persistence.Dapper.Connection;

namespace IIoT.Edge.Infrastructure.Persistence.Dapper.Stores;

public sealed class MesDeadLetterStore : DeadLetterStoreBase, IMesDeadLetterStore
{
    public override string DbName => "pipeline_mes";

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
