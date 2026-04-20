using IIoT.Edge.Application.Abstractions.DataPipeline.Stores;
using IIoT.Edge.Application.Abstractions.Logging;
using IIoT.Edge.Infrastructure.Persistence.Dapper.Connection;

namespace IIoT.Edge.Infrastructure.Persistence.Dapper.Stores;

public sealed class CloudRetryRecordStore : RetryRecordStoreBase, ICloudRetryRecordStore
{
    public override string DbName => "pipeline_cloud";
    protected override string TableName => "failed_cloud_records";
    protected override string ChannelName => "Cloud";

    protected override string CreateTableSql => @"
        CREATE TABLE IF NOT EXISTS failed_cloud_records (
            Id              INTEGER PRIMARY KEY AUTOINCREMENT,
            ProcessType     TEXT    NOT NULL,
            CellDataJson    TEXT    NOT NULL,
            FailedTarget    TEXT    NOT NULL,
            ErrorMessage    TEXT    NOT NULL,
            RetryCount      INTEGER NOT NULL DEFAULT 0,
            NextRetryTime   TEXT    NOT NULL,
            CreatedAt       TEXT    NOT NULL
        );

        CREATE INDEX IF NOT EXISTS idx_failed_cloud_retry
            ON failed_cloud_records (NextRetryTime);
        CREATE INDEX IF NOT EXISTS idx_failed_cloud_target_retry
            ON failed_cloud_records (FailedTarget, NextRetryTime);
        CREATE INDEX IF NOT EXISTS idx_failed_cloud_process_retry
            ON failed_cloud_records (ProcessType, NextRetryTime);
    ";

    public CloudRetryRecordStore(
        SqliteConnectionFactory connectionFactory,
        ILogService logger)
        : base(connectionFactory, logger)
    {
    }
}
