using IIoT.Edge.Application.Common.DataPipeline;
using IIoT.Edge.Module.Contracts.Config;
using IIoT.Edge.Module.Contracts.Logging;
using IIoT.Edge.Module.Contracts.Modules;
using IIoT.Edge.Host.DataPipeline.Services;

using IIoT.Edge.Module.Contracts.Mes;
using IIoT.Edge.Module.Contracts.Shared;
namespace IIoT.Edge.Host.DataPipeline.Tasks;

/// <summary>
/// MES 独立补传任务。只处理 MES retry/fallback/deadletter，不复用云端补偿队列。
/// </summary>
public sealed class MesRetryTask : RetryTaskBase<MesRetryRuntimeState, MesRetryProcessResult>
{
    private readonly ILocalSystemRuntimeConfigService _runtimeConfig;
    private readonly DataPipelineCapacityGuard _capacityGuard;
    private readonly IExternalHeartbeatStateStore _heartbeatStateStore;

    public override string TaskName => "MesRetryTask";
    protected override int ExecuteInterval => 5000;

    public MesRetryTask(
        ILogService logger,
        ILocalSystemRuntimeConfigService runtimeConfig,
        IMesRetryDiagnosticsStore diagnosticsStore,
        DataPipelineCapacityGuard capacityGuard,
        IExternalHeartbeatStateStore heartbeatStateStore,
        IMesFallbackRecoveryService fallbackRecoveryService,
        IMesRetryRecordProcessor retryRecordProcessor,
        IMesRetryHousekeepingService housekeepingService,
        DataPipelineRetryScheduleOptions? scheduleOptions = null)
        : base(
            logger,
            diagnosticsStore,
            fallbackRecoveryService,
            retryRecordProcessor,
            housekeepingService,
            MesRetryRuntimeState.Retrying,
            scheduleOptions)
    {
        ArgumentNullException.ThrowIfNull(capacityGuard);
        ArgumentNullException.ThrowIfNull(heartbeatStateStore);
        ArgumentNullException.ThrowIfNull(fallbackRecoveryService);
        ArgumentNullException.ThrowIfNull(retryRecordProcessor);
        ArgumentNullException.ThrowIfNull(housekeepingService);

        _runtimeConfig = runtimeConfig;
        _capacityGuard = capacityGuard;
        _heartbeatStateStore = heartbeatStateStore;
    }

    protected override ValueTask<RetryTaskAvailability> CheckAvailabilityAsync(CancellationToken cancellationToken)
    {
        // MES 总开关关闭时不能领取补传记录，只刷新容量状态，保留 backlog 等待后续恢复。
        if (!_runtimeConfig.Current.MesUploadEnabled)
        {
            return ValueTask.FromResult(RetryTaskAvailability.Unavailable(
                MesRetryRuntimeState.Idle,
                refreshCapacityBeforeState: true));
        }

        // MES 心跳未恢复前不调用 MES 上传接口，也不把本地 retry/fallback 记录挪动到其他链路。
        if (!IsMesHeartbeatReady())
        {
            return ValueTask.FromResult(RetryTaskAvailability.Unavailable(MesRetryRuntimeState.Backoff));
        }

        return ValueTask.FromResult(RetryTaskAvailability.Available());
    }

    protected override async Task<bool> HandleRetryResultAsync(MesRetryProcessResult result)
    {
        if (result != MesRetryProcessResult.Failed)
        {
            return true;
        }

        await RefreshCapacityStatusAsync().ConfigureAwait(false);
        SetRuntimeState(MesRetryRuntimeState.LastFailed);
        return false;
    }

    protected override async Task RefreshCapacityStatusAsync()
    {
        await _capacityGuard.RefreshMesRetryCapacityStatusAsync().ConfigureAwait(false);
        await _capacityGuard.RefreshMesFallbackCapacityStatusAsync().ConfigureAwait(false);
    }

    private bool IsMesHeartbeatReady()
        => _heartbeatStateStore.Get(ExternalSystemKind.Mes).IsReady;
}
