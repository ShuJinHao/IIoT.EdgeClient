using IIoT.Edge.Module.Contracts.DataPipeline.Stores;
using IIoT.Edge.Module.Contracts.Logging;
using IIoT.Edge.Infrastructure.Persistence.Dapper.Connection;
using IIoT.Edge.Module.Contracts.DataPipeline.CellData;
using IIoT.Edge.Application.Features.DataPipeline.DeadLetters;
using IIoT.Edge.Module.Contracts.DataPipeline;

using IIoT.Edge.Module.Contracts.Cloud;
using IIoT.Edge.Application.Common.Identity;
using IIoT.Edge.Application.Common.DataPipeline;
namespace IIoT.Edge.Infrastructure.Persistence.Dapper.Stores;

public sealed class CloudRetryRecordStore :
    RetryRecordStoreBase,
    ICloudRetryRecordStore,
    ICloudDeadLetterRequeueStore,
    ICloudRetryDeadLetterTransitionStore
{
    public override string DbName => "pipeline_cloud";
    protected override string TableName => "failed_cloud_records";
    protected override string ChannelName => "Cloud";
    protected override string ClaimTableName => "failed_cloud_record_claims";
    protected override string DeadLetterTableName => "dead_cloud_records";

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

        CREATE TABLE IF NOT EXISTS failed_cloud_record_claims (
            RecordId    INTEGER PRIMARY KEY,
            ClaimToken  TEXT    NOT NULL,
            ClaimedAt   TEXT    NOT NULL
        );
        CREATE INDEX IF NOT EXISTS idx_failed_cloud_claim_token
            ON failed_cloud_record_claims (ClaimToken);
        CREATE INDEX IF NOT EXISTS idx_failed_cloud_claim_time
            ON failed_cloud_record_claims (ClaimedAt);
    ";

    public CloudRetryRecordStore(
        SqliteConnectionFactory connectionFactory,
        ILogService logger,
        ICellDataJsonSerializer cellDataJsonSerializer,
        IDevicePluginRuntimeContext? runtimeContext = null,
        DataPipelineRetryScheduleOptions? retryScheduleOptions = null)
        : base(connectionFactory, logger, cellDataJsonSerializer, runtimeContext, retryScheduleOptions)
    {
    }

    public Task RequeueAndRemoveAsync(
        long deadLetterId,
        string operatorId,
        string businessIdentifier,
        CancellationToken cancellationToken = default)
        => RequeueAndRemoveDeadLetterCoreAsync(
            deadLetterId,
            operatorId,
            businessIdentifier,
            cancellationToken);

    public Task MoveExhaustedRetryToDeadLetterAsync(
        FailedCellRecord sourceRecord,
        int finalRetryCount,
        string failureReason,
        CancellationToken cancellationToken = default)
        => MoveExhaustedRetryToDeadLetterCoreAsync(
            sourceRecord,
            finalRetryCount,
            failureReason,
            cancellationToken);
}
