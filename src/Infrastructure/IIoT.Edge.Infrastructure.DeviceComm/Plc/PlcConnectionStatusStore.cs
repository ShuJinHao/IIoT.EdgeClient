using IIoT.Edge.Application.Abstractions.Plc;

namespace IIoT.Edge.Infrastructure.DeviceComm.Plc;

public sealed class PlcConnectionStatusStore
{
    private const int RequiredProtocolSuccessCount = 2;

    private readonly object _stateLock = new();
    private readonly Dictionary<int, PlcConnectionRuntimeSnapshot> _snapshots = new();
    private readonly Dictionary<int, int> _protocolSuccessCounts = new();

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
                IsConnected = false,
                ConnectionState = PlcConnectionState.Connecting
            };
        }
    }

    public void MarkConnecting(int networkDeviceId, string deviceName)
    {
        lock (_stateLock)
        {
            var existing = GetOrCreateSnapshot(networkDeviceId, deviceName);
            _protocolSuccessCounts[networkDeviceId] = 0;
            _snapshots[networkDeviceId] = existing with
            {
                DeviceName = deviceName,
                IsConnected = false,
                ConnectionState = PlcConnectionState.Connecting,
                LatencyMs = null
            };
        }
    }

    public void MarkConnected(int networkDeviceId, string deviceName, int? latencyMs = null)
    {
        lock (_stateLock)
        {
            var existing = GetOrCreateSnapshot(networkDeviceId, deviceName);
            _protocolSuccessCounts[networkDeviceId] = RequiredProtocolSuccessCount;
            _snapshots[networkDeviceId] = existing with
            {
                DeviceName = deviceName,
                IsConnected = true,
                ConnectionState = PlcConnectionState.Connected,
                LastConnectedAtUtc = DateTimeOffset.UtcNow,
                LastError = null,
                LatencyMs = latencyMs
            };
        }
    }

    public bool MarkProtocolSuccess(int networkDeviceId, string deviceName, int? latencyMs = null)
    {
        lock (_stateLock)
        {
            var existing = GetOrCreateSnapshot(networkDeviceId, deviceName);
            var currentCount = _protocolSuccessCounts.TryGetValue(networkDeviceId, out var count)
                ? count
                : existing.IsConnected ? RequiredProtocolSuccessCount : 0;
            currentCount = Math.Min(RequiredProtocolSuccessCount, currentCount + 1);
            _protocolSuccessCounts[networkDeviceId] = currentCount;

            var isStableOnline = currentCount >= RequiredProtocolSuccessCount;
            _snapshots[networkDeviceId] = existing with
            {
                DeviceName = deviceName,
                IsConnected = isStableOnline,
                ConnectionState = isStableOnline
                    ? PlcConnectionState.Connected
                    : PlcConnectionState.Connecting,
                LastConnectedAtUtc = isStableOnline
                    ? DateTimeOffset.UtcNow
                    : existing.LastConnectedAtUtc,
                LastError = isStableOnline ? null : existing.LastError,
                LatencyMs = isStableOnline ? latencyMs : null
            };

            return isStableOnline;
        }
    }

    public bool IsStableOnline(int networkDeviceId)
    {
        lock (_stateLock)
        {
            return _snapshots.TryGetValue(networkDeviceId, out var snapshot)
                && snapshot.IsConnected
                && _protocolSuccessCounts.TryGetValue(networkDeviceId, out var count)
                && count >= RequiredProtocolSuccessCount;
        }
    }

    public void MarkDisconnected(int networkDeviceId, string deviceName, string? error = null)
    {
        lock (_stateLock)
        {
            var existing = GetOrCreateSnapshot(networkDeviceId, deviceName);
            _protocolSuccessCounts[networkDeviceId] = 0;
            _snapshots[networkDeviceId] = existing with
            {
                DeviceName = deviceName,
                IsConnected = false,
                ConnectionState = string.IsNullOrWhiteSpace(error)
                    ? PlcConnectionState.Disconnected
                    : PlcConnectionState.Retrying,
                LastFailureAtUtc = string.IsNullOrWhiteSpace(error)
                    ? existing.LastFailureAtUtc
                    : DateTimeOffset.UtcNow,
                LastError = string.IsNullOrWhiteSpace(error)
                    ? existing.LastError
                    : error,
                LatencyMs = null
            };
        }
    }

    public void MarkRuntimeFault(int networkDeviceId, string deviceName, string error)
    {
        lock (_stateLock)
        {
            var existing = GetOrCreateSnapshot(networkDeviceId, deviceName);
            _protocolSuccessCounts[networkDeviceId] = 0;
            _snapshots[networkDeviceId] = existing with
            {
                DeviceName = deviceName,
                IsConnected = false,
                ConnectionState = PlcConnectionState.Faulted,
                LastFailureAtUtc = DateTimeOffset.UtcNow,
                LastError = error,
                LatencyMs = null
            };
        }
    }

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
            IsConnected = false,
            ConnectionState = PlcConnectionState.Connecting
        };
    }
}
