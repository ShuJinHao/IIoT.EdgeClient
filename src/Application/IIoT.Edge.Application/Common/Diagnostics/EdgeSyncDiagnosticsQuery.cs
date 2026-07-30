using IIoT.Edge.Module.Contracts.Context;
using IIoT.Edge.Module.Contracts.DataPipeline.Stores;
using IIoT.Edge.Module.Contracts.Device;
using IIoT.Edge.Module.Contracts.Modules;
using IIoT.Edge.Application.Common.Persistence;
using IIoT.Edge.Module.Contracts.DataPipeline;

using IIoT.Edge.Module.Contracts.Cloud;
using IIoT.Edge.Module.Contracts.Mes;
using IIoT.Edge.Module.Contracts.Shared;
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
        var latestMesChannel = mesChannels
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
            LastBlockedAt: cloudDiagnostics.LastBlockedAt,
            LastOutcome: cloudDiagnostics.LastOutcome,
            LastReasonCode: cloudDiagnostics.LastReasonCode,
            LastBlockedReason: cloudDiagnostics.LastBlockedReason,
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
            LastProcessDisplayName: ResolveProcessDisplayName(cloudDiagnostics.LastProcessType),
            PendingPassStationCount: cloudPending.PendingRetryCount,
            LastDeviceName: cloudDiagnostics.LastDeviceName,
            LastModuleId: cloudDiagnostics.LastModuleId,
            LastTaskKey: cloudDiagnostics.LastTaskKey,
            LastScenario: cloudDiagnostics.LastScenario)
        {
            LastPlcCode = cloudDiagnostics.LastPlcCode
        };

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
            DeadLetters: mesDeadLetters)
        {
            LastPlcCode = latestMesChannel?.PlcCode
        };

        return new EdgeSyncDiagnosticsSnapshot(
            DeviceName: _deviceService.CurrentDevice?.DeviceName ?? "未知",
            Cloud: cloud,
            Mes: mes,
            ContextPersistence: _productionContextStore.GetPersistenceDiagnostics())
        {
            PlcCode = cloud.LastPlcCode
                      ?? mes.LastPlcCode
                      ?? string.Empty
        };
    }

    private Task<PendingDiagnosticsSnapshot> GetCloudPendingDiagnosticsAsync(CancellationToken ct)
        => GetPendingDiagnosticsAsync(
            ct,
            () => _cloudRetryStore.GetCountAsync(),
            () => _deviceLogBufferStore.GetCountAsync(),
            () => _capacityBufferStore.GetCountAsync());

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
            if (!IsProcessMatch(module, processType))
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

    private static bool IsProcessMatch(IEdgeProcessModule module, string processType)
        => string.Equals(module.ProcessType, processType, StringComparison.OrdinalIgnoreCase)
           || string.Equals(module.ModuleId, processType, StringComparison.OrdinalIgnoreCase)
           || processType.StartsWith($"{module.ModuleId}.", StringComparison.OrdinalIgnoreCase)
           || processType.StartsWith($"{module.ProcessType}.", StringComparison.OrdinalIgnoreCase);

    private MesChannelDiagnostics WithProcessDisplayName(MesChannelDiagnostics diagnostics)
        => diagnostics with { ProcessDisplayName = ResolveProcessDisplayName(diagnostics.ProcessType) };

    private Task<PendingDiagnosticsSnapshot> GetMesPendingDiagnosticsAsync(CancellationToken ct)
        => GetPendingDiagnosticsAsync(ct, () => _mesRetryStore.GetCountAsync());

    private static async Task<PendingDiagnosticsSnapshot> GetPendingDiagnosticsAsync(
        CancellationToken ct,
        Func<Task<int>> retryCountAction,
        Func<Task<int>>? deviceLogCountAction = null,
        Func<Task<int>>? capacityCountAction = null)
    {
        var retryTask = TryGetCountAsync(retryCountAction, ct);
        var deviceLogTask = deviceLogCountAction is null
            ? Task.FromResult(CountResult.Empty)
            : TryGetCountAsync(deviceLogCountAction, ct);
        var capacityTask = capacityCountAction is null
            ? Task.FromResult(CountResult.Empty)
            : TryGetCountAsync(capacityCountAction, ct);

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
        public static CountResult Empty { get; } = new(
            Count: 0,
            IsFaulted: false,
            LastFaultAt: null,
            FaultMessage: null);

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
