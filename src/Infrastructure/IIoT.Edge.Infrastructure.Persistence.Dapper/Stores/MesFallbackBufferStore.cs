using IIoT.Edge.Application.Abstractions.DataPipeline.Stores;
using IIoT.Edge.Application.Abstractions.Logging;
using IIoT.Edge.Infrastructure.Persistence.Dapper.Connection;
using IIoT.Edge.SharedKernel.DataPipeline;

namespace IIoT.Edge.Infrastructure.Persistence.Dapper.Stores;

public class MesFallbackBufferStore : FallbackBufferStoreBase<MesFallbackRecord>, IMesFallbackBufferStore
{
    public override string DbName => "pipeline_mes";
    protected override string TableName => "mes_fallback_records";
    protected override string ChannelName => "MES";
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
        ILogService logger)
        : base(connectionFactory, logger)
    {
    }
}
