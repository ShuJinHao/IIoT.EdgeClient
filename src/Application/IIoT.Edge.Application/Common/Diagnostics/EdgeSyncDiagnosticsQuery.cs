using IIoT.Edge.Application.Modules.Diagnostics;
using IIoT.Edge.Application.Context;
using IIoT.Edge.Application.Abstractions.Context;
using IIoT.Edge.Application.Abstractions.DataPipeline.Stores;
using IIoT.Edge.Application.Abstractions.Device;
using IIoT.Edge.Application.Abstractions.Integration;
using IIoT.Edge.Application.Abstractions.Modules;
using IIoT.Edge.Application.Common.Persistence;
using IIoT.Edge.SharedKernel.DataPipeline;

namespace IIoT.Edge.Application.Common.Diagnostics;

public sealed class EdgeSyncDiagnosticsQuery : IEdgeSyncDiagnosticsQuery
{
    private readonly IDeviceService _deviceService;
    private readonly ICloudUploadDiagnosticsStore _cloudDiagnosticsStore;
    private readonly IMesRetryDiagnosticsStore _mesRetryDiagnosticsStore;
    private readonly IMesUploadDiagnosticsStore _mesUploadDiagnosticsStore;
    private readonly ICloudRetryRecordStore _cloudRetryStore;
    private readonly IMesRetryRecordStore _mesRetryStore;
    private readonly IDeviceLogBufferStore _deviceLogBufferStore;
    private readonly ICapacityBufferStore _capacityBufferStore;
    private readonly IProductionContextStore _productionContextStore;
    private readonly IExternalHeartbeatStateStore? _heartbeatStateStore;
    private readonly ICloudDeadLetterStore? _cloudDeadLetterStore;
    private readonly IMesDeadLetterStore? _mesDeadLetterStore;
    private readonly IReadOnlyList<IEdgeProcessModule> _processModules;

    public EdgeSyncDiagnosticsQuery(
        IProductionContextStore productionContextStore,
        IDeviceService deviceService,
        ICloudUploadDiagnosticsStore cloudDiagnosticsStore,
        IMesRetryDiagnosticsStore mesRetryDiagnosticsStore,
        IMesUploadDiagnosticsStore mesUploadDiagnosticsStore,
        ICloudRetryRecordStore cloudRetryStore,
        IMesRetryRecordStore mesRetryStore,
        IDeviceLogBufferStore deviceLogBufferStore,
        ICapacityBufferStore capacityBufferStore,
        IExternalHeartbeatStateStore? heartbeatStateStore = null,
        ICloudDeadLetterStore? cloudDeadLetterStore = null,
        IMesDeadLetterStore? mesDeadLetterStore = null,
        IEnumerable<IEdgeProcessModule>? modules = null)
    {
        _productionContextStore = productionContextStore;
        _deviceService = deviceService;
        _cloudDiagnosticsStore = cloudDiagnosticsStore;
        _mesRetryDiagnosticsStore = mesRetryDiagnosticsStore;
        _mesUploadDiagnosticsStore = mesUploadDiagnosticsStore;
        _cloudRetryStore = cloudRetryStore;
        _mesRetryStore = mesRetryStore;
        _deviceLogBufferStore = deviceLogBufferStore;
        _capacityBufferStore = capacityBufferStore;
        _heartbeatStateStore = heartbeatStateStore;
        _cloudDeadLetterStore = cloudDeadLetterStore;
        _mesDeadLetterStore = mesDeadLetterStore;
        _processModules = (modules ?? [])
            .Where(static x => !string.IsNullOrWhiteSpace(x.ProcessType))
            .ToArray();
    }

    public async Task<EdgeSyncDiagnosticsSnapshot> GetCurrentAsync(CancellationToken ct = default)
    {
        var cloudDiagnostics = _cloudDiagnosticsStore.Snapshot;
        var mesRuntime = _mesRetryDiagnosticsStore.Snapshot;
        var mesChannels = _mesUploadDiagnosticsStore.GetAll()
            .Select(WithProcessDisplayName)
            .ToArray();
        var latestMesFailure = mesChannels
            .Where(x => string.Equals(x.LastResult, "Failed", StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(x => x.LastAttemptAt ?? DateTime.MinValue)
            .FirstOrDefault();

        var cloudPendingTask = GetCloudPendingDiagnosticsAsync(ct);
        var mesPendingTask = GetMesPendingDiagnosticsAsync(ct);
        var cloudDeadLetterTask = GetDeadLetterDiagnosticsAsync(_cloudDeadLetterStore, ct);
        var mesDeadLetterTask = GetDeadLetterDiagnosticsAsync(_mesDeadLetterStore, ct);
        await Task.WhenAll(cloudPendingTask, mesPendingTask, cloudDeadLetterTask, mesDeadLetterTask).ConfigureAwait(false);

        var cloudPending = await cloudPendingTask.ConfigureAwait(false);
        var mesPending = await mesPendingTask.ConfigureAwait(false);
        var cloudDeadLetters = await cloudDeadLetterTask.ConfigureAwait(false);
        var mesDeadLetters = await mesDeadLetterTask.ConfigureAwait(false);

        var cloud = new CloudSyncDiagnosticsSnapshot(
            GateState: _deviceService.CurrentUploadGate.State,
            BlockReason: _deviceService.CurrentUploadGate.Reason,
            RuntimeState: cloudDiagnostics.RuntimeState,
            LastAttemptAt: cloudDiagnostics.LastAttemptAt,
            LastSuccessAt: cloudDiagnostics.LastSuccessAt,
            LastFailureAt: cloudDiagnostics.LastFailureAt,
            LastOutcome: cloudDiagnostics.LastOutcome,
            LastReasonCode: cloudDiagnostics.LastReasonCode,
            LastProcessType: cloudDiagnostics.LastProcessType,
            PendingRetryCount: cloudPending.PendingRetryCount,
            PendingDeviceLogCount: cloudPending.PendingDeviceLogCount,
            PendingCapacityCount: cloudPending.PendingCapacityCount,
            IsPausedWaitingForRecovery:
                cloudDiagnostics.RuntimeState == CloudRetryRuntimeState.WaitingForRecovery
                || _deviceService.CurrentUploadGate.State == EdgeUploadGateState.Refreshing
                || _deviceService.CurrentUploadGate.Reason == EdgeUploadBlockReason.UploadTokenRejected,
            IsCapacityBlocked: cloudDiagnostics.IsCapacityBlocked,
            BlockedChannel: cloudDiagnostics.BlockedChannel,
            BlockedReason: cloudDiagnostics.BlockedReason,
            LastCapacityBlockAt: cloudDiagnostics.LastCapacityBlockAt,
            IsPersistenceFaulted: cloudPending.IsPersistenceFaulted,
            LastPersistenceFaultAt: cloudPending.LastPersistenceFaultAt,
            PersistenceFaultMessage: cloudPending.PersistenceFaultMessage,
            Heartbeat: GetHeartbeat(ExternalSystemKind.Cloud),
            DeadLetters: cloudDeadLetters,
            LastProcessDisplayName: ResolveProcessDisplayName(cloudDiagnostics.LastProcessType));

        var mes = new MesSyncDiagnosticsSnapshot(
            RuntimeState: mesRuntime.RuntimeState,
            LastAttemptAt: mesChannels.MaxBy(x => x.LastAttemptAt ?? DateTime.MinValue)?.LastAttemptAt,
            LastSuccessAt: mesChannels.MaxBy(x => x.LastSuccessAt ?? DateTime.MinValue)?.LastSuccessAt,
            LastFailureAt: latestMesFailure?.LastAttemptAt,
            LastFailureReason: latestMesFailure?.LastFailureReason,
            PendingRetryCount: mesPending.PendingRetryCount,
            Channels: mesChannels,
            IsCapacityBlocked: mesRuntime.IsCapacityBlocked,
            BlockedChannel: mesRuntime.BlockedChannel,
            BlockedReason: mesRuntime.BlockedReason,
            LastCapacityBlockAt: mesRuntime.LastCapacityBlockAt,
            IsPersistenceFaulted: mesPending.IsPersistenceFaulted,
            LastPersistenceFaultAt: mesPending.LastPersistenceFaultAt,
            PersistenceFaultMessage: mesPending.PersistenceFaultMessage,
            Heartbeat: GetHeartbeat(ExternalSystemKind.Mes),
            DeadLetters: mesDeadLetters);

        return new EdgeSyncDiagnosticsSnapshot(
            DeviceName: _deviceService.CurrentDevice?.DeviceName ?? "未知",
            Cloud: cloud,
            Mes: mes,
            ContextPersistence: _productionContextStore.GetPersistenceDiagnostics());
    }

    private async Task<PendingDiagnosticsSnapshot> GetCloudPendingDiagnosticsAsync(CancellationToken ct)
    {
        var retryTask = TryGetCountAsync(() => _cloudRetryStore.GetCountAsync(), ct);
        var deviceLogTask = TryGetCountAsync(() => _deviceLogBufferStore.GetCountAsync(), ct);
        var capacityTask = TryGetCountAsync(() => _capacityBufferStore.GetCountAsync(), ct);
        await Task.WhenAll(retryTask, deviceLogTask, capacityTask).ConfigureAwait(false);

        var retryCount = await retryTask.ConfigureAwait(false);
        var deviceLogCount = await deviceLogTask.ConfigureAwait(false);
        var capacityCount = await capacityTask.ConfigureAwait(false);
        var fault = CountResult.Merge(retryCount, deviceLogCount, capacityCount);

        return new PendingDiagnosticsSnapshot(
            retryCount.Count,
            deviceLogCount.Count,
            capacityCount.Count,
            fault.IsFaulted,
            fault.LastFaultAt,
            fault.FaultMessage);
    }

    private ExternalHeartbeatSnapshot? GetHeartbeat(ExternalSystemKind kind)
        => _heartbeatStateStore?.Get(kind);

    private string? ResolveProcessDisplayName(string? processType)
    {
        if (string.IsNullOrWhiteSpace(processType))
        {
            return null;
        }

        foreach (var module in _processModules)
        {
            if (!string.Equals(module.ProcessType, processType, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var displayName = module.DisplayName;
            if (!string.IsNullOrWhiteSpace(displayName))
            {
                return displayName;
            }
        }

        return null;
    }

    private MesChannelDiagnostics WithProcessDisplayName(MesChannelDiagnostics diagnostics)
        => diagnostics with { ProcessDisplayName = ResolveProcessDisplayName(diagnostics.ProcessType) };

    private async Task<PendingDiagnosticsSnapshot> GetMesPendingDiagnosticsAsync(CancellationToken ct)
    {
        var retryCount = await TryGetCountAsync(() => _mesRetryStore.GetCountAsync(), ct).ConfigureAwait(false);

        return new PendingDiagnosticsSnapshot(
            retryCount.Count,
            0,
            0,
            retryCount.IsFaulted,
            retryCount.LastFaultAt,
            retryCount.FaultMessage);
    }

    private static async Task<CountResult> TryGetCountAsync(Func<Task<int>> action, CancellationToken ct)
    {
        try
        {
            ct.ThrowIfCancellationRequested();
            return new CountResult(
                Count: await action().ConfigureAwait(false),
                IsFaulted: false,
                LastFaultAt: null,
                FaultMessage: null);
        }
        catch (PersistenceAccessException ex)
        {
            return new CountResult(
                Count: 0,
                IsFaulted: true,
                LastFaultAt: DateTime.UtcNow,
                FaultMessage: ex.Message);
        }
    }

    private async Task<DeadLetterDiagnosticsSnapshot> GetDeadLetterDiagnosticsAsync(
        IDeadLetterDiagnosticsStore? store,
        CancellationToken ct)
    {
        if (store is null)
        {
            return DeadLetterDiagnosticsSnapshot.Empty;
        }

        try
        {
            ct.ThrowIfCancellationRequested();
            var countTask = store.GetCountAsync();
            var groupTask = store.GetGroupSummaryAsync();
            var latestTask = store.GetLatestAsync(count: 10);
            await Task.WhenAll(countTask, groupTask, latestTask).ConfigureAwait(false);

            var groups = await groupTask.ConfigureAwait(false);

            return new DeadLetterDiagnosticsSnapshot(
                TotalCount: await countTask.ConfigureAwait(false),
                GroupSummary: groups.Select(WithProcessDisplayName).ToArray(),
                LatestRecords: await latestTask.ConfigureAwait(false),
                IsPersistenceFaulted: false,
                LastPersistenceFaultAt: null,
                PersistenceFaultMessage: null);
        }
        catch (PersistenceAccessException ex)
        {
            return new DeadLetterDiagnosticsSnapshot(
                TotalCount: 0,
                GroupSummary: [],
                LatestRecords: [],
                IsPersistenceFaulted: true,
                LastPersistenceFaultAt: DateTime.UtcNow,
                PersistenceFaultMessage: ex.Message);
        }
    }

    private DeadLetterGroupSummary WithProcessDisplayName(DeadLetterGroupSummary summary)
        => new()
        {
            ProcessType = summary.ProcessType,
            ProcessDisplayName = ResolveProcessDisplayName(summary.ProcessType) ?? summary.ProcessDisplayName,
            FailureStage = summary.FailureStage,
            Count = summary.Count,
            LastCreatedAt = summary.LastCreatedAt
        };

    private sealed record PendingDiagnosticsSnapshot(
        int PendingRetryCount,
        int PendingDeviceLogCount,
        int PendingCapacityCount,
        bool IsPersistenceFaulted,
        DateTime? LastPersistenceFaultAt,
        string? PersistenceFaultMessage);

    private sealed record CountResult(
        int Count,
        bool IsFaulted,
        DateTime? LastFaultAt,
        string? FaultMessage)
    {
        public static CountResult Merge(params CountResult[] results)
        {
            var lastFaultAt = results
                .Where(x => x.IsFaulted)
                .Select(x => x.LastFaultAt)
                .Where(x => x.HasValue)
                .Max();

            var faultMessage = results
                .Where(x => x.IsFaulted && !string.IsNullOrWhiteSpace(x.FaultMessage))
                .Select(x => x.FaultMessage)
                .FirstOrDefault();

            return new CountResult(
                Count: 0,
                IsFaulted: results.Any(x => x.IsFaulted),
                LastFaultAt: lastFaultAt,
                FaultMessage: faultMessage);
        }
    }
}

public static class EdgeSyncDiagnosticsFormatter
{
    public static string FormatCloudFooterStatus(CloudSyncDiagnosticsSnapshot snapshot)
        => EdgeSyncDiagnosticStatusClassifier.ClassifyCloud(snapshot) switch
        {
            CloudSyncDiagnosticStatus.PersistenceFaulted => "云端：存储故障",
            CloudSyncDiagnosticStatus.CapacityBlocked => "云端：产能阻塞",
            CloudSyncDiagnosticStatus.WaitingHeartbeat => "云端：等待心跳恢复",
            CloudSyncDiagnosticStatus.Ready => "云端：已就绪",
            CloudSyncDiagnosticStatus.WaitingRecovery => "云端：等待恢复",
            _ => $"云端：已阻塞（{FormatBlockReason(snapshot.BlockReason)}）"
        };

    public static string FormatMesFooterStatus(MesSyncDiagnosticsSnapshot snapshot)
        => EdgeSyncDiagnosticStatusClassifier.ClassifyMes(snapshot) switch
        {
            MesSyncDiagnosticStatus.PersistenceFaulted => "MES：存储故障",
            MesSyncDiagnosticStatus.CapacityBlocked => "MES：产能阻塞",
            MesSyncDiagnosticStatus.WaitingHeartbeat => "MES：等待心跳恢复",
            MesSyncDiagnosticStatus.Retrying => "MES：重试中",
            MesSyncDiagnosticStatus.Backoff => "MES：退避中",
            MesSyncDiagnosticStatus.LastFailed => "MES：最近失败",
            _ => "MES：空闲"
        };

    public static string FormatCloudMonitorSummary(CloudSyncDiagnosticsSnapshot snapshot)
    {
        var gateText = EdgeSyncDiagnosticStatusClassifier.ClassifyCloud(snapshot) switch
        {
            CloudSyncDiagnosticStatus.PersistenceFaulted => "存储故障",
            CloudSyncDiagnosticStatus.CapacityBlocked => "产能阻塞",
            CloudSyncDiagnosticStatus.WaitingHeartbeat => "等待心跳恢复",
            CloudSyncDiagnosticStatus.Ready => "已就绪",
            CloudSyncDiagnosticStatus.WaitingRecovery => "等待恢复",
            _ => $"已阻塞（{FormatBlockReason(snapshot.BlockReason)}）"
        };

        return string.Join(Environment.NewLine, [
            $"上传门禁：{gateText}",
            $"运行状态：{FormatCloudRuntimeState(snapshot.RuntimeState)}",
            $"最近结果：{FormatCloudOutcome(snapshot.LastOutcome, snapshot.LastReasonCode, snapshot.LastProcessType, snapshot.LastProcessDisplayName)}",
            $"最近成功：{FormatTimestamp(snapshot.LastSuccessAt)}",
            $"最近失败：{FormatTimestamp(snapshot.LastFailureAt)}",
            $"待处理：重试={snapshot.PendingRetryCount}，日志={snapshot.PendingDeviceLogCount}，产能={snapshot.PendingCapacityCount}",
            FormatDeadLetterSummary(snapshot.DeadLetters),
            FormatHeartbeatSummary(snapshot.Heartbeat),
            FormatPersistenceFaultSummary(
                snapshot.IsPersistenceFaulted,
                snapshot.LastPersistenceFaultAt,
                snapshot.PersistenceFaultMessage),
            FormatCapacityBlockedSummary(
                snapshot.IsCapacityBlocked,
                snapshot.BlockedChannel,
                snapshot.BlockedReason,
                snapshot.LastCapacityBlockAt)
        ]);
    }

    public static string FormatMesMonitorSummary(MesSyncDiagnosticsSnapshot snapshot)
    {
        return string.Join(Environment.NewLine, [
            $"运行状态：{FormatMesRuntimeState(snapshot.RuntimeState)}",
            $"最近尝试：{FormatTimestamp(snapshot.LastAttemptAt)}",
            $"最近成功：{FormatTimestamp(snapshot.LastSuccessAt)}",
            $"最近失败：{FormatTimestamp(snapshot.LastFailureAt)}",
            $"失败原因：{NormalizeText(snapshot.LastFailureReason)}",
            $"待处理：重试={snapshot.PendingRetryCount}",
            FormatDeadLetterSummary(snapshot.DeadLetters),
            FormatHeartbeatSummary(snapshot.Heartbeat),
            FormatPersistenceFaultSummary(
                snapshot.IsPersistenceFaulted,
                snapshot.LastPersistenceFaultAt,
                snapshot.PersistenceFaultMessage),
            FormatCapacityBlockedSummary(
                snapshot.IsCapacityBlocked,
                snapshot.BlockedChannel,
                snapshot.BlockedReason,
                snapshot.LastCapacityBlockAt)
        ]);
    }

    public static string FormatHeartbeatSummary(ExternalHeartbeatSnapshot? snapshot)
    {
        if (snapshot is null)
        {
            return "心跳：未配置";
        }

        return snapshot.State switch
        {
            ExternalHeartbeatState.Ready => "心跳：已就绪",
            ExternalHeartbeatState.NotReady => $"心跳：等待恢复（{NormalizeText(snapshot.ReasonCode)}）",
            _ => "心跳：未知"
        };
    }

    public static string FormatPersistenceFaultSummary(
        bool isPersistenceFaulted,
        DateTime? lastPersistenceFaultAt,
        string? persistenceFaultMessage)
    {
        if (!isPersistenceFaulted)
        {
            return "存储故障：否";
        }

        return $"存储故障：是，最近 {FormatTimestamp(lastPersistenceFaultAt)}，原因：{NormalizeText(persistenceFaultMessage)}";
    }

    public static string FormatCapacityBlockedSummary(
        bool isCapacityBlocked,
        CapacityBlockedChannel? blockedChannel,
        string blockedReason,
        DateTime? lastCapacityBlockAt)
    {
        if (!isCapacityBlocked)
        {
            return "产能阻塞：否";
        }

        return $"产能阻塞：是（{FormatBlockedChannel(blockedChannel)} / {FormatCapacityBlockedReason(blockedReason)}），最近 {FormatTimestamp(lastCapacityBlockAt)}";
    }

    public static string FormatDeadLetterSummary(DeadLetterDiagnosticsSnapshot? diagnostics)
    {
        diagnostics ??= DeadLetterDiagnosticsSnapshot.Empty;
        if (diagnostics.IsPersistenceFaulted)
        {
            return $"死信：读取失败（{NormalizeText(diagnostics.PersistenceFaultMessage)}）";
        }

        if (diagnostics.TotalCount <= 0)
        {
            return "死信：0";
        }

        var groups = diagnostics.GroupSummary
            .Take(3)
            .Select(x =>
            {
                var processText = string.IsNullOrWhiteSpace(x.ProcessDisplayName)
                    ? NormalizeProcessType(x.ProcessType)
                    : x.ProcessDisplayName;
                return $"{processText}/{NormalizeText(x.FailureStage)}={x.Count}";
            });
        return $"死信：{diagnostics.TotalCount}（{string.Join("，", groups)}）";
    }

    public static string FormatContextPersistenceSummary(ProductionContextPersistenceDiagnostics diagnostics)
        => string.Join(Environment.NewLine, [
            $"损坏文件数：{diagnostics.CorruptFileCount}",
            $"最近损坏文件：{FormatTimestamp(diagnostics.LastCorruptDetectedAt)}"
        ]);

    public static string FormatCapacityBlockedReason(string blockedReason) => blockedReason switch
    {
        "total" => "总量上限",
        "process_type" => "工序类型上限",
        _ => string.IsNullOrWhiteSpace(blockedReason) ? "--" : blockedReason
    };

    public static string FormatBlockReason(EdgeUploadBlockReason reason) => reason switch
    {
        EdgeUploadBlockReason.None => "无",
        EdgeUploadBlockReason.DeviceUnidentified => "设备未识别",
        EdgeUploadBlockReason.MissingUploadToken => "缺少上传令牌",
        EdgeUploadBlockReason.ExpiredUploadToken => "上传令牌已过期",
        EdgeUploadBlockReason.BootstrapHttpFailure => "bootstrap HTTP 失败",
        EdgeUploadBlockReason.BootstrapTimeout => "bootstrap 超时",
        EdgeUploadBlockReason.BootstrapNetworkFailure => "bootstrap 网络失败",
        EdgeUploadBlockReason.BootstrapPayloadInvalid => "bootstrap 响应无效",
        EdgeUploadBlockReason.UploadTokenRejected => "上传令牌被拒绝",
        _ => "未知"
    };

    public static string FormatCloudRuntimeState(CloudRetryRuntimeState state) => state switch
    {
        CloudRetryRuntimeState.Idle => "空闲",
        CloudRetryRuntimeState.Retrying => "重试中",
        CloudRetryRuntimeState.Backoff => "退避中",
        CloudRetryRuntimeState.WaitingForRecovery => "等待恢复",
        _ => "未知"
    };

    public static string FormatMesRuntimeState(MesRetryRuntimeState state) => state switch
    {
        MesRetryRuntimeState.Idle => "空闲",
        MesRetryRuntimeState.Retrying => "重试中",
        MesRetryRuntimeState.Backoff => "退避中",
        MesRetryRuntimeState.LastFailed => "最近失败",
        _ => "未知"
    };

    public static string FormatTimestamp(DateTime? value)
        => value is null
            ? "--"
            : NormalizeTimestamp(value.Value).ToString("yyyy-MM-dd HH:mm:ss");

    public static string FormatCloudOutcome(
        CloudCallOutcome outcome,
        string reasonCode,
        string? processType,
        string? processDisplayName = null)
    {
        var processText = string.IsNullOrWhiteSpace(processDisplayName)
            ? NormalizeProcessType(processType)
            : processDisplayName;
        var reasonText = NormalizeText(reasonCode);
        var outcomeText = outcome switch
        {
            CloudCallOutcome.Success => "成功",
            CloudCallOutcome.SkippedUploadNotReady => "未就绪，已跳过",
            CloudCallOutcome.UnauthorizedAfterRetry => "重试后仍未授权",
            CloudCallOutcome.HttpFailure => "HTTP 失败",
            CloudCallOutcome.NetworkFailure => "网络失败",
            CloudCallOutcome.Exception => "异常",
            _ => "未知"
        };

        return $"{outcomeText}（{processText} / {reasonText}）";
    }

    public static string FormatMesChannelResult(string? lastResult) => lastResult switch
    {
        null or "" => "--",
        "Success" => "成功",
        "Failed" => "失败",
        _ => lastResult
    };

    public static string FormatPluginLifecycleState(PluginLifecycleState state) => state switch
    {
        PluginLifecycleState.Discovered => "已发现",
        PluginLifecycleState.DisabledByConfig => "已禁用",
        PluginLifecycleState.ManifestInvalid => "清单无效",
        PluginLifecycleState.DependencyMissing => "依赖缺失",
        PluginLifecycleState.HostVersionIncompatible => "宿主版本不兼容",
        PluginLifecycleState.LoadFailed => "加载失败",
        PluginLifecycleState.Activated => "已激活",
        _ => "未知"
    };

    public static string FormatProcessType(string? processType) => NormalizeProcessType(processType);

    private static DateTime NormalizeTimestamp(DateTime value) => value.Kind switch
    {
        DateTimeKind.Utc => DateTime.SpecifyKind(value, DateTimeKind.Unspecified),
        DateTimeKind.Local => DateTime.SpecifyKind(value, DateTimeKind.Unspecified),
        _ => value
    };

    private static string FormatBlockedChannel(CapacityBlockedChannel? blockedChannel) => blockedChannel switch
    {
        CapacityBlockedChannel.Retry => "重试队列",
        CapacityBlockedChannel.Fallback => "兜底队列",
        _ => "--"
    };

    private static string NormalizeText(string? value)
        => string.IsNullOrWhiteSpace(value)
            ? "--"
            : value;

    private static string NormalizeProcessType(string? processType)
        => string.IsNullOrWhiteSpace(processType)
            ? "--"
            : processType;
}
