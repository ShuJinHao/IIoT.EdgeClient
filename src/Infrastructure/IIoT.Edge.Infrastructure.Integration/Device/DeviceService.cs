using System.Net.Http.Json;
using IIoT.Edge.Application.Abstractions.Config;
using IIoT.Edge.Application.Abstractions.Device;
using IIoT.Edge.Application.Abstractions.Logging;
using IIoT.Edge.Infrastructure.Integration.Config;
using IIoT.Edge.Infrastructure.Integration.Device.Cache;

namespace IIoT.Edge.Infrastructure.Integration.Device;

public class DeviceService : IDeviceService, IDeviceAccessTokenProvider
{
    private readonly HttpClient _httpClient;
    private readonly ICloudApiEndpointProvider _endpointProvider;
    private readonly DeviceSessionFileCacheStore _cacheStore;
    private readonly ILocalSystemRuntimeConfigService _runtimeConfig;
    private readonly ILogService _logger;
    private readonly object _stateLock = new();
    private readonly object _lifecycleLock = new();
    private readonly SemaphoreSlim _identifyGate = new(1, 1);
    private CancellationTokenSource? _cts;
    private Task? _heartbeatTask;
    private bool _isRunning;
    private static readonly TimeSpan OfflineInterval = TimeSpan.FromSeconds(10);

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
        HttpClient httpClient,
        ICloudApiEndpointProvider endpointProvider,
        DeviceSessionFileCacheStore cacheStore,
        ILocalSystemRuntimeConfigService runtimeConfig,
        ILogService logger)
    {
        _httpClient = httpClient;
        _endpointProvider = endpointProvider;
        _cacheStore = cacheStore;
        _runtimeConfig = runtimeConfig;
        _logger = logger;
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

        if (localCts is not null)
        {
            await localCts.CancelAsync();
            if (localTask is not null)
            {
                try
                {
                    await localTask;
                }
                catch (OperationCanceledException)
                {
                }
            }

            localCts.Dispose();
        }
    }

    public Task RefreshBootstrapAsync(CancellationToken ct = default)
        => IdentifyOnceAsync(ct);

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
                Reason = ResolveBlockReason(CurrentDevice, reason),
                TokenExpiresAtUtc = CurrentDevice?.UploadAccessTokenExpiresAtUtc,
                LastBootstrapFailedAtUtc = occurredAtUtc
            };
        }

        UpdateUploadGate(nextGate);
    }

    private async Task HeartbeatLoopAsync(CancellationToken ct)
    {
        _logger.Info("[DeviceService] Heartbeat loop started.");
        await IdentifyOnceAsync(ct);

        while (!ct.IsCancellationRequested)
        {
            var interval = CurrentState == NetworkState.Online
                ? _runtimeConfig.Current.OnlineHeartbeatInterval
                : OfflineInterval;
            try
            {
                await Task.Delay(interval, ct);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            await IdentifyOnceAsync(ct);
        }

        _logger.Info("[DeviceService] Heartbeat loop stopped.");
    }

    private async Task IdentifyOnceAsync(CancellationToken ct)
    {
        await _identifyGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var previousGate = CurrentUploadGate;
            var attemptedAtUtc = DateTimeOffset.UtcNow;
            UpdateUploadGate(
                previousGate with
                {
                    State = EdgeUploadGateState.Refreshing,
                    LastBootstrapAttemptedAtUtc = attemptedAtUtc
                });

            await IdentifyOnceCoreAsync(attemptedAtUtc, previousGate, ct).ConfigureAwait(false);
        }
        finally
        {
            _identifyGate.Release();
        }
    }

    private async Task IdentifyOnceCoreAsync(
        DateTimeOffset attemptedAtUtc,
        EdgeUploadGateSnapshot previousGate,
        CancellationToken ct)
    {
        var clientCode = string.Empty;
        try
        {
            clientCode = _endpointProvider.GetClientCode();
            var deviceInstancePath = _endpointProvider.GetDeviceInstancePath();
            var url = _endpointProvider.BuildUrl(
                $"{deviceInstancePath}?clientCode={Uri.EscapeDataString(clientCode)}");

            var response = await _httpClient.GetAsync(url, ct).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                _logger.Warn(
                    $"event=edge.bootstrap.failure client_code={FormatValue(clientCode)} status_code={(int)response.StatusCode} result=failed reason=http_status");
                GoOffline(clientCode, null, EdgeUploadBlockReason.BootstrapHttpFailure, attemptedAtUtc);
                return;
            }

            var dto = await response.Content.ReadFromJsonAsync<DeviceResponseDto>(ct).ConfigureAwait(false);
            if (dto is null)
            {
                _logger.Warn(
                    $"event=edge.bootstrap.failure client_code={FormatValue(clientCode)} result=failed reason=empty_payload");
                GoOffline(clientCode, null, EdgeUploadBlockReason.BootstrapPayloadInvalid, attemptedAtUtc);
                return;
            }

            var session = new DeviceSession
            {
                DeviceId = dto.Id,
                DeviceName = dto.DeviceName,
                ClientCode = string.IsNullOrWhiteSpace(dto.ClientCode) ? clientCode : dto.ClientCode,
                ProcessId = dto.ProcessId,
                UploadAccessToken = dto.UploadAccessToken,
                UploadAccessTokenExpiresAtUtc = dto.UploadAccessTokenExpiresAtUtc
            };

            if (!TryResolveTokenBlockReason(session, out var invalidReason))
            {
                _logger.Info(
                    $"event=edge.bootstrap.success client_code={FormatValue(session.ClientCode)} device_id={session.DeviceId} process_id={session.ProcessId} expires_at_utc={FormatTimestamp(session.UploadAccessTokenExpiresAtUtc)} result=ok");
                GoOnline(session, attemptedAtUtc);
                return;
            }

            _logger.Warn(
                $"event=edge.bootstrap.invalid_token client_code={FormatValue(session.ClientCode)} device_id={session.DeviceId} process_id={session.ProcessId} expires_at_utc={FormatTimestamp(session.UploadAccessTokenExpiresAtUtc)} result=invalid reason={invalidReason.ToReasonCode()}");
            GoOffline(session.ClientCode, session, invalidReason, attemptedAtUtc);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            UpdateUploadGate(
                previousGate with
                {
                    LastBootstrapAttemptedAtUtc = attemptedAtUtc
                });
        }
        catch (TaskCanceledException)
        {
            _logger.Warn(
                $"event=edge.bootstrap.failure client_code={FormatValue(clientCode)} result=failed reason=timeout");
            GoOffline(clientCode, null, EdgeUploadBlockReason.BootstrapTimeout, attemptedAtUtc);
        }
        catch (HttpRequestException ex)
        {
            _logger.Warn(
                $"event=edge.bootstrap.failure client_code={FormatValue(clientCode)} result=failed reason=network_exception message={SanitizeValue(ex.Message)}");
            GoOffline(clientCode, null, EdgeUploadBlockReason.BootstrapNetworkFailure, attemptedAtUtc);
        }
        catch (Exception ex)
        {
            _logger.Error(
                $"event=edge.bootstrap.failure client_code={FormatValue(clientCode)} result=failed reason=exception message={SanitizeValue(ex.Message)}");
            GoOffline(clientCode, null, EdgeUploadBlockReason.BootstrapPayloadInvalid, attemptedAtUtc);
        }
    }

    private static bool TryResolveTokenBlockReason(DeviceSession? session, out EdgeUploadBlockReason reason)
    {
        if (session is null || session.DeviceId == Guid.Empty)
        {
            reason = EdgeUploadBlockReason.DeviceUnidentified;
            return true;
        }

        if (string.IsNullOrWhiteSpace(session.UploadAccessToken))
        {
            reason = EdgeUploadBlockReason.MissingUploadToken;
            return true;
        }

        if (session.UploadAccessTokenExpiresAtUtc.HasValue
            && session.UploadAccessTokenExpiresAtUtc.Value <= DateTimeOffset.UtcNow)
        {
            reason = EdgeUploadBlockReason.ExpiredUploadToken;
            return true;
        }

        reason = EdgeUploadBlockReason.None;
        return false;
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
                _logger.Info("[DeviceService] State changed to Online.");
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
                try
                {
                    var cached = _cacheStore.TryLoad(clientCode);
                    if (cached is not null)
                    {
                        CurrentDevice = cached;
                        _logger.Info($"[DeviceService] Loaded local cache: {cached.DeviceName}");
                        raiseDeviceIdentified = true;
                    }
                }
                catch (Exception ex)
                {
                    _logger.Warn($"[DeviceService] Load cache failed: {ex.Message}");
                }
            }

            if (CurrentState != NetworkState.Offline)
            {
                CurrentState = NetworkState.Offline;
                _logger.Info("[DeviceService] State changed to Offline.");
                raiseStateChanged = true;
            }

            nextGate = CurrentUploadGate with
            {
                State = EdgeUploadGateState.Blocked,
                Reason = ResolveBlockReason(CurrentDevice, blockReason),
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
                _logger.Warn($"[DeviceService] Save cache failed: {ex.Message}");
            }
        }

        if (deviceChanged)
        {
            _logger.Info($"[DeviceService] Device updated: {session.DeviceName}");
        }

        return deviceChanged;
    }

    private static EdgeUploadBlockReason ResolveBlockReason(
        DeviceSession? session,
        EdgeUploadBlockReason explicitReason)
    {
        if (explicitReason == EdgeUploadBlockReason.MissingUploadToken
            || explicitReason == EdgeUploadBlockReason.ExpiredUploadToken)
        {
            return explicitReason;
        }

        if (session is null)
        {
            return explicitReason == EdgeUploadBlockReason.None
                ? EdgeUploadBlockReason.DeviceUnidentified
                : explicitReason;
        }

        return explicitReason == EdgeUploadBlockReason.None
            ? ResolveFallbackTokenReason(session)
            : explicitReason;
    }

    private static EdgeUploadBlockReason ResolveFallbackTokenReason(DeviceSession session)
        => TryResolveTokenBlockReason(session, out var tokenReason)
            ? tokenReason
            : EdgeUploadBlockReason.DeviceUnidentified;

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

    private static string FormatTimestamp(DateTimeOffset? value)
        => value?.ToString("O") ?? "null";

    private static string FormatValue(string? value)
        => string.IsNullOrWhiteSpace(value) ? "unknown" : SanitizeValue(value);

    private static string SanitizeValue(string value)
        => value.Replace(' ', '_');
}
