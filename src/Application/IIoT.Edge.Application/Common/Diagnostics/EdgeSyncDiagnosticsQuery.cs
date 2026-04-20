using IIoT.Edge.Application.Abstractions.Context;
using IIoT.Edge.Application.Abstractions.DataPipeline.Stores;
using IIoT.Edge.Application.Abstractions.Device;
using IIoT.Edge.Application.Abstractions.Modules;
using IIoT.Edge.Application.Common.Persistence;

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

    public EdgeSyncDiagnosticsQuery(
        IProductionContextStore productionContextStore,
        IDeviceService deviceService,
        ICloudUploadDiagnosticsStore cloudDiagnosticsStore,
        IMesRetryDiagnosticsStore mesRetryDiagnosticsStore,
        IMesUploadDiagnosticsStore mesUploadDiagnosticsStore,
        ICloudRetryRecordStore cloudRetryStore,
        IMesRetryRecordStore mesRetryStore,
        IDeviceLogBufferStore deviceLogBufferStore,
        ICapacityBufferStore capacityBufferStore)
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
    }

    public EdgeSyncDiagnosticsSnapshot GetCurrent()
    {
        var cloudDiagnostics = _cloudDiagnosticsStore.Snapshot;
        var mesRuntime = _mesRetryDiagnosticsStore.Snapshot;
        var mesChannels = _mesUploadDiagnosticsStore.GetAll();
        var latestMesFailure = mesChannels
            .Where(x => string.Equals(x.LastResult, "Failed", StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(x => x.LastAttemptAt ?? DateTime.MinValue)
            .FirstOrDefault();
        var cloudPending = GetCloudPendingDiagnostics();
        var mesPending = GetMesPendingDiagnostics();

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
            PersistenceFaultMessage: cloudPending.PersistenceFaultMessage);

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
            PersistenceFaultMessage: mesPending.PersistenceFaultMessage);

        return new EdgeSyncDiagnosticsSnapshot(
            DeviceName: _deviceService.CurrentDevice?.DeviceName ?? "Unknown",
            Cloud: cloud,
            Mes: mes,
            ContextPersistence: _productionContextStore.GetPersistenceDiagnostics());
    }

    private PendingDiagnosticsSnapshot GetCloudPendingDiagnostics()
    {
        DateTime? lastFaultAt = null;
        string? faultMessage = null;

        var retryCount = TryGetCount(
            () => _cloudRetryStore.GetCountAsync(),
            ref lastFaultAt,
            ref faultMessage);
        var deviceLogCount = TryGetCount(
            () => _deviceLogBufferStore.GetCountAsync(),
            ref lastFaultAt,
            ref faultMessage);
        var capacityCount = TryGetCount(
            () => _capacityBufferStore.GetCountAsync(),
            ref lastFaultAt,
            ref faultMessage);

        return new PendingDiagnosticsSnapshot(
            retryCount,
            deviceLogCount,
            capacityCount,
            !string.IsNullOrWhiteSpace(faultMessage),
            lastFaultAt,
            faultMessage);
    }

    private PendingDiagnosticsSnapshot GetMesPendingDiagnostics()
    {
        DateTime? lastFaultAt = null;
        string? faultMessage = null;

        var retryCount = TryGetCount(
            () => _mesRetryStore.GetCountAsync(),
            ref lastFaultAt,
            ref faultMessage);

        return new PendingDiagnosticsSnapshot(
            retryCount,
            0,
            0,
            !string.IsNullOrWhiteSpace(faultMessage),
            lastFaultAt,
            faultMessage);
    }

    private static int TryGetCount(
        Func<Task<int>> action,
        ref DateTime? lastFaultAt,
        ref string? faultMessage)
    {
        try
        {
            return action().GetAwaiter().GetResult();
        }
        catch (PersistenceAccessException ex)
        {
            lastFaultAt ??= DateTime.Now;
            faultMessage ??= ex.Message;
            return 0;
        }
    }

    private sealed record PendingDiagnosticsSnapshot(
        int PendingRetryCount,
        int PendingDeviceLogCount,
        int PendingCapacityCount,
        bool IsPersistenceFaulted,
        DateTime? LastPersistenceFaultAt,
        string? PersistenceFaultMessage);
}

public static class EdgeSyncDiagnosticsFormatter
{
    public static string FormatCloudFooterStatus(CloudSyncDiagnosticsSnapshot snapshot)
    {
        if (snapshot.IsPersistenceFaulted)
        {
            return "Cloud: Storage Fault";
        }

        if (snapshot.IsCapacityBlocked)
        {
            return "Cloud: Capacity Blocked";
        }

        if (snapshot.GateState == EdgeUploadGateState.Ready)
        {
            return "Cloud: Ready";
        }

        if (snapshot.IsPausedWaitingForRecovery)
        {
            return "Cloud: Waiting for Recovery";
        }

        return $"Cloud: Blocked ({FormatBlockReason(snapshot.BlockReason)})";
    }

    public static string FormatMesFooterStatus(MesSyncDiagnosticsSnapshot snapshot) => snapshot.RuntimeState switch
    {
        _ when snapshot.IsPersistenceFaulted => "MES: Storage Fault",
        _ when snapshot.IsCapacityBlocked => "MES: Capacity Blocked",
        MesRetryRuntimeState.Retrying => "MES: Retry Active",
        MesRetryRuntimeState.Backoff => "MES: Retry Backoff",
        MesRetryRuntimeState.LastFailed => "MES: Last Failed",
        _ => "MES: Idle"
    };

    public static string FormatCloudMonitorSummary(CloudSyncDiagnosticsSnapshot snapshot)
    {
        var gateText = snapshot.GateState switch
        {
            EdgeUploadGateState.Ready => "Ready",
            _ when snapshot.IsPausedWaitingForRecovery => "Waiting for Recovery",
            _ => $"Blocked ({FormatBlockReason(snapshot.BlockReason)})"
        };

        return string.Join(Environment.NewLine, [
            $"Gate: {gateText}",
            $"Runtime: {snapshot.RuntimeState}",
            $"Last: {FormatCloudOutcome(snapshot.LastOutcome, snapshot.LastReasonCode, snapshot.LastProcessType)}",
            $"Last success: {FormatTimestamp(snapshot.LastSuccessAt)}",
            $"Last failure: {FormatTimestamp(snapshot.LastFailureAt)}",
            $"Pending: retry={snapshot.PendingRetryCount}, logs={snapshot.PendingDeviceLogCount}, capacity={snapshot.PendingCapacityCount}",
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
            $"Runtime: {snapshot.RuntimeState}",
            $"Last attempt: {FormatTimestamp(snapshot.LastAttemptAt)}",
            $"Last success: {FormatTimestamp(snapshot.LastSuccessAt)}",
            $"Last failure: {FormatTimestamp(snapshot.LastFailureAt)}",
            $"Failure reason: {snapshot.LastFailureReason ?? "--"}",
            $"Pending: retry={snapshot.PendingRetryCount}",
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

    public static string FormatPersistenceFaultSummary(
        bool isPersistenceFaulted,
        DateTime? lastPersistenceFaultAt,
        string? persistenceFaultMessage)
    {
        if (!isPersistenceFaulted)
        {
            return "Storage fault: no";
        }

        return $"Storage fault: yes, last={FormatTimestamp(lastPersistenceFaultAt)}, message={persistenceFaultMessage ?? "--"}";
    }

    public static string FormatCapacityBlockedSummary(
        bool isCapacityBlocked,
        CapacityBlockedChannel? blockedChannel,
        string blockedReason,
        DateTime? lastCapacityBlockAt)
    {
        if (!isCapacityBlocked)
        {
            return "Capacity blocked: no";
        }

        return $"Capacity blocked: yes ({blockedChannel?.ToString() ?? "--"} / {FormatCapacityBlockedReason(blockedReason)}), last={FormatTimestamp(lastCapacityBlockAt)}";
    }

    public static string FormatContextPersistenceSummary(ProductionContextPersistenceDiagnostics diagnostics)
        => string.Join(Environment.NewLine, [
            $"Corrupt files: {diagnostics.CorruptFileCount}",
            $"Last corrupt: {FormatTimestamp(diagnostics.LastCorruptDetectedAt)}"
        ]);

    public static string FormatCapacityBlockedReason(string blockedReason) => blockedReason switch
    {
        "total" => "total limit",
        "process_type" => "process-type limit",
        _ => blockedReason
    };

    public static string FormatBlockReason(EdgeUploadBlockReason reason) => reason switch
    {
        EdgeUploadBlockReason.DeviceUnidentified => "device",
        EdgeUploadBlockReason.MissingUploadToken => "no token",
        EdgeUploadBlockReason.ExpiredUploadToken => "expired",
        EdgeUploadBlockReason.BootstrapHttpFailure => "bootstrap http",
        EdgeUploadBlockReason.BootstrapTimeout => "bootstrap timeout",
        EdgeUploadBlockReason.BootstrapNetworkFailure => "bootstrap network",
        EdgeUploadBlockReason.BootstrapPayloadInvalid => "bootstrap payload",
        EdgeUploadBlockReason.UploadTokenRejected => "token rejected",
        _ => "unknown"
    };

    public static string FormatTimestamp(DateTime? value)
        => value?.ToString("yyyy-MM-dd HH:mm:ss") ?? "--";

    public static string FormatCloudOutcome(
        CloudCallOutcome outcome,
        string reasonCode,
        string? processType)
    {
        var processText = string.IsNullOrWhiteSpace(processType) ? "--" : processType;
        return outcome == CloudCallOutcome.Success
            ? $"Success ({processText})"
            : $"{outcome} / {reasonCode} ({processText})";
    }
}
