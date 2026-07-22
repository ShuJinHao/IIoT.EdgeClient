using IIoT.Edge.Module.Contracts.DataPipeline;
using IIoT.Edge.Module.Contracts.DataPipeline.Stores;
using IIoT.Edge.Module.Contracts.Logging;
using IIoT.Edge.Module.Contracts.DataPipeline;
using IIoT.Edge.Module.Contracts.DataPipeline.CellData;

using IIoT.Edge.Module.Contracts.Cloud;
namespace IIoT.Edge.Host.DataPipeline.Services;

internal sealed class CloudFallbackRecoveryService
    : FallbackRecoveryServiceBase<CloudFallbackRecord>, ICloudFallbackRecoveryService
{
    private static readonly DataPipelineDeadLetterChannel DeadLetterChannel =
        DataPipelineRetryChannelMetadata.CreateDeadLetterChannel(DataPipelineRetryChannel.Cloud);

    private readonly DataPipelineCapacityGuard _capacityGuard;

    public CloudFallbackRecoveryService(
        ILogService logger,
        ICloudFallbackBufferStore fallbackStore,
        ICloudDeadLetterStore deadLetterStore,
        ICriticalPersistenceFallbackWriter criticalFallbackWriter,
        DataPipelineCapacityGuard capacityGuard,
        IDataPipelineDeadLetterWriter deadLetterWriter,
        ICellDataJsonSerializer cellDataJsonSerializer)
        : base(
            logger,
            fallbackStore,
            deadLetterStore,
            criticalFallbackWriter,
            deadLetterWriter,
            cellDataJsonSerializer,
            DeadLetterChannel)
    {
        _capacityGuard = capacityGuard;
    }

    protected override string ChannelName => "Cloud";

    protected override string SourceTable =>
        DataPipelineRetryChannelMetadata.GetFallbackRecordSourceTable(DataPipelineRetryChannel.Cloud);

    protected override Task<string?> GetRetryBlockReasonAsync(string processType)
        => _capacityGuard.GetCloudRetryBlockReasonAsync(processType);

    protected override Task RefreshFallbackCapacityStatusAsync()
        => _capacityGuard.RefreshCloudFallbackCapacityStatusAsync();
}
