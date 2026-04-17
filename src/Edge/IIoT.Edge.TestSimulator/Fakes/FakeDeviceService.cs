using IIoT.Edge.Application.Abstractions.Device;

namespace IIoT.Edge.TestSimulator.Fakes;

/// <summary>
/// 替换真实设备寻址服务，外部随时切换网络状态
/// </summary>
public sealed class FakeDeviceService : IDeviceService
{
    private static readonly DeviceSession _fixedDevice = new()
    {
        DeviceId  = Guid.Parse("11111111-1111-1111-1111-111111111111"),
        DeviceName = "TestDevice",
        ClientCode = "SIM-CLIENT-01",
        ProcessId  = Guid.Parse("22222222-2222-2222-2222-222222222222")
    };

    public DeviceSession? CurrentDevice => _fixedDevice;

    public NetworkState CurrentState { get; set; } = NetworkState.Offline;

    public bool HasDeviceId => true;

    public event Action<NetworkState>? NetworkStateChanged;
    public event Action<DeviceSession?>? DeviceIdentified;

    public void SetOnline()
    {
        CurrentState = NetworkState.Online;
        NetworkStateChanged?.Invoke(NetworkState.Online);
    }

    public void SetOffline()
    {
        CurrentState = NetworkState.Offline;
        NetworkStateChanged?.Invoke(NetworkState.Offline);
    }

    public Task StartAsync(CancellationToken ct) => Task.CompletedTask;

    public Task StopAsync() => Task.CompletedTask;
}
