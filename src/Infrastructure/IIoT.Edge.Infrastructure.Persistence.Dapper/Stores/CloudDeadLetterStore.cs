using IIoT.Edge.Application.Abstractions.DataPipeline.Stores;
using IIoT.Edge.Application.Abstractions.Logging;
using IIoT.Edge.Infrastructure.Persistence.Dapper.Connection;

namespace IIoT.Edge.Infrastructure.Persistence.Dapper.Stores;

public sealed class CloudDeadLetterStore : DeadLetterStoreBase, ICloudDeadLetterStore
{
    public override string DbName => "pipeline_cloud";

    protected override string TableName => "dead_cloud_records";

    protected override string CreateTableSql => @"
        CREATE TABLE IF NOT EXISTS dead_cloud_records (
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

        CREATE INDEX IF NOT EXISTS idx_dead_cloud_created
            ON dead_cloud_records (CreatedAt);
        CREATE INDEX IF NOT EXISTS idx_dead_cloud_stage
            ON dead_cloud_records (FailureStage, CreatedAt);
    ";

    public CloudDeadLetterStore(
        SqliteConnectionFactory connectionFactory,
        ILogService logger)
        : base(connectionFactory, logger)
    {
    }
}
