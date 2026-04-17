using System.Net.Http.Json;
using IIoT.Edge.Application.Abstractions.Device;
using IIoT.Edge.Application.Abstractions.Logging;
using IIoT.Edge.Infrastructure.Integration.Config;
using IIoT.Edge.Infrastructure.Integration.Device.Cache;

namespace IIoT.Edge.Infrastructure.Integration.Device;

public class DeviceService : IDeviceService
{
    private readonly HttpClient _httpClient;
    private readonly ICloudApiEndpointProvider _endpointProvider;
    private readonly DeviceSessionFileCacheStore _cacheStore;
    private readonly ILogService _logger;
    private readonly object _stateLock = new();
    private readonly object _lifecycleLock = new();
    private CancellationTokenSource? _cts;
    private Task? _heartbeatTask;
    private bool _isRunning;
    private static readonly TimeSpan OnlineInterval = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan OfflineInterval = TimeSpan.FromSeconds(10);

    public DeviceSession? CurrentDevice { get; private set; }
    public NetworkState CurrentState { get; private set; } = NetworkState.Offline;
    public bool HasDeviceId => CurrentDevice is not null;
    public event Action<NetworkState>? NetworkStateChanged;
    public event Action<DeviceSession?>? DeviceIdentified;

    public DeviceService(
        HttpClient httpClient,
        ICloudApiEndpointProvider endpointProvider,
        DeviceSessionFileCacheStore cacheStore,
        ILogService logger)
    {
        _httpClient = httpClient;
        _endpointProvider = endpointProvider;
        _cacheStore = cacheStore;
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

    private async Task HeartbeatLoopAsync(CancellationToken ct)
    {
        _logger.Info("[DeviceService] Heartbeat loop started.");
        await IdentifyOnceAsync(ct);

        while (!ct.IsCancellationRequested)
        {
            var interval = CurrentState == NetworkState.Online ? OnlineInterval : OfflineInterval;
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
                _logger.Warn($"[DeviceService] Device identify failed: {response.StatusCode}");
                GoOffline(clientCode);
                return;
            }

            var dto = await response.Content.ReadFromJsonAsync<DeviceResponseDto>(ct).ConfigureAwait(false);
            if (dto is null)
            {
                _logger.Warn("[DeviceService] Device identify returned empty payload.");
                GoOffline(clientCode);
                return;
            }

            var session = new DeviceSession
            {
                DeviceId = dto.Id,
                DeviceName = dto.DeviceName,
                ClientCode = clientCode,
                ProcessId = dto.ProcessId
            };

            GoOnline(session);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
        }
        catch (TaskCanceledException)
        {
            _logger.Warn("[DeviceService] Device identify timeout.");
            GoOffline(clientCode);
        }
        catch (HttpRequestException ex)
        {
            _logger.Warn($"[DeviceService] Network exception: {ex.Message}");
            GoOffline(clientCode);
        }
        catch (Exception ex)
        {
            _logger.Error($"[DeviceService] Identify exception: {ex.Message}");
            GoOffline(clientCode);
        }
    }

    private void GoOnline(DeviceSession session)
    {
        var raiseStateChanged = false;
        var raiseDeviceIdentified = false;

        lock (_stateLock)
        {
            var deviceChanged = CurrentDevice is null
                || CurrentDevice.DeviceId != session.DeviceId
                || CurrentDevice.DeviceName != session.DeviceName
                || CurrentDevice.ProcessId != session.ProcessId
                || !string.Equals(CurrentDevice.ClientCode, session.ClientCode, StringComparison.OrdinalIgnoreCase);

            CurrentDevice = session;

            try
            {
                _cacheStore.Save(session);
            }
            catch (Exception ex)
            {
                _logger.Warn($"[DeviceService] Save cache failed: {ex.Message}");
            }

            if (deviceChanged)
            {
                _logger.Info($"[DeviceService] Device updated: {session.DeviceName}");
                raiseDeviceIdentified = true;
            }

            if (CurrentState != NetworkState.Online)
            {
                CurrentState = NetworkState.Online;
                _logger.Info("[DeviceService] State changed to Online.");
                raiseStateChanged = true;
            }
        }

        if (raiseDeviceIdentified)
        {
            DeviceIdentified?.Invoke(CurrentDevice);
        }

        if (raiseStateChanged)
        {
            NetworkStateChanged?.Invoke(NetworkState.Online);
        }
    }

    private void GoOffline(string clientCode)
    {
        var raiseStateChanged = false;
        var raiseDeviceIdentified = false;

        lock (_stateLock)
        {
            if (CurrentDevice is null)
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
        }

        if (raiseDeviceIdentified)
        {
            DeviceIdentified?.Invoke(CurrentDevice);
        }

        if (raiseStateChanged)
        {
            NetworkStateChanged?.Invoke(NetworkState.Offline);
        }
    }
}
