using IIoT.Edge.Application.Abstractions.Device;
using IIoT.Edge.Application.Abstractions.DataPipeline.Stores;
using IIoT.Edge.Application.Abstractions.Logging;
using IIoT.Edge.Application.Abstractions.Modules;

namespace IIoT.Edge.Runtime.DataPipeline.Services;

public interface ICloudRetryHousekeepingService : IRetryTaskHousekeepingService
{
    bool DidPauseForRecovery(CloudUploadDiagnosticsSnapshot previousSnapshot, string processType);
}

internal sealed class CloudRetryHousekeepingService
    : RetryHousekeepingServiceBase<CloudRetryRuntimeState>, ICloudRetryHousekeepingService
{
    private readonly ICloudUploadDiagnosticsStore _diagnosticsStore;

    public CloudRetryHousekeepingService(
        ILogService logger,
        ICloudRetryRecordStore retryStore,
        ICloudUploadDiagnosticsStore diagnosticsStore)
        : base(
            logger,
            retryStore,
            diagnosticsStore,
            "Retry-Cloud",
            "云端上传门控已恢复，弃置记录已重置为可补传。",
            CloudRetryRuntimeState.Idle,
            CloudRetryRuntimeState.Backoff)
    {
        _diagnosticsStore = diagnosticsStore;
    }

    public bool DidPauseForRecovery(CloudUploadDiagnosticsSnapshot previousSnapshot, string processType)
    {
        var currentSnapshot = _diagnosticsStore.Snapshot;
        if (!string.Equals(currentSnapshot.LastProcessType, processType, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (currentSnapshot.LastAttemptAt == previousSnapshot.LastAttemptAt)
        {
            return false;
        }

        return ShouldPauseForRecovery(currentSnapshot);
    }

    private static bool ShouldPauseForRecovery(CloudUploadDiagnosticsSnapshot snapshot)
        => snapshot.LastOutcome is CloudCallOutcome.SkippedUploadNotReady or CloudCallOutcome.UnauthorizedAfterRetry;
}
