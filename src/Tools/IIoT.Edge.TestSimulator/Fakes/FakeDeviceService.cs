using IIoT.Edge.Application.Abstractions.Device;

namespace IIoT.Edge.TestSimulator.Fakes;

/// <summary>
/// 替换真实设备寻址服务，外部随时切换网络状态
/// </summary>
public sealed class FakeDeviceService : IDeviceService, IDeviceAccessTokenProvider
{
    private static readonly DeviceSession _fixedDevice = new()
    {
        DeviceId  = Guid.Parse("11111111-1111-1111-1111-111111111111"),
        DeviceName = "TestDevice",
        ClientCode = "SIM-CLIENT-01",
        ProcessId  = Guid.Parse("22222222-2222-2222-2222-222222222222"),
        UploadAccessToken = "simulator-device-token",
        UploadAccessTokenExpiresAtUtc = DateTimeOffset.UtcNow.AddDays(30)
    };

    public DeviceSession? CurrentDevice => _fixedDevice;
    public string? AccessToken => CurrentDevice?.UploadAccessToken;
    public DateTimeOffset? AccessTokenExpiresAtUtc => CurrentDevice?.UploadAccessTokenExpiresAtUtc;

    public NetworkState CurrentState { get; set; } = NetworkState.Offline;
    public EdgeUploadGateSnapshot CurrentUploadGate { get; private set; } = new()
    {
        State = EdgeUploadGateState.Unknown,
        Reason = EdgeUploadBlockReason.DeviceUnidentified
    };

    public bool HasDeviceId => true;
    public bool CanUploadToCloud => CurrentUploadGate.State == EdgeUploadGateState.Ready;

    public event Action<NetworkState>? NetworkStateChanged;
    public event Action<DeviceSession?>? DeviceIdentified;
    public event Action<EdgeUploadGateSnapshot>? UploadGateChanged;

    public void SetOnline()
    {
        CurrentState = NetworkState.Online;
        CurrentUploadGate = new EdgeUploadGateSnapshot
        {
            State = EdgeUploadGateState.Ready,
            Reason = EdgeUploadBlockReason.None,
            TokenExpiresAtUtc = CurrentDevice?.UploadAccessTokenExpiresAtUtc,
            LastBootstrapSucceededAtUtc = DateTimeOffset.UtcNow
        };
        NetworkStateChanged?.Invoke(NetworkState.Online);
        UploadGateChanged?.Invoke(CurrentUploadGate);
    }

    public void SetOffline()
    {
        CurrentState = NetworkState.Offline;
        CurrentUploadGate = new EdgeUploadGateSnapshot
        {
            State = EdgeUploadGateState.Blocked,
            Reason = EdgeUploadBlockReason.BootstrapNetworkFailure,
            TokenExpiresAtUtc = CurrentDevice?.UploadAccessTokenExpiresAtUtc,
            LastBootstrapFailedAtUtc = DateTimeOffset.UtcNow
        };
        NetworkStateChanged?.Invoke(NetworkState.Offline);
        UploadGateChanged?.Invoke(CurrentUploadGate);
    }

    public Task StartAsync(CancellationToken ct) => Task.CompletedTask;

    public Task StopAsync() => Task.CompletedTask;

    public Task RefreshBootstrapAsync(CancellationToken ct = default) => Task.CompletedTask;

    public void MarkUploadGateBlocked(EdgeUploadBlockReason reason, DateTimeOffset occurredAtUtc)
    {
        CurrentUploadGate = new EdgeUploadGateSnapshot
        {
            State = EdgeUploadGateState.Blocked,
            Reason = reason,
            TokenExpiresAtUtc = CurrentDevice?.UploadAccessTokenExpiresAtUtc,
            LastBootstrapFailedAtUtc = occurredAtUtc
        };
        UploadGateChanged?.Invoke(CurrentUploadGate);
    }
}
