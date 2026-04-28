using System.Net.Http.Json;
using IIoT.Edge.Application.Abstractions.Config;
using IIoT.Edge.Application.Abstractions.Device;
using IIoT.Edge.Application.Common.Device;
using IIoT.Edge.Application.Abstractions.Logging;
using IIoT.Edge.Infrastructure.Integration.Config;
using IIoT.Edge.Infrastructure.Integration.Device.Cache;
using IIoT.Edge.Infrastructure.Integration.Http;

namespace IIoT.Edge.Infrastructure.Integration.Device;

public class DeviceService : IDeviceService, IDeviceAccessTokenProvider
{
    public const string HttpClientName = "CloudDevice";

    private readonly IHttpClientFactory _httpClientFactory;
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
        IHttpClientFactory httpClientFactory,
        ICloudApiEndpointProvider endpointProvider,
        DeviceSessionFileCacheStore cacheStore,
        ILocalSystemRuntimeConfigService runtimeConfig,
        ILogService logger)
    {
        _httpClientFactory = httpClientFactory;
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
                Reason = ResolveBlockReason(CurrentDevice, reason),
                TokenExpiresAtUtc = CurrentDevice?.UploadAccessTokenExpiresAtUtc,
                LastBootstrapFailedAtUtc = occurredAtUtc
            };
        }

        UpdateUploadGate(nextGate);
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

    private async Task RefreshOrIdentifyOnceCoreAsync(
        DateTimeOffset attemptedAtUtc,
        EdgeUploadGateSnapshot previousGate,
        CancellationToken ct)
    {
        if (CanRefreshCurrentDevice())
        {
            var refreshResult = await TryRefreshCurrentDeviceAsync(attemptedAtUtc, ct).ConfigureAwait(false);
            if (refreshResult == DeviceRefreshResult.Refreshed)
            {
                return;
            }

            if (refreshResult == DeviceRefreshResult.Cancelled)
            {
                UpdateUploadGate(
                    previousGate with
                    {
                        LastBootstrapAttemptedAtUtc = attemptedAtUtc
                    });
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
        var clientCode = string.Empty;
        try
        {
            clientCode = _endpointProvider.GetClientCode();
            var deviceInstancePath = _endpointProvider.GetDeviceInstancePath();
            var url = _endpointProvider.BuildUrl(
                $"{deviceInstancePath}?clientCode={Uri.EscapeDataString(clientCode)}");

            using var response = await CreateHttpClient().GetAsync(url, ct).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                var errorMessage = await TryReadFirstErrorAsync(response, ct).ConfigureAwait(false);
                _logger.Warn(
                    $"事件(edge.bootstrap.failure) 客户端编码={FormatValue(clientCode)} 状态码={(int)response.StatusCode} 结果=失败 原因=HTTP状态 错误={FormatValue(errorMessage)}");
                GoOffline(clientCode, null, EdgeUploadBlockReason.BootstrapHttpFailure, attemptedAtUtc);
                return;
            }

            var dto = await response.Content.ReadFromJsonAsync<DeviceResponseDto>(ct).ConfigureAwait(false);
            if (dto is null)
            {
                _logger.Warn(
                    $"事件(edge.bootstrap.failure) 客户端编码={FormatValue(clientCode)} 结果=失败 原因=空响应");
                GoOffline(clientCode, null, EdgeUploadBlockReason.BootstrapPayloadInvalid, attemptedAtUtc);
                return;
            }

            dto.RefreshToken ??= CloudAuthHeaders.ReadRefreshToken(response);
            dto.RefreshTokenExpiresAtUtc ??= CloudAuthHeaders.ReadRefreshTokenExpiresAtUtc(response);
            dto.UploadAccessTokenExpiresAtUtc ??= CloudAuthHeaders.ReadAccessTokenExpiresAtUtc(response);

            var session = new DeviceSession
            {
                DeviceId = dto.Id,
                DeviceName = dto.DeviceName,
                ClientCode = string.IsNullOrWhiteSpace(dto.ClientCode) ? clientCode : dto.ClientCode,
                ProcessId = dto.ProcessId,
                UploadAccessToken = dto.UploadAccessToken,
                UploadAccessTokenExpiresAtUtc = dto.UploadAccessTokenExpiresAtUtc,
                RefreshToken = dto.RefreshToken,
                RefreshTokenExpiresAtUtc = dto.RefreshTokenExpiresAtUtc
            };

            if (!TryResolveTokenBlockReason(session, out var invalidReason))
            {
                _logger.Info(
                    $"事件(edge.bootstrap.success) 客户端编码={FormatValue(session.ClientCode)} 设备ID={session.DeviceId} 工序ID={session.ProcessId} 令牌过期时间={FormatTimestamp(session.UploadAccessTokenExpiresAtUtc)} 结果=成功");
                GoOnline(session, attemptedAtUtc);
                return;
            }

            _logger.Warn(
                $"事件(edge.bootstrap.invalid_token) 客户端编码={FormatValue(session.ClientCode)} 设备ID={session.DeviceId} 工序ID={session.ProcessId} 令牌过期时间={FormatTimestamp(session.UploadAccessTokenExpiresAtUtc)} 结果=无效 原因={invalidReason.ToReasonCode()}");
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
                $"事件(edge.bootstrap.failure) 客户端编码={FormatValue(clientCode)} 结果=失败 原因=超时");
            GoOffline(clientCode, null, EdgeUploadBlockReason.BootstrapTimeout, attemptedAtUtc);
        }
        catch (HttpRequestException ex)
        {
            _logger.Warn(
                $"事件(edge.bootstrap.failure) 客户端编码={FormatValue(clientCode)} 结果=失败 原因=网络异常 消息={SanitizeValue(ex.Message)}");
            GoOffline(clientCode, null, EdgeUploadBlockReason.BootstrapNetworkFailure, attemptedAtUtc);
        }
        catch (Exception ex)
        {
            _logger.Error(
                $"事件(edge.bootstrap.failure) 客户端编码={FormatValue(clientCode)} 结果=失败 原因=异常 消息={SanitizeValue(ex.Message)}");
            GoOffline(clientCode, null, EdgeUploadBlockReason.BootstrapPayloadInvalid, attemptedAtUtc);
        }
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

        var clientCode = string.IsNullOrWhiteSpace(session.ClientCode)
            ? _endpointProvider.GetClientCode()
            : session.ClientCode;

        try
        {
            using var request = new HttpRequestMessage(
                HttpMethod.Post,
                _endpointProvider.BuildUrl(_endpointProvider.GetBootstrapRefreshPath()));
            request.Headers.TryAddWithoutValidation(CloudAuthHeaders.RefreshToken, session.RefreshToken);

            using var response = await CreateHttpClient().SendAsync(request, ct).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                var errorMessage = await TryReadFirstErrorAsync(response, ct).ConfigureAwait(false);
                _logger.Warn(
                    $"事件(edge.bootstrap.refresh.failure) 客户端编码={FormatValue(clientCode)} 状态码={(int)response.StatusCode} 结果=失败 原因=HTTP状态 错误={FormatValue(errorMessage)}");
                return DeviceRefreshResult.FallbackToBootstrap;
            }

            var dto = await response.Content.ReadFromJsonAsync<DeviceResponseDto>(ct).ConfigureAwait(false);
            if (dto is null)
            {
                _logger.Warn(
                    $"事件(edge.bootstrap.refresh.failure) 客户端编码={FormatValue(clientCode)} 结果=失败 原因=空响应");
                return DeviceRefreshResult.FallbackToBootstrap;
            }

            dto.RefreshToken ??= CloudAuthHeaders.ReadRefreshToken(response);
            dto.RefreshTokenExpiresAtUtc ??= CloudAuthHeaders.ReadRefreshTokenExpiresAtUtc(response);
            dto.UploadAccessTokenExpiresAtUtc ??= CloudAuthHeaders.ReadAccessTokenExpiresAtUtc(response);

            var refreshedSession = new DeviceSession
            {
                DeviceId = dto.Id,
                DeviceName = dto.DeviceName,
                ClientCode = string.IsNullOrWhiteSpace(dto.ClientCode) ? clientCode : dto.ClientCode,
                ProcessId = dto.ProcessId,
                UploadAccessToken = dto.UploadAccessToken,
                UploadAccessTokenExpiresAtUtc = dto.UploadAccessTokenExpiresAtUtc,
                RefreshToken = dto.RefreshToken,
                RefreshTokenExpiresAtUtc = dto.RefreshTokenExpiresAtUtc
            };

            if (TryResolveTokenBlockReason(refreshedSession, out var invalidReason))
            {
                _logger.Warn(
                    $"事件(edge.bootstrap.refresh.invalid_token) 客户端编码={FormatValue(refreshedSession.ClientCode)} 设备ID={refreshedSession.DeviceId} 工序ID={refreshedSession.ProcessId} 令牌过期时间={FormatTimestamp(refreshedSession.UploadAccessTokenExpiresAtUtc)} 结果=无效 原因={invalidReason.ToReasonCode()}");
                GoOffline(refreshedSession.ClientCode, refreshedSession, invalidReason, attemptedAtUtc);
                return DeviceRefreshResult.Refreshed;
            }

            _logger.Info(
                $"事件(edge.bootstrap.refresh.success) 客户端编码={FormatValue(refreshedSession.ClientCode)} 设备ID={refreshedSession.DeviceId} 工序ID={refreshedSession.ProcessId} 令牌过期时间={FormatTimestamp(refreshedSession.UploadAccessTokenExpiresAtUtc)} 结果=成功");
            GoOnline(refreshedSession, attemptedAtUtc);
            return DeviceRefreshResult.Refreshed;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            return DeviceRefreshResult.Cancelled;
        }
        catch (TaskCanceledException)
        {
            _logger.Warn(
                $"事件(edge.bootstrap.refresh.failure) 客户端编码={FormatValue(clientCode)} 结果=失败 原因=超时");
            return DeviceRefreshResult.FallbackToBootstrap;
        }
        catch (HttpRequestException ex)
        {
            _logger.Warn(
                $"事件(edge.bootstrap.refresh.failure) 客户端编码={FormatValue(clientCode)} 结果=失败 原因=网络异常 消息={SanitizeValue(ex.Message)}");
            return DeviceRefreshResult.FallbackToBootstrap;
        }
        catch (Exception ex)
        {
            _logger.Error(
                $"事件(edge.bootstrap.refresh.failure) 客户端编码={FormatValue(clientCode)} 结果=失败 原因=异常 消息={SanitizeValue(ex.Message)}");
            return DeviceRefreshResult.FallbackToBootstrap;
        }
    }

    private bool CanRefreshCurrentDevice()
    {
        var session = CurrentDevice;
        return session is not null
            && !string.IsNullOrWhiteSpace(session.RefreshToken)
            && (!session.RefreshTokenExpiresAtUtc.HasValue || session.RefreshTokenExpiresAtUtc.Value > DateTimeOffset.UtcNow);
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
                        _logger.Info($"[设备服务] 已加载本地缓存：{cached.DeviceName}");
                        raiseDeviceIdentified = true;
                    }
                }
                catch (Exception ex)
                {
                    _logger.Warn($"[设备服务] 加载本地缓存失败：{ex.Message}");
                }
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
                _logger.Warn($"[设备服务] 保存本地缓存失败：{ex.Message}");
            }
        }

        if (deviceChanged)
        {
            _logger.Info($"[设备服务] 当前设备已更新：{session.DeviceName}");
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

    private static async Task<string?> TryReadFirstErrorAsync(HttpResponseMessage response, CancellationToken ct)
    {
        try
        {
            var envelope = await response.Content.ReadFromJsonAsync<ApiErrorEnvelope>(ct).ConfigureAwait(false);
            return envelope?.Errors?.FirstOrDefault();
        }
        catch
        {
            return null;
        }
    }

    private static string FormatTimestamp(DateTimeOffset? value)
        => value?.ToString("O") ?? "空";

    private static string FormatValue(string? value)
        => string.IsNullOrWhiteSpace(value) ? "未知" : SanitizeValue(value);

    private static string SanitizeValue(string value)
        => value.Replace(' ', '_');

    private HttpClient CreateHttpClient()
        => _httpClientFactory.CreateClient(HttpClientName);

    private enum DeviceRefreshResult
    {
        FallbackToBootstrap,
        Refreshed,
        Cancelled
    }
}
