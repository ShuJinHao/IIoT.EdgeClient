namespace IIoT.Edge.Application.Common.DataPipeline;

public interface IDataPipelineIdentityMigration
{
    Task<DataPipelineIdentityMigrationResult> MigrateAsync(
        CancellationToken cancellationToken = default);
}

public sealed record DataPipelineIdentityMigrationResult(
    int MigratedRecordCount,
    IReadOnlyList<DataPipelineIdentityMigrationIssue> Issues);

public sealed record DataPipelineIdentityMigrationIssue(
    string DatabaseName,
    string TableName,
    long RecordId,
    string PlcCode,
    int? NetworkDeviceId,
    string DeviceName,
    string TaskKey,
    string DiagnosticCode,
    string DiagnosticMessage);
