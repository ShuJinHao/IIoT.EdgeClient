using IIoT.Edge.Application.Abstractions.Plc;

namespace IIoT.Edge.Infrastructure.DeviceComm.Plc;

public sealed class PlcConnectionStatusStore
{
    private readonly object _stateLock = new();
    private readonly Dictionary<int, PlcConnectionRuntimeSnapshot> _snapshots = new();

    public void EnsureTracked(int networkDeviceId, string deviceName)
    {
        lock (_stateLock)
        {
            if (_snapshots.TryGetValue(networkDeviceId, out var existing))
            {
                _snapshots[networkDeviceId] = existing with
                {
                    DeviceName = deviceName
                };
                return;
            }

            _snapshots[networkDeviceId] = new PlcConnectionRuntimeSnapshot
            {
                NetworkDeviceId = networkDeviceId,
                DeviceName = deviceName,
                IsConnected = false
            };
        }
    }

    public void MarkConnected(int networkDeviceId, string deviceName)
    {
        lock (_stateLock)
        {
            var existing = GetOrCreateSnapshot(networkDeviceId, deviceName);
            _snapshots[networkDeviceId] = existing with
            {
                DeviceName = deviceName,
                IsConnected = true,
                LastConnectedAtUtc = DateTimeOffset.UtcNow,
                LastError = null
            };
        }
    }

    public void MarkDisconnected(int networkDeviceId, string deviceName, string? error = null)
    {
        lock (_stateLock)
        {
            var existing = GetOrCreateSnapshot(networkDeviceId, deviceName);
            _snapshots[networkDeviceId] = existing with
            {
                DeviceName = deviceName,
                IsConnected = false,
                LastFailureAtUtc = string.IsNullOrWhiteSpace(error)
                    ? existing.LastFailureAtUtc
                    : DateTimeOffset.UtcNow,
                LastError = string.IsNullOrWhiteSpace(error)
                    ? existing.LastError
                    : error
            };
        }
    }

    public void MarkRuntimeFault(int networkDeviceId, string deviceName, string error)
        => MarkDisconnected(networkDeviceId, deviceName, error);

    public PlcConnectionRuntimeSnapshot? GetSnapshot(int networkDeviceId)
    {
        lock (_stateLock)
        {
            return _snapshots.TryGetValue(networkDeviceId, out var snapshot)
                ? snapshot
                : null;
        }
    }

    public IReadOnlyCollection<PlcConnectionRuntimeSnapshot> GetSnapshots()
    {
        lock (_stateLock)
        {
            return _snapshots.Values
                .OrderBy(x => x.DeviceName, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }
    }

    private PlcConnectionRuntimeSnapshot GetOrCreateSnapshot(int networkDeviceId, string deviceName)
    {
        if (_snapshots.TryGetValue(networkDeviceId, out var existing))
        {
            return existing;
        }

        return new PlcConnectionRuntimeSnapshot
        {
            NetworkDeviceId = networkDeviceId,
            DeviceName = deviceName,
            IsConnected = false
        };
    }
}
