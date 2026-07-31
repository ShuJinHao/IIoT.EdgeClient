using IIoT.Edge.Module.Contracts.DataPipeline.Stores;
using IIoT.Edge.Module.Contracts.Logging;
using IIoT.Edge.Module.Contracts.Modules;

using IIoT.Edge.Module.Contracts.Mes;
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
            "MES补传",
            "MES 心跳已恢复，弃置记录已重置为可补传。",
            MesRetryRuntimeState.Idle,
            MesRetryRuntimeState.Backoff)
    {
    }
}
