using IIoT.Edge.Application.Abstractions.DataPipeline;
using IIoT.Edge.Application.Abstractions.DataPipeline.Stores;
using IIoT.Edge.Application.Abstractions.Logging;
using IIoT.Edge.SharedKernel.DataPipeline;
using IIoT.Edge.SharedKernel.DataPipeline.CellData;

using IIoT.Edge.Application.Abstractions.Mes;
namespace IIoT.Edge.Host.DataPipeline.Services;

internal sealed class MesFallbackRecoveryService
    : FallbackRecoveryServiceBase<MesFallbackRecord>, IMesFallbackRecoveryService
{
    private static readonly DataPipelineDeadLetterChannel DeadLetterChannel = new(
        LogPrefix: "MES补传",
        DeadLetterName: "MES",
        CriticalSource: "Retry.MesDeadLetterPersistFailed");

    private readonly DataPipelineCapacityGuard _capacityGuard;

    public MesFallbackRecoveryService(
        ILogService logger,
        IMesFallbackBufferStore fallbackStore,
        IMesDeadLetterStore deadLetterStore,
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

    protected override string ChannelName => "MES";

    protected override string SourceTable => "mes_fallback_records";

    protected override Task<string?> GetRetryBlockReasonAsync(string processType)
        => _capacityGuard.GetMesRetryBlockReasonAsync(processType);

    protected override Task RefreshFallbackCapacityStatusAsync()
        => _capacityGuard.RefreshMesFallbackCapacityStatusAsync();
}
