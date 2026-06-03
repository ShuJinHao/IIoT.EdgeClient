using IIoT.Edge.Application.Abstractions.Config;
using IIoT.Edge.Application.Abstractions.Integration;
using IIoT.Edge.Application.Abstractions.Logging;
using IIoT.Edge.Application.Abstractions.Modules;
using IIoT.Edge.Runtime.Base;
using IIoT.Edge.Runtime.DataPipeline.Services;

namespace IIoT.Edge.Runtime.DataPipeline.Tasks;

/// <summary>
/// MES 独立补传任务。只处理 MES retry/fallback/deadletter，不复用云端补偿队列。
/// </summary>
public sealed class MesRetryTask : ScheduledTaskBase
{
    private readonly ILocalSystemRuntimeConfigService _runtimeConfig;
    private readonly IMesRetryDiagnosticsStore _diagnosticsStore;
    private readonly DataPipelineCapacityGuard _capacityGuard;
    private readonly IExternalHeartbeatStateStore _heartbeatStateStore;
    private readonly IMesFallbackRecoveryService _fallbackRecoveryService;
    private readonly IMesRetryRecordProcessor _retryRecordProcessor;
    private readonly IMesRetryHousekeepingService _housekeepingService;
    private bool _wasUnavailable = true;

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
        IMesRetryHousekeepingService housekeepingService)
        : base(logger)
    {
        ArgumentNullException.ThrowIfNull(capacityGuard);
        ArgumentNullException.ThrowIfNull(heartbeatStateStore);
        ArgumentNullException.ThrowIfNull(fallbackRecoveryService);
        ArgumentNullException.ThrowIfNull(retryRecordProcessor);
        ArgumentNullException.ThrowIfNull(housekeepingService);

        _runtimeConfig = runtimeConfig;
        _diagnosticsStore = diagnosticsStore;
        _capacityGuard = capacityGuard;
        _heartbeatStateStore = heartbeatStateStore;
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
        // MES 总开关关闭时不能领取补传记录，只刷新容量状态，保留 backlog 等待后续恢复。
        if (!_runtimeConfig.Current.MesUploadEnabled)
        {
            await RefreshMesCapacityStatusAsync().ConfigureAwait(false);

            _diagnosticsStore.SetRuntimeState(MesRetryRuntimeState.Idle);
            _wasUnavailable = true;
            return;
        }

        // MES 心跳未恢复前不调用 MES 上传接口，也不把本地 retry/fallback 记录挪动到其他链路。
        if (!IsMesHeartbeatReady())
        {
            _diagnosticsStore.SetRuntimeState(MesRetryRuntimeState.Backoff);
            _wasUnavailable = true;
            return;
        }

        if (_wasUnavailable)
        {
            await _housekeepingService.RecoverAbandonedRecordsAsync().ConfigureAwait(false);
            _wasUnavailable = false;
        }

        await _housekeepingService.CleanupExpiredAbandonedRecordsAsync().ConfigureAwait(false);

        // 心跳恢复后先尝试把 fallback 搬回 MES retry，再领取 retry 批次补传。
        await _fallbackRecoveryService.RecoverAsync().ConfigureAwait(false);

        _diagnosticsStore.SetRuntimeState(MesRetryRuntimeState.Retrying);
        var retryResult = await _retryRecordProcessor.ProcessAsync(CurrentCancellationToken).ConfigureAwait(false);
        if (retryResult == MesRetryProcessResult.Failed)
        {
            await RefreshMesCapacityStatusAsync().ConfigureAwait(false);

            _diagnosticsStore.SetRuntimeState(MesRetryRuntimeState.LastFailed);
            return;
        }

        await RefreshMesCapacityStatusAsync().ConfigureAwait(false);

        await _housekeepingService.ApplyIdleOrBackoffStateAsync().ConfigureAwait(false);
    }

    private async Task RefreshMesCapacityStatusAsync()
    {
        await _capacityGuard.RefreshMesRetryCapacityStatusAsync().ConfigureAwait(false);
        await _capacityGuard.RefreshMesFallbackCapacityStatusAsync().ConfigureAwait(false);
    }

    private bool IsMesHeartbeatReady()
        => _heartbeatStateStore.Get(ExternalSystemKind.Mes).IsReady;
}
