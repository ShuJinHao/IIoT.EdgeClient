using IIoT.Edge.Application.Abstractions.Config;
using IIoT.Edge.Application.Abstractions.Device;
using IIoT.Edge.Application.Abstractions.Integration;
using IIoT.Edge.Application.Abstractions.Logging;
using IIoT.Edge.Application.Common.Device;
using IIoT.Edge.Infrastructure.Integration.Device.Cache;

namespace IIoT.Edge.Infrastructure.Integration.Device;

public class DeviceService : IDeviceService, IDeviceAccessTokenProvider
{
    public const string HttpClientName = "CloudDevice";

    private static readonly TimeSpan OfflineInterval = TimeSpan.FromSeconds(10);

    private readonly ICloudDeviceBootstrapClient _bootstrapClient;
    private readonly IDeviceUploadGatePolicy _uploadGatePolicy;
    private readonly IDeviceBootstrapEventLogger _bootstrapEventLogger;
    private readonly IDeviceSessionCacheStore _cacheStore;
    private readonly ILocalSystemRuntimeConfigService _runtimeConfig;
    private readonly ILogService _logger;
    private readonly IExternalHeartbeatStateStore? _heartbeatStateStore;
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
        IDeviceSessionCacheStore cacheStore,
        ILocalSystemRuntimeConfigService runtimeConfig,
        ILogService logger,
        IExternalHeartbeatStateStore? heartbeatStateStore = null)
    {
        _bootstrapClient = bootstrapClient;
        _uploadGatePolicy = uploadGatePolicy;
        _bootstrapEventLogger = bootstrapEventLogger;
        _cacheStore = cacheStore;
        _runtimeConfig = runtimeConfig;
        _logger = logger;
        _heartbeatStateStore = heartbeatStateStore;
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
            if (!_runtimeConfig.Current.CloudUploadEnabled)
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
        var result = await _bootstrapClient.BootstrapAsync(ct).ConfigureAwait(false);
        if (result.Kind == CloudDeviceBootstrapResultKind.Cancelled)
        {
            RestoreCancelledGate(previousGate, attemptedAtUtc);
            return;
        }

        if (result.Kind != CloudDeviceBootstrapResultKind.Success || result.Session is null)
        {
            _bootstrapEventLogger.LogBootstrapFailure(result);
            GoOffline(result.ClientCode, null, ResolveBootstrapFailureReason(result.Kind), attemptedAtUtc);
            return;
        }

        HandleIdentifiedSession(
            result.Session,
            attemptedAtUtc,
            successEventName: "edge.bootstrap.success",
            invalidTokenEventName: "edge.bootstrap.invalid_token");
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

        var result = await _bootstrapClient.RefreshAsync(session, ct).ConfigureAwait(false);
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
            invalidTokenEventName: "edge.bootstrap.refresh.invalid_token");
        return DeviceRefreshResult.Refreshed;
    }

    private void HandleIdentifiedSession(
        DeviceSession session,
        DateTimeOffset attemptedAtUtc,
        string successEventName,
        string invalidTokenEventName)
    {
        if (!_uploadGatePolicy.TryResolveTokenBlockReason(session, out var invalidReason))
        {
            _bootstrapEventLogger.LogSessionAccepted(successEventName, session);
            GoOnline(session, attemptedAtUtc);
            return;
        }

        _bootstrapEventLogger.LogSessionRejected(invalidTokenEventName, session, invalidReason);
        GoOffline(session.ClientCode, session, invalidReason, attemptedAtUtc);
    }

    private void GoOnline(DeviceSession session, DateTimeOffset attemptedAtUtc)
    {
        var raiseStateChanged = false;
        var raiseDeviceIdentified = false;
        EdgeUploadGateSnapshot? nextGate = null;

        lock (_stateLock)
        {
            raiseDeviceIdentified = SetCurrentDevice(session, persistToCache: true);

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
        _heartbeatStateStore?.MarkReady(ExternalSystemKind.Cloud, attemptedAtUtc.UtcDateTime);
    }

    private void GoOffline(
        string clientCode,
        DeviceSession? identifiedSession,
        EdgeUploadBlockReason blockReason,
        DateTimeOffset attemptedAtUtc)
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
            attemptedAtUtc.UtcDateTime);
    }

    private bool TryLoadCachedDevice(string clientCode)
    {
        try
        {
            var cached = _cacheStore.TryLoad(clientCode);
            if (cached is null)
            {
                return false;
            }

            CurrentDevice = cached;
            _logger.Info($"[设备服务] 已加载本地缓存：{cached.DeviceName}");
            return true;
        }
        catch (Exception ex)
        {
            _logger.Warn($"[设备服务] 加载本地缓存失败：{ex.Message}");
            return false;
        }
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
            try
            {
                _cacheStore.Save(session);
            }
            catch (Exception ex)
            {
                _logger.Warn($"[设备服务] 保存本地缓存失败：{ex.Message}");
            }
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

    private static EdgeUploadBlockReason ResolveBootstrapFailureReason(CloudDeviceBootstrapResultKind kind)
        => kind switch
        {
            CloudDeviceBootstrapResultKind.HttpFailure => EdgeUploadBlockReason.BootstrapHttpFailure,
            CloudDeviceBootstrapResultKind.Timeout => EdgeUploadBlockReason.BootstrapTimeout,
            CloudDeviceBootstrapResultKind.NetworkFailure => EdgeUploadBlockReason.BootstrapNetworkFailure,
            _ => EdgeUploadBlockReason.BootstrapPayloadInvalid
        };

}
