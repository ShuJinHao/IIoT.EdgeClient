using IIoT.Edge.Application.Abstractions.DataPipeline.Stores;
using IIoT.Edge.Application.Abstractions.Logging;
using IIoT.Edge.Application.Abstractions.Modules;

using IIoT.Edge.Application.Abstractions.Mes;
namespace IIoT.Edge.Host.DataPipeline.Services;

internal sealed class MesRetryHousekeepingService
    : RetryHousekeepingServiceBase<MesRetryRuntimeState>, IMesRetryHousekeepingService
{
    public MesRetryHousekeepingService(
        ILogService logger,
        IMesRetryRecordStore retryStore,
        IMesRetryDiagnosticsStore diagnosticsStore)
        : base(
            logger,
            retryStore,
            diagnosticsStore,
            "Retry-MES",
            "MES 心跳已恢复，弃置记录已重置为可补传。",
            MesRetryRuntimeState.Idle,
            MesRetryRuntimeState.Backoff)
    {
    }
}
