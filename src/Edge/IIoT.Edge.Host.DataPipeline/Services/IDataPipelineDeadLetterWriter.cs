using IIoT.Edge.Application.Abstractions.DataPipeline.Stores;
using IIoT.Edge.Application.Abstractions.Logging;
using IIoT.Edge.SharedKernel.DataPipeline;

namespace IIoT.Edge.Host.DataPipeline.Services;

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
        string failureReason,
        FailedCellRecord? sourceRecord = null);
}
