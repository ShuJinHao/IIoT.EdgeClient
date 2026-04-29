using IIoT.Edge.Application.Abstractions.DataPipeline.Stores;
using IIoT.Edge.Application.Abstractions.Logging;
using IIoT.Edge.SharedKernel.DataPipeline;

namespace IIoT.Edge.Runtime.DataPipeline.Services;

internal static class DataPipelineDeadLetterWriter
{
    public static async Task<bool> TryPersistAsync(
        Func<DeadLetterRecord, Task> saveAsync,
        ICriticalPersistenceFallbackWriter criticalFallbackWriter,
        ILogService logger,
        string logPrefix,
        string deadLetterName,
        string criticalSource,
        string processType,
        string cellDataJson,
        string failedTarget,
        string sourceTable,
        long sourceRecordId,
        DeadLetterStage stage,
        string failureReason)
    {
        try
        {
            await saveAsync(new DeadLetterRecord
            {
                ProcessType = processType,
                CellDataJson = cellDataJson,
                FailedTarget = failedTarget,
                SourceTable = sourceTable,
                SourceRecordId = sourceRecordId,
                FailureStage = stage.ToString(),
                FailureReason = failureReason,
                CreatedAt = DateTime.UtcNow
            }).ConfigureAwait(false);

            logger.Fatal($"[{logPrefix}] {processType} record {sourceRecordId} moved into {deadLetterName} dead-letter store.");
            return true;
        }
        catch (Exception ex)
        {
            criticalFallbackWriter.Write(
                criticalSource,
                $"{failureReason} Dead-letter save failed: {ex.Message}",
                ex);
            return false;
        }
    }
}
