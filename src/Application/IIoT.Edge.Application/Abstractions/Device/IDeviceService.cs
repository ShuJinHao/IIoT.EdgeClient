namespace IIoT.Edge.Application.Abstractions.Device;

public interface IDeviceService
{
    DeviceSession? CurrentDevice { get; }

    NetworkState CurrentState { get; }

    EdgeUploadGateSnapshot CurrentUploadGate { get; }

    bool HasDeviceId { get; }

    bool CanUploadToCloud { get; }

    Task StartAsync(CancellationToken ct);

    Task StopAsync();

    Task RefreshBootstrapAsync(CancellationToken ct = default);

    void MarkUploadGateBlocked(EdgeUploadBlockReason reason, DateTimeOffset occurredAtUtc);

    event Action<NetworkState> NetworkStateChanged;

    event Action<DeviceSession?> DeviceIdentified;

    event Action<EdgeUploadGateSnapshot> UploadGateChanged;
}
