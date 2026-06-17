using IIoT.Edge.Application.Abstractions.DataPipeline;
using IIoT.Edge.Application.Abstractions.DataPipeline.Stores;
using IIoT.Edge.Application.Abstractions.Logging;
using IIoT.Edge.SharedKernel.DataPipeline;
using IIoT.Edge.SharedKernel.DataPipeline.CellData;

using IIoT.Edge.Application.Abstractions.Cloud;
namespace IIoT.Edge.Host.DataPipeline.Services;

internal sealed class CloudFallbackRecoveryService
    : FallbackRecoveryServiceBase<CloudFallbackRecord>, ICloudFallbackRecoveryService
{
    private static readonly DataPipelineDeadLetterChannel DeadLetterChannel = new(
        LogPrefix: "Retry-Cloud",
        DeadLetterName: "Cloud",
        CriticalSource: "Retry.CloudDeadLetterPersistFailed");

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

    protected override string SourceTable => "cloud_fallback_records";

    protected override Task<string?> GetRetryBlockReasonAsync(string processType)
        => _capacityGuard.GetCloudRetryBlockReasonAsync(processType);

    protected override Task RefreshFallbackCapacityStatusAsync()
        => _capacityGuard.RefreshCloudFallbackCapacityStatusAsync();
}
