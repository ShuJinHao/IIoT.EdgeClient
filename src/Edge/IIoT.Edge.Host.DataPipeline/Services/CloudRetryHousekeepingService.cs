using IIoT.Edge.Module.Contracts.Device;
using IIoT.Edge.Module.Contracts.DataPipeline;
using IIoT.Edge.Module.Contracts.DataPipeline.Stores;
using IIoT.Edge.Module.Contracts.Logging;
using IIoT.Edge.Module.Contracts.Modules;

using IIoT.Edge.Module.Contracts.Cloud;
namespace IIoT.Edge.Host.DataPipeline.Services;

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
            DataPipelineRetryChannelMetadata.CreateDeadLetterChannel(DataPipelineRetryChannel.Cloud).LogPrefix,
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
