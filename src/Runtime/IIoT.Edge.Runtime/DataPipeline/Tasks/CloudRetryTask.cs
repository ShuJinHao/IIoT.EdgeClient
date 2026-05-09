using IIoT.Edge.Application.Abstractions.DataPipeline.SyncTask;
using IIoT.Edge.Application.Abstractions.Config;
using IIoT.Edge.Application.Abstractions.Device;
using IIoT.Edge.Application.Abstractions.Logging;
using IIoT.Edge.Application.Abstractions.Modules;
using IIoT.Edge.Runtime.Base;
using IIoT.Edge.Runtime.DataPipeline.Services;
using IIoT.Edge.SharedKernel.DataPipeline;

namespace IIoT.Edge.Runtime.DataPipeline.Tasks;

public sealed class CloudRetryTask : ScheduledTaskBase
{
    private readonly IDeviceService _deviceService;
    private readonly IDeviceLogSyncTask _deviceLogSync;
    private readonly ICapacitySyncTask _capacitySync;
    private readonly ILocalSystemRuntimeConfigService? _runtimeConfig;
    private readonly ICloudUploadDiagnosticsStore _diagnosticsStore;
    private readonly DataPipelineCapacityGuard _capacityGuard;
    private readonly ICloudFallbackRecoveryService _fallbackRecoveryService;
    private readonly ICloudRetryRecordProcessor _retryRecordProcessor;
    private readonly ICloudRetryHousekeepingService _housekeepingService;
    private bool _wasUnavailable = true;

    public override string TaskName => "CloudRetryTask";
    protected override int ExecuteInterval => 5000;

    public CloudRetryTask(
        ILogService logger,
        IDeviceService deviceService,
        IDeviceLogSyncTask deviceLogSync,
        ICapacitySyncTask capacitySync,
        ICloudUploadDiagnosticsStore diagnosticsStore,
        DataPipelineCapacityGuard capacityGuard,
        ICloudFallbackRecoveryService fallbackRecoveryService,
        ICloudRetryRecordProcessor retryRecordProcessor,
        ICloudRetryHousekeepingService housekeepingService,
        ILocalSystemRuntimeConfigService? runtimeConfig = null)
        : base(logger)
    {
        _deviceService = deviceService;
        _deviceLogSync = deviceLogSync;
        _capacitySync = capacitySync;
        _runtimeConfig = runtimeConfig;
        _diagnosticsStore = diagnosticsStore;
        _capacityGuard = capacityGuard;
        _fallbackRecoveryService = fallbackRecoveryService;
        _retryRecordProcessor = retryRecordProcessor;
        _housekeepingService = housekeepingService;
    }

    internal Task ExecuteOneIterationAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        return ExecuteAsync().WaitAsync(ct);
    }

    protected override async Task ExecuteAsync()
    {
        if (_runtimeConfig?.Current.CloudUploadEnabled == false)
        {
            _diagnosticsStore.SetRuntimeState(CloudRetryRuntimeState.Idle);
            _wasUnavailable = true;
            return;
        }

        if (!_deviceService.CanUploadToCloud)
        {
            _diagnosticsStore.SetRuntimeState(CloudRetryRuntimeState.WaitingForRecovery);
            _wasUnavailable = true;
            return;
        }

        _diagnosticsStore.SetRuntimeState(CloudRetryRuntimeState.Retrying);

        if (_wasUnavailable)
        {
            _wasUnavailable = false;
            await _housekeepingService.RecoverAbandonedRecordsAsync().ConfigureAwait(false);
        }

        await _housekeepingService.CleanupExpiredAbandonedRecordsAsync().ConfigureAwait(false);
        await _fallbackRecoveryService.RecoverAsync().ConfigureAwait(false);

        var retryResult = await _retryRecordProcessor.ProcessAsync(CurrentCancellationToken).ConfigureAwait(false);
        if (retryResult == CloudRetryProcessResult.PauseForRecovery)
        {
            _diagnosticsStore.SetRuntimeState(CloudRetryRuntimeState.WaitingForRecovery);
            return;
        }

        if (retryResult == CloudRetryProcessResult.Failed)
        {
            return;
        }

        var deviceLogSnapshotBefore = _diagnosticsStore.Snapshot;
        var retriedLogs = await _deviceLogSync.RetryBufferAsync().ConfigureAwait(false);
        if (!retriedLogs)
        {
            if (_housekeepingService.DidPauseForRecovery(deviceLogSnapshotBefore, "DeviceLog"))
            {
                _diagnosticsStore.SetRuntimeState(CloudRetryRuntimeState.WaitingForRecovery);
                return;
            }

            Logger.Warn("[Retry-Cloud] 设备日志缓冲补传已暂停或失败。");
        }

        var capacitySnapshotBefore = _diagnosticsStore.Snapshot;
        var retriedCapacity = await _capacitySync.RetryBufferAsync().ConfigureAwait(false);
        if (!retriedCapacity)
        {
            if (_housekeepingService.DidPauseForRecovery(capacitySnapshotBefore, "Capacity"))
            {
                _diagnosticsStore.SetRuntimeState(CloudRetryRuntimeState.WaitingForRecovery);
                return;
            }

            Logger.Warn("[Retry-Cloud] 产能缓冲补传已暂停或失败。");
        }

        await _capacityGuard.RefreshCloudRetryCapacityStatusAsync().ConfigureAwait(false);
        await _capacityGuard.RefreshCloudFallbackCapacityStatusAsync().ConfigureAwait(false);

        await _housekeepingService.ApplyIdleOrBackoffStateAsync().ConfigureAwait(false);
    }
}
