using IIoT.Edge.Application.Abstractions.DataPipeline;
using IIoT.Edge.Application.Abstractions.DataPipeline.Stores;
using IIoT.Edge.Application.Abstractions.Logging;
using IIoT.Edge.Application.Abstractions.Modules;
using IIoT.Edge.SharedKernel.DataPipeline;
using IIoT.Edge.SharedKernel.DataPipeline.CellData;

namespace IIoT.Edge.Runtime.DataPipeline.Services;

internal abstract class RetryDeadLetterServiceBase
{
    private readonly IDeadLetterStore _deadLetterStore;
    private readonly ICriticalPersistenceFallbackWriter _criticalFallbackWriter;
    private readonly IDataPipelineDeadLetterWriter _deadLetterWriter;
    private readonly ICellDataJsonSerializer _cellDataJsonSerializer;

    protected RetryDeadLetterServiceBase(
        ILogService logger,
        IDeadLetterStore deadLetterStore,
        ICriticalPersistenceFallbackWriter criticalFallbackWriter,
        IDataPipelineDeadLetterWriter deadLetterWriter,
        ICellDataJsonSerializer cellDataJsonSerializer,
        DataPipelineDeadLetterChannel deadLetterChannel)
    {
        Logger = logger;
        DeadLetterChannelMetadata = deadLetterChannel;
        _deadLetterStore = deadLetterStore;
        _criticalFallbackWriter = criticalFallbackWriter;
        _deadLetterWriter = deadLetterWriter;
        _cellDataJsonSerializer = cellDataJsonSerializer;
    }

    protected ILogService Logger { get; }

    protected DataPipelineDeadLetterChannel DeadLetterChannelMetadata { get; }

    protected CellDataBase? DeserializeCellData(string processType, string json)
    {
        try
        {
            return _cellDataJsonSerializer.Deserialize(processType, json);
        }
        catch (Exception ex)
        {
            Logger.Error($"[{DeadLetterChannelMetadata.LogPrefix}] CellData 反序列化失败：{ex.Message}");
            return null;
        }
    }

    protected async Task<bool> TryPersistDeadLetterAsync(
        string processType,
        string cellDataJson,
        string failedTarget,
        string sourceTable,
        long sourceRecordId,
        DeadLetterStage stage,
        string failureReason)
        => await _deadLetterWriter.TryPersistAsync(
            _deadLetterStore.SaveAsync,
            _criticalFallbackWriter,
            Logger,
            DeadLetterChannelMetadata,
            processType,
            cellDataJson,
            failedTarget,
            sourceTable,
            sourceRecordId,
            stage,
            failureReason).ConfigureAwait(false);
}
