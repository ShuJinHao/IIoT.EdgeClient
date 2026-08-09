using IIoT.Edge.Module.Contracts.DataPipeline;

namespace IIoT.Edge.Infrastructure.Integration;

/// <summary>
/// Distinguishes legacy v2 retry/fallback records from v3 records without treating the
/// shared ProcessType field as a v3 discriminator. Legacy persistence always carried a
/// process type, while ClientCode, CompletionId and TypeKey were introduced together by v3.
/// </summary>
internal static class DataPipelineRecordIdentityClassifier
{
    public static DataPipelineRecordIdentityKind Classify(CellCompletedRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);

        var hasClientCode = !string.IsNullOrWhiteSpace(record.ClientCode);
        var hasCompletionId = !string.IsNullOrWhiteSpace(record.CompletionId);
        var hasTypeKey = !string.IsNullOrWhiteSpace(record.TypeKey);
        var hasAnyV3Marker = hasClientCode || hasCompletionId || hasTypeKey;
        if (!hasAnyV3Marker)
        {
            return DataPipelineRecordIdentityKind.LegacyV2;
        }

        return hasClientCode
               && hasCompletionId
               && hasTypeKey
               && !string.IsNullOrWhiteSpace(record.ProcessType)
               && !string.IsNullOrWhiteSpace(record.ModuleId)
            ? DataPipelineRecordIdentityKind.CompleteV3
            : DataPipelineRecordIdentityKind.IncompleteV3;
    }
}

internal enum DataPipelineRecordIdentityKind
{
    LegacyV2,
    CompleteV3,
    IncompleteV3
}
