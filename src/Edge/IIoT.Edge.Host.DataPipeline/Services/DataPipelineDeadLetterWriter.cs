using IIoT.Edge.Module.Contracts.DataPipeline.Stores;
using IIoT.Edge.Module.Contracts.Logging;
using IIoT.Edge.Module.Contracts.DataPipeline;

namespace IIoT.Edge.Host.DataPipeline.Services;

public sealed class DataPipelineDeadLetterWriter : IDataPipelineDeadLetterWriter
{
    public async Task<bool> TryPersistAsync(
        Func<DeadLetterRecord, Task> saveAsync,
        ICriticalPersistenceFallbackWriter criticalFallbackWriter,
        ILogService logger,
        DataPipelineDeadLetterChannel channel,
        string processType,
        string cellDataJson,
        string failedTarget,
        string sourceTable,
        long sourceRecordId,
        DeadLetterStage stage,
        string failureReason,
        FailedCellRecord? sourceRecord = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
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
                CreatedAt = DateTime.UtcNow,
                PlcCode = sourceRecord?.PlcCode ?? string.Empty,
                IdempotencyKeyVersion = sourceRecord?.IdempotencyKeyVersion
                    ?? CloudIdempotencyKeyVersion.LegacyV1,
                NetworkDeviceId = sourceRecord?.NetworkDeviceId,
                DeviceName = sourceRecord?.DeviceName ?? string.Empty,
                ModuleId = sourceRecord?.ModuleId ?? string.Empty,
                TaskKey = sourceRecord?.TaskKey ?? string.Empty,
                PlanSessionId = sourceRecord?.PlanSessionId ?? string.Empty,
                MainPlanCode = sourceRecord?.MainPlanCode ?? string.Empty,
                TraceBatchNumber = sourceRecord?.TraceBatchNumber ?? string.Empty
            }).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();

            var context = sourceRecord is null
                ? $"[CorrelationId=DeadLetter:{channel.DeadLetterName}:{sourceRecordId}]" +
                  "[PlcCode=Unresolved][TaskKey=Unresolved]" +
                  $"[BusinessId=RetryRecord:{sourceRecordId}]"
                : DataPipelineLogContext.Format(sourceRecord);
            logger.Fatal(
                $"{context}[{channel.DeadLetterName}] 工序={processType}，" +
                $"结果=DurableDeadLetter，阶段={stage}，尚未上传成功。");
            return true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            criticalFallbackWriter.Write(
                channel.CriticalSource,
                $"{failureReason} 死信写入失败：{ex.Message}",
                ex);
            return false;
        }
    }
}

/// <summary>
/// 标识当前 deadletter 写入属于哪条补偿链路，避免 Cloud/MES 的日志源和兜底源被散字符串误接。
/// </summary>
public readonly record struct DataPipelineDeadLetterChannel(
    string LogPrefix,
    string DeadLetterName,
    string CriticalSource);
