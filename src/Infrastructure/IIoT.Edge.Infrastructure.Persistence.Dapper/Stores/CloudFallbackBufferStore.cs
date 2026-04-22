using IIoT.Edge.Application.Abstractions.DataPipeline.Stores;
using IIoT.Edge.Application.Abstractions.Logging;
using IIoT.Edge.Infrastructure.Persistence.Dapper.Connection;
using IIoT.Edge.SharedKernel.DataPipeline;

namespace IIoT.Edge.Infrastructure.Persistence.Dapper.Stores;

public class CloudFallbackBufferStore : FallbackBufferStoreBase<CloudFallbackRecord>, ICloudFallbackBufferStore
{
    public override string DbName => "pipeline_cloud";
    protected override string TableName => "cloud_fallback_records";
    protected override string ChannelName => "Cloud";
    protected override string RetryTableName => "failed_cloud_records";

    protected override string CreateTableSql => @"
        CREATE TABLE IF NOT EXISTS cloud_fallback_records (
            Id            INTEGER PRIMARY KEY AUTOINCREMENT,
            ProcessType   TEXT    NOT NULL,
            CellDataJson  TEXT    NOT NULL,
            FailedTarget  TEXT    NOT NULL,
            ErrorMessage  TEXT    NOT NULL,
            CreatedAt     TEXT    NOT NULL
        );

        CREATE INDEX IF NOT EXISTS idx_cloud_fallback_created
            ON cloud_fallback_records (CreatedAt);
    ";

    public CloudFallbackBufferStore(
        SqliteConnectionFactory connectionFactory,
        ILogService logger)
        : base(connectionFactory, logger)
    {
    }
}
