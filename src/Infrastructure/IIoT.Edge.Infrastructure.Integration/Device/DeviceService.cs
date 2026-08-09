using IIoT.Edge.Module.Contracts.Config;
using IIoT.Edge.Module.Contracts.Device;
using IIoT.Edge.Module.Contracts.Logging;
using IIoT.Edge.Application.Common.Device;
using IIoT.Edge.Infrastructure.Integration.Device.Cache;
using System.Diagnostics;

using IIoT.Edge.Module.Contracts.Cloud;
using IIoT.Edge.Module.Contracts.Shared;
namespace IIoT.Edge.Infrastructure.Integration.Device;

public class DeviceService : IDeviceService, IDeviceAccessTokenProvider, IDeviceActivationCoordinator
{
    public const string HttpClientName = "CloudDevice";

    private static readonly TimeSpan OfflineInterval = TimeSpan.FromSeconds(10);

    private readonly ICloudDeviceBootstrapClient _bootstrapClient;
    private readonly IDeviceUploadGatePolicy _uploadGatePolicy;
    private readonly IDeviceBootstrapEventLogger _bootstrapEventLogger;
    private readonly IDeviceSessionCacheCoordinator _cacheCoordinator;
    private readonly ILocalSystemRuntimeConfigService _runtimeConfig;
    private readonly ILogService _logger;
    private readonly IExternalHeartbeatStateStore? _heartbeatStateStore;
    private readonly ICloudDeviceActivationClient? _activationClient;
    private readonly IDeviceActivationStateStore? _activationStateStore;
    private readonly object _stateLock = new();
    private readonly object _lifecycleLock = new();
    private readonly SemaphoreSlim _identifyGate = new(1, 1);
    private CancellationTokenSource? _cts;
    private Task? _heartbeatTask;
    private bool _isRunning;

    public DeviceSession? CurrentDevice { get; private set; }
    public string? AccessToken => CurrentDevice?.UploadAccessToken;
    public DateTimeOffset? AccessTokenExpiresAtUtc => CurrentDevice?.UploadAccessTokenExpiresAtUtc;
    public NetworkState CurrentState { get; private set; } = NetworkState.Offline;
    public EdgeUploadGateSnapshot CurrentUploadGate { get; private set; } = new()
    {
        State = EdgeUploadGateState.Unknown,
        Reason = EdgeUploadBlockReason.DeviceUnidentified
    };

    public bool HasDeviceId => CurrentDevice is not null;
    public bool CanUploadToCloud => CurrentUploadGate.State == EdgeUploadGateState.Ready;

    public event Action<NetworkState>? NetworkStateChanged;
    public event Action<DeviceSession?>? DeviceIdentified;
    public event Action<EdgeUploadGateSnapshot>? UploadGateChanged;

    public DeviceService(
        ICloudDeviceBootstrapClient bootstrapClient,
        IDeviceUploadGatePolicy uploadGatePolicy,
        IDeviceBootstrapEventLogger bootstrapEventLogger,
        IDeviceSessionCacheCoordinator cacheCoordinator,
        ILocalSystemRuntimeConfigService runtimeConfig,
        ILogService logger,
        IExternalHeartbeatStateStore? heartbeatStateStore = null,
        ICloudDeviceActivationClient? activationClient = null,
        IDeviceActivationStateStore? activationStateStore = null)
    {
        _bootstrapClient = bootstrapClient;
        _uploadGatePolicy = uploadGatePolicy;
        _bootstrapEventLogger = bootstrapEventLogger;
        _cacheCoordinator = cacheCoordinator;
        _runtimeConfig = runtimeConfig;
        _logger = logger;
        _heartbeatStateStore = heartbeatStateStore;
        _activationClient = activationClient;
        _activationStateStore = activationStateStore;
    }

    public Task StartAsync(CancellationToken ct)
    {
        lock (_lifecycleLock)
        {
            if (_isRunning)
            {
                return Task.CompletedTask;
            }

            _isRunning = true;
            _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            _heartbeatTask = Task.Run(() => HeartbeatLoopAsync(_cts.Token), CancellationToken.None);
        }

        return Task.CompletedTask;
    }

    public async Task StopAsync()
    {
        CancellationTokenSource? localCts;
        Task? localTask;

        lock (_lifecycleLock)
        {
            if (!_isRunning)
            {
                return;
            }

            _isRunning = false;
            localCts = _cts;
            localTask = _heartbeatTask;
            _cts = null;
            _heartbeatTask = null;
        }

        if (localCts is null)
        {
            return;
        }

        await localCts.CancelAsync().ConfigureAwait(false);
        if (localTask is not null)
        {
            try
            {
                await localTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
        }

        localCts.Dispose();
    }

    public Task RefreshBootstrapAsync(CancellationToken ct = default)
        => RefreshOrIdentifyOnceAsync(ct);

    public async Task<DeviceActivationResult> EnsureActivatedAsync(
        DeviceActivationReadyFacts readyFacts,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(readyFacts);
        var clientCode = IIoT.Edge.SharedKernel.Configuration.EdgeClientIdentity.NormalizeClientCode(
            readyFacts.ClientCode);
        if (readyFacts.Pid <= 0
            || string.IsNullOrWhiteSpace(readyFacts.GenerationId)
            || string.IsNullOrWhiteSpace(readyFacts.ModuleId)
            || string.IsNullOrWhiteSpace(readyFacts.PluginVersion)
            || string.IsNullOrWhiteSpace(readyFacts.PackageSha256)
            || readyFacts.PackageSha256.Length != 64
            || !readyFacts.PackageSha256.All(Uri.IsHexDigit))
        {
            return DeviceActivationResult.Failed("activation_ready_facts_invalid");
        }

        await _identifyGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var session = CurrentDevice;
            var isSameGenerationActiveSession = session is not null
                && string.Equals(session.SessionKind, "Active", StringComparison.OrdinalIgnoreCase)
                && string.Equals(session.ClientCode, clientCode, StringComparison.OrdinalIgnoreCase)
                && string.Equals(session.GenerationId, readyFacts.GenerationId, StringComparison.Ordinal);
            var isSameGenerationActivatedSession = isSameGenerationActiveSession
                && _activationStateStore?.IsActivated(clientCode, readyFacts.GenerationId) == true;

            // An old Active session must never prove that a newly installed generation is ready.
            // Unless both Cloud generation and the runtime activation ledger match exactly, force
            // this generation through its pending bootstrap credential and Cloud activation API.
            if (!isSameGenerationActiveSession
                && (session is null
                    || !string.Equals(session.SessionKind, "ActivationOnly", StringComparison.OrdinalIgnoreCase)
                    || !string.Equals(session.GenerationId, readyFacts.GenerationId, StringComparison.Ordinal)
                    || !string.Equals(session.ClientCode, clientCode, StringComparison.OrdinalIgnoreCase)))
            {
                var bootstrap = await _bootstrapClient.BootstrapAsync(cancellationToken).ConfigureAwait(false);
                if (bootstrap.Kind != CloudDeviceBootstrapResultKind.Success || bootstrap.Session is null)
                {
                    return DeviceActivationResult.Failed("activation_bootstrap_failed");
                }

                session = bootstrap.Session;
                SetCurrentDevice(session, persistToCache: false);
            }

            if (session is null)
            {
                return DeviceActivationResult.Failed("activation_session_missing");
            }

            if (string.Equals(session.SessionKind, "Active", StringComparison.OrdinalIgnoreCase))
            {
                if (!isSameGenerationActiveSession)
                {
                    return DeviceActivationResult.Failed("active_session_generation_mismatch");
                }

                if (_uploadGatePolicy.TryResolveTokenBlockReason(session, out _)
                    && _uploadGatePolicy.CanRefresh(session))
                {
                    var refresh = await _bootstrapClient.RefreshAsync(session, cancellationToken)
                        .ConfigureAwait(false);
                    if (refresh.Kind != CloudDeviceBootstrapResultKind.Success || refresh.Session is null)
                    {
                        return DeviceActivationResult.Failed("active_session_refresh_failed");
                    }

                    session = refresh.Session;
                }

                if (_uploadGatePolicy.TryResolveTokenBlockReason(session, out _))
                {
                    return DeviceActivationResult.Failed("active_session_invalid");
                }

                if (isSameGenerationActivatedSession)
                {
                    _cacheCoordinator.SaveRequired(session);
                    _activationStateStore?.CommitActivated(session, readyFacts.GenerationId);
                    GoOnline(
                        session,
                        DateTimeOffset.UtcNow,
                        latencyMs: null,
                        persistToCache: false);
                    return DeviceActivationResult.Activated();
                }

                return await ConfirmActiveSessionAsync(
                        session,
                        readyFacts with { ClientCode = clientCode },
                        cancellationToken)
                    .ConfigureAwait(false);
            }

            if (!string.Equals(session.SessionKind, "ActivationOnly", StringComparison.OrdinalIgnoreCase)
                || !string.Equals(session.GenerationId, readyFacts.GenerationId, StringComparison.Ordinal)
                || !string.Equals(session.ClientCode, clientCode, StringComparison.OrdinalIgnoreCase)
                || string.IsNullOrWhiteSpace(session.ActivationAccessToken)
                || session.ActivationAccessTokenExpiresAtUtc is { } expiry
                   && expiry <= DateTimeOffset.UtcNow)
            {
                return DeviceActivationResult.Failed("pending_session_invalid");
            }

            if (_activationClient is null || _activationStateStore is null)
            {
                return DeviceActivationResult.Failed("activation_service_unavailable");
            }

            var activation = await _activationClient
                .ActivateAsync(session, readyFacts with { ClientCode = clientCode }, cancellationToken)
                .ConfigureAwait(false);
            if (activation.Kind != CloudDeviceBootstrapResultKind.Success || activation.Session is null)
            {
                return DeviceActivationResult.Failed("activation_request_failed");
            }

            var activeSession = activation.Session;
            if (!string.Equals(activeSession.SessionKind, "Active", StringComparison.OrdinalIgnoreCase)
                || !string.Equals(activeSession.ClientCode, clientCode, StringComparison.OrdinalIgnoreCase)
                || !string.Equals(activeSession.GenerationId, readyFacts.GenerationId, StringComparison.Ordinal)
                || _uploadGatePolicy.TryResolveTokenBlockReason(activeSession, out _))
            {
                return DeviceActivationResult.Failed("activation_response_invalid");
            }

            return await ConfirmActiveSessionAsync(
                    activeSession,
                    readyFacts with { ClientCode = clientCode },
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.Error($"[DeviceActivation] failed: {ex.GetType().Name}");
            return DeviceActivationResult.Failed("activation_commit_failed");
        }
        finally
        {
            _identifyGate.Release();
        }
    }

    private async Task<DeviceActivationResult> ConfirmActiveSessionAsync(
        DeviceSession activeSession,
        DeviceActivationReadyFacts readyFacts,
        CancellationToken cancellationToken)
    {
        if (_activationClient is null || _activationStateStore is null)
        {
            return DeviceActivationResult.Failed("activation_confirmation_service_unavailable");
        }

        // Durable refresh storage must succeed before Cloud is allowed to revoke the legacy session.
        _cacheCoordinator.SaveRequired(activeSession);
        _activationStateStore.CommitActivating(activeSession, readyFacts.GenerationId);
        SetCurrentDevice(activeSession, persistToCache: false);
        if (!await _activationClient
                .ConfirmAsync(activeSession, readyFacts, cancellationToken)
                .ConfigureAwait(false))
        {
            return DeviceActivationResult.Failed("activation_confirmation_failed");
        }

        // The pending credential is deleted only after Cloud confirms the exact ready evidence.
        _activationStateStore.CommitActivated(activeSession, readyFacts.GenerationId);
        GoOnline(
            activeSession,
            DateTimeOffset.UtcNow,
            latencyMs: null,
            persistToCache: false);
        return DeviceActivationResult.Activated();
    }

    public void MarkUploadGateBlocked(EdgeUploadBlockReason reason, DateTimeOffset occurredAtUtc)
    {
        if (reason == EdgeUploadBlockReason.None)
        {
            return;
        }

        EdgeUploadGateSnapshot? nextGate;
        lock (_stateLock)
        {
            nextGate = CurrentUploadGate with
            {
                State = EdgeUploadGateState.Blocked,
                Reason = _uploadGatePolicy.ResolveBlockReason(CurrentDevice, reason),
                TokenExpiresAtUtc = CurrentDevice?.UploadAccessTokenExpiresAtUtc,
                LastBootstrapFailedAtUtc = occurredAtUtc
            };
        }

        UpdateUploadGate(nextGate);
        _heartbeatStateStore?.MarkNotReady(
            ExternalSystemKind.Cloud,
            nextGate.Reason.ToReasonCode(),
            null,
            occurredAtUtc.UtcDateTime);
    }

    private async Task HeartbeatLoopAsync(CancellationToken ct)
    {
        _logger.Info("[设备服务] 心跳循环已启动。");
        await RefreshOrIdentifyOnceAsync(ct).ConfigureAwait(false);

        while (!ct.IsCancellationRequested)
        {
            var interval = CurrentState == NetworkState.Online
                ? _runtimeConfig.Current.OnlineHeartbeatInterval
                : OfflineInterval;
            try
            {
                await Task.Delay(interval, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            await RefreshOrIdentifyOnceAsync(ct).ConfigureAwait(false);
        }

        _logger.Info("[设备服务] 心跳循环已停止。");
    }

    private async Task RefreshOrIdentifyOnceAsync(CancellationToken ct)
    {
        await _identifyGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (!_runtimeConfig.Current.SystemCloudEnabled)
            {
                MarkCloudUploadDisabled(DateTimeOffset.UtcNow);
                return;
            }

            var previousGate = CurrentUploadGate;
            var attemptedAtUtc = DateTimeOffset.UtcNow;
            UpdateUploadGate(
                previousGate with
                {
                    State = EdgeUploadGateState.Refreshing,
                    LastBootstrapAttemptedAtUtc = attemptedAtUtc
                });

            await RefreshOrIdentifyOnceCoreAsync(attemptedAtUtc, previousGate, ct).ConfigureAwait(false);
        }
        finally
        {
            _identifyGate.Release();
        }
    }

    private void MarkCloudUploadDisabled(DateTimeOffset occurredAtUtc)
    {
        var raiseStateChanged = false;
        EdgeUploadGateSnapshot? nextGate = null;

        lock (_stateLock)
        {
            if (CurrentState != NetworkState.Offline)
            {
                CurrentState = NetworkState.Offline;
                _logger.Info("[设备服务] 云端上传已关闭，状态已切换为离线。");
                raiseStateChanged = true;
            }

            nextGate = CurrentUploadGate with
            {
                State = EdgeUploadGateState.Blocked,
                Reason = EdgeUploadBlockReason.CloudUploadDisabled,
                TokenExpiresAtUtc = CurrentDevice?.UploadAccessTokenExpiresAtUtc
            };
        }

        if (raiseStateChanged)
        {
            NetworkStateChanged?.Invoke(NetworkState.Offline);
        }

        UpdateUploadGate(nextGate);
        _heartbeatStateStore?.MarkNotReady(
            ExternalSystemKind.Cloud,
            EdgeUploadBlockReason.CloudUploadDisabled.ToReasonCode(),
            "云端上传已关闭。",
            occurredAtUtc.UtcDateTime);
    }

    private async Task RefreshOrIdentifyOnceCoreAsync(
        DateTimeOffset attemptedAtUtc,
        EdgeUploadGateSnapshot previousGate,
        CancellationToken ct)
    {
        if (_uploadGatePolicy.CanRefresh(CurrentDevice))
        {
            var refreshResult = await TryRefreshCurrentDeviceAsync(attemptedAtUtc, ct).ConfigureAwait(false);
            if (refreshResult == DeviceRefreshResult.Refreshed)
            {
                return;
            }

            if (refreshResult == DeviceRefreshResult.Cancelled)
            {
                RestoreCancelledGate(previousGate, attemptedAtUtc);
                return;
            }
        }

        await IdentifyOnceCoreAsync(attemptedAtUtc, previousGate, ct).ConfigureAwait(false);
    }

    private async Task IdentifyOnceCoreAsync(
        DateTimeOffset attemptedAtUtc,
        EdgeUploadGateSnapshot previousGate,
        CancellationToken ct)
    {
        var stopwatch = Stopwatch.StartNew();
        var result = await _bootstrapClient.BootstrapAsync(ct).ConfigureAwait(false);
        var latencyMs = ToLatencyMs(stopwatch.ElapsedMilliseconds);
        if (result.Kind == CloudDeviceBootstrapResultKind.Cancelled)
        {
            RestoreCancelledGate(previousGate, attemptedAtUtc);
            return;
        }

        if (result.Kind != CloudDeviceBootstrapResultKind.Success || result.Session is null)
        {
            _bootstrapEventLogger.LogBootstrapFailure(result);
            GoOffline(result.ClientCode, null, _uploadGatePolicy.ResolveBootstrapFailureReason(result.Kind), attemptedAtUtc, latencyMs);
            return;
        }

        HandleIdentifiedSession(
            result.Session,
            attemptedAtUtc,
            successEventName: "edge.bootstrap.success",
            invalidTokenEventName: "edge.bootstrap.invalid_token",
            latencyMs);
    }

    private async Task<DeviceRefreshResult> TryRefreshCurrentDeviceAsync(
        DateTimeOffset attemptedAtUtc,
        CancellationToken ct)
    {
        var session = CurrentDevice;
        if (session is null || string.IsNullOrWhiteSpace(session.RefreshToken))
        {
            return DeviceRefreshResult.FallbackToBootstrap;
        }

        var stopwatch = Stopwatch.StartNew();
        var result = await _bootstrapClient.RefreshAsync(session, ct).ConfigureAwait(false);
        var latencyMs = ToLatencyMs(stopwatch.ElapsedMilliseconds);
        if (result.Kind == CloudDeviceBootstrapResultKind.Cancelled)
        {
            return DeviceRefreshResult.Cancelled;
        }

        if (result.Kind != CloudDeviceBootstrapResultKind.Success || result.Session is null)
        {
            _bootstrapEventLogger.LogRefreshFailure(result);
            return DeviceRefreshResult.FallbackToBootstrap;
        }

        HandleIdentifiedSession(
            result.Session,
            attemptedAtUtc,
            successEventName: "edge.bootstrap.refresh.success",
            invalidTokenEventName: "edge.bootstrap.refresh.invalid_token",
            latencyMs);
        return DeviceRefreshResult.Refreshed;
    }

    private void HandleIdentifiedSession(
        DeviceSession session,
        DateTimeOffset attemptedAtUtc,
        string successEventName,
        string invalidTokenEventName,
        int? latencyMs)
    {
        if (!_uploadGatePolicy.TryResolveTokenBlockReason(session, out var invalidReason))
        {
            _bootstrapEventLogger.LogSessionAccepted(successEventName, session);
            if (_runtimeConfig.Current.SystemCloudEnabled)
            {
                GoOnline(session, attemptedAtUtc, latencyMs);
            }
            else
            {
                GoIdentifiedWithCloudUploadDisabled(session, attemptedAtUtc, latencyMs);
            }
            return;
        }

        _bootstrapEventLogger.LogSessionRejected(invalidTokenEventName, session, invalidReason);
        GoOffline(session.ClientCode, session, invalidReason, attemptedAtUtc, latencyMs);
    }

    private void GoOnline(
        DeviceSession session,
        DateTimeOffset attemptedAtUtc,
        int? latencyMs,
        bool persistToCache = true)
    {
        var raiseStateChanged = false;
        var raiseDeviceIdentified = false;
        EdgeUploadGateSnapshot? nextGate = null;

        lock (_stateLock)
        {
            raiseDeviceIdentified = SetCurrentDevice(session, persistToCache);

            if (CurrentState != NetworkState.Online)
            {
                CurrentState = NetworkState.Online;
                _logger.Info("[设备服务] 状态已切换为在线。");
                raiseStateChanged = true;
            }

            nextGate = CurrentUploadGate with
            {
                State = EdgeUploadGateState.Ready,
                Reason = EdgeUploadBlockReason.None,
                TokenExpiresAtUtc = session.UploadAccessTokenExpiresAtUtc,
                LastBootstrapAttemptedAtUtc = attemptedAtUtc,
                LastBootstrapSucceededAtUtc = attemptedAtUtc
            };
        }

        if (raiseDeviceIdentified)
        {
            DeviceIdentified?.Invoke(CurrentDevice);
        }

        if (raiseStateChanged)
        {
            NetworkStateChanged?.Invoke(NetworkState.Online);
        }

        UpdateUploadGate(nextGate);
        _heartbeatStateStore?.MarkReady(ExternalSystemKind.Cloud, attemptedAtUtc.UtcDateTime, latencyMs: latencyMs);
    }

    private void GoIdentifiedWithCloudUploadDisabled(DeviceSession session, DateTimeOffset attemptedAtUtc, int? latencyMs)
    {
        var raiseStateChanged = false;
        var raiseDeviceIdentified = false;
        EdgeUploadGateSnapshot? nextGate = null;

        lock (_stateLock)
        {
            raiseDeviceIdentified = SetCurrentDevice(session, persistToCache: true);

            if (CurrentState != NetworkState.Offline)
            {
                CurrentState = NetworkState.Offline;
                _logger.Info("[设备服务] 设备已识别，云端上传关闭，状态保持离线。");
                raiseStateChanged = true;
            }

            nextGate = CurrentUploadGate with
            {
                State = EdgeUploadGateState.Blocked,
                Reason = EdgeUploadBlockReason.CloudUploadDisabled,
                TokenExpiresAtUtc = session.UploadAccessTokenExpiresAtUtc,
                LastBootstrapAttemptedAtUtc = attemptedAtUtc,
                LastBootstrapSucceededAtUtc = attemptedAtUtc
            };
        }

        if (raiseDeviceIdentified)
        {
            DeviceIdentified?.Invoke(CurrentDevice);
        }

        if (raiseStateChanged)
        {
            NetworkStateChanged?.Invoke(NetworkState.Offline);
        }

        UpdateUploadGate(nextGate);
        _heartbeatStateStore?.MarkNotReady(
            ExternalSystemKind.Cloud,
            EdgeUploadBlockReason.CloudUploadDisabled.ToReasonCode(),
            "设备已完成云端识别，生产数据云端上传关闭。",
            attemptedAtUtc.UtcDateTime,
            latencyMs);
    }

    private void GoOffline(
        string clientCode,
        DeviceSession? identifiedSession,
        EdgeUploadBlockReason blockReason,
        DateTimeOffset attemptedAtUtc,
        int? latencyMs)
    {
        var raiseStateChanged = false;
        var raiseDeviceIdentified = false;
        EdgeUploadGateSnapshot? nextGate = null;

        lock (_stateLock)
        {
            if (identifiedSession is not null)
            {
                raiseDeviceIdentified = SetCurrentDevice(identifiedSession, persistToCache: false);
            }
            else if (CurrentDevice is null)
            {
                raiseDeviceIdentified = TryLoadCachedDevice(clientCode);
            }

            if (CurrentState != NetworkState.Offline)
            {
                CurrentState = NetworkState.Offline;
                _logger.Info("[设备服务] 状态已切换为离线。");
                raiseStateChanged = true;
            }

            nextGate = CurrentUploadGate with
            {
                State = EdgeUploadGateState.Blocked,
                Reason = _uploadGatePolicy.ResolveBlockReason(CurrentDevice, blockReason),
                TokenExpiresAtUtc = CurrentDevice?.UploadAccessTokenExpiresAtUtc,
                LastBootstrapAttemptedAtUtc = attemptedAtUtc,
                LastBootstrapFailedAtUtc = attemptedAtUtc
            };
        }

        if (raiseDeviceIdentified)
        {
            DeviceIdentified?.Invoke(CurrentDevice);
        }

        if (raiseStateChanged)
        {
            NetworkStateChanged?.Invoke(NetworkState.Offline);
        }

        UpdateUploadGate(nextGate);
        _heartbeatStateStore?.MarkNotReady(
            ExternalSystemKind.Cloud,
            nextGate.Reason.ToReasonCode(),
            null,
            attemptedAtUtc.UtcDateTime,
            latencyMs);
    }

    private bool TryLoadCachedDevice(string clientCode)
    {
        var cached = _cacheCoordinator.TryLoad(clientCode);
        if (cached is null)
        {
            return false;
        }

        CurrentDevice = cached;
        return true;
    }

    private bool SetCurrentDevice(DeviceSession session, bool persistToCache)
    {
        var deviceChanged = CurrentDevice is null
            || CurrentDevice.DeviceId != session.DeviceId
            || CurrentDevice.DeviceName != session.DeviceName
            || CurrentDevice.ProcessId != session.ProcessId
            || !string.Equals(CurrentDevice.ClientCode, session.ClientCode, StringComparison.OrdinalIgnoreCase);

        CurrentDevice = session;

        if (persistToCache)
        {
            _cacheCoordinator.Save(session);
        }

        if (deviceChanged)
        {
            _logger.Info($"[设备服务] 当前设备已更新：{session.DeviceName}");
        }

        return deviceChanged;
    }

    private void UpdateUploadGate(EdgeUploadGateSnapshot? nextGate)
    {
        if (nextGate is null)
        {
            return;
        }

        var raiseChanged = false;
        lock (_stateLock)
        {
            if (Equals(CurrentUploadGate, nextGate))
            {
                return;
            }

            CurrentUploadGate = nextGate;
            raiseChanged = true;
        }

        if (raiseChanged)
        {
            UploadGateChanged?.Invoke(nextGate);
        }
    }

    private void RestoreCancelledGate(EdgeUploadGateSnapshot previousGate, DateTimeOffset attemptedAtUtc)
        => UpdateUploadGate(
            previousGate with
            {
                LastBootstrapAttemptedAtUtc = attemptedAtUtc
            });

    private static int ToLatencyMs(long elapsedMilliseconds)
        => (int)Math.Min(int.MaxValue, Math.Max(0, elapsedMilliseconds));
}
