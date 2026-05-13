using IIoT.Edge.Application.Abstractions.DataPipeline.Stores;
using IIoT.Edge.Application.Abstractions.Logging;
using IIoT.Edge.SharedKernel.DataPipeline;

namespace IIoT.Edge.Runtime.DataPipeline.Services;

/// <summary>
/// 统一构造 deadletter 记录和最终文件兜底，但具体写入哪个 Cloud/MES store 由调用方传入。
/// </summary>
public interface IDataPipelineDeadLetterWriter
{
    Task<bool> TryPersistAsync(
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
        string failureReason);
}

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

            logger.Fatal($"[{channel.LogPrefix}] {processType} 记录 {sourceRecordId} 已进入 {channel.DeadLetterName} 死信表。");
            return true;
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
