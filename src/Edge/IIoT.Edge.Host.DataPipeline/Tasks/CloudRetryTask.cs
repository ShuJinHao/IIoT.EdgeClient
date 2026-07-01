using IIoT.Edge.Application.Abstractions.Cloud;
using IIoT.Edge.Application.Abstractions.Config;
using IIoT.Edge.Application.Abstractions.Device;
using IIoT.Edge.Application.Abstractions.Logging;
using IIoT.Edge.Application.Abstractions.Modules;
using IIoT.Edge.Host.DataPipeline.Services;
using IIoT.Edge.SharedKernel.DataPipeline;

namespace IIoT.Edge.Host.DataPipeline.Tasks;

public sealed class CloudRetryTask : RetryTaskBase<CloudRetryRuntimeState, CloudRetryProcessResult>
{
    private readonly IDeviceService _deviceService;
    private readonly IDeviceLogSyncTask _deviceLogSync;
    private readonly ICapacitySyncTask _capacitySync;
    private readonly ILocalSystemRuntimeConfigService? _runtimeConfig;
    private readonly ICloudUploadDiagnosticsStore _diagnosticsStore;
    private readonly DataPipelineCapacityGuard _capacityGuard;
    private readonly ICloudRetryHousekeepingService _housekeepingService;

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
        : base(
            logger,
            diagnosticsStore,
            fallbackRecoveryService,
            retryRecordProcessor,
            housekeepingService,
            CloudRetryRuntimeState.Retrying)
    {
        _deviceService = deviceService;
        _deviceLogSync = deviceLogSync;
        _capacitySync = capacitySync;
        _runtimeConfig = runtimeConfig;
        _diagnosticsStore = diagnosticsStore;
        _capacityGuard = capacityGuard;
        _housekeepingService = housekeepingService;
    }

    protected override bool SetRetryingBeforeRecovery => true;

    protected override ValueTask<RetryTaskAvailability> CheckAvailabilityAsync(CancellationToken cancellationToken)
    {
        if (_runtimeConfig?.Current.SystemCloudEnabled == false)
        {
            return ValueTask.FromResult(RetryTaskAvailability.Unavailable(CloudRetryRuntimeState.Idle));
        }

        if (!_deviceService.CanUploadToCloud)
        {
            return ValueTask.FromResult(RetryTaskAvailability.Unavailable(CloudRetryRuntimeState.WaitingForRecovery));
        }

        return ValueTask.FromResult(RetryTaskAvailability.Available());
    }

    protected override Task<bool> HandleRetryResultAsync(CloudRetryProcessResult result)
    {
        if (result == CloudRetryProcessResult.PauseForRecovery)
        {
            SetRuntimeState(CloudRetryRuntimeState.WaitingForRecovery);
            return Task.FromResult(false);
        }

        return Task.FromResult(result != CloudRetryProcessResult.Failed);
    }

    protected override async Task<bool> AfterRetryProcessingAsync()
    {
        var deviceLogSnapshotBefore = _diagnosticsStore.Snapshot;
        var retriedLogs = await _deviceLogSync.RetryBufferAsync().ConfigureAwait(false);
        if (!retriedLogs)
        {
            if (_housekeepingService.DidPauseForRecovery(deviceLogSnapshotBefore, "DeviceLog"))
            {
                SetRuntimeState(CloudRetryRuntimeState.WaitingForRecovery);
                return false;
            }

            Logger.Warn("[云端补传] 设备日志缓冲补传已暂停或失败。");
        }

        var capacitySnapshotBefore = _diagnosticsStore.Snapshot;
        var retriedCapacity = await _capacitySync.RetryBufferAsync().ConfigureAwait(false);
        if (!retriedCapacity)
        {
            if (_housekeepingService.DidPauseForRecovery(capacitySnapshotBefore, "Capacity"))
            {
                SetRuntimeState(CloudRetryRuntimeState.WaitingForRecovery);
                return false;
            }

            Logger.Warn("[云端补传] 产能缓冲补传已暂停或失败。");
        }

        return true;
    }

    protected override async Task RefreshCapacityStatusAsync()
    {
        await _capacityGuard.RefreshCloudRetryCapacityStatusAsync().ConfigureAwait(false);
        await _capacityGuard.RefreshCloudFallbackCapacityStatusAsync().ConfigureAwait(false);
    }
}
