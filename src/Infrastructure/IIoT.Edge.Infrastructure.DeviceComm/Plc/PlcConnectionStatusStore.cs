using IIoT.Edge.Module.Contracts.Plc;

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
            var now = DateTimeOffset.UtcNow;
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
                ConnectionState = PlcConnectionState.Connecting,
                LastAttemptAtUtc = now,
                StateChangedAtUtc = now
            };
        }
    }

    public void MarkConnecting(int networkDeviceId, string deviceName)
    {
        lock (_stateLock)
        {
            var now = DateTimeOffset.UtcNow;
            var existing = GetOrCreateSnapshot(networkDeviceId, deviceName);
            _protocolSuccessCounts[networkDeviceId] = 0;
            _snapshots[networkDeviceId] = existing with
            {
                DeviceName = deviceName,
                IsConnected = false,
                ConnectionState = PlcConnectionState.Connecting,
                LastAttemptAtUtc = now,
                StateChangedAtUtc = ResolveStateChangedAt(existing, PlcConnectionState.Connecting, false, now),
                LatencyMs = null
            };
        }
    }

    public void MarkConnected(int networkDeviceId, string deviceName, int? latencyMs = null)
    {
        lock (_stateLock)
        {
            var now = DateTimeOffset.UtcNow;
            var existing = GetOrCreateSnapshot(networkDeviceId, deviceName);
            _protocolSuccessCounts[networkDeviceId] = RequiredProtocolSuccessCount;
            _snapshots[networkDeviceId] = existing with
            {
                DeviceName = deviceName,
                IsConnected = true,
                ConnectionState = PlcConnectionState.Connected,
                LastAttemptAtUtc = now,
                LastConnectedAtUtc = now,
                StateChangedAtUtc = ResolveStateChangedAt(existing, PlcConnectionState.Connected, true, now),
                LastError = null,
                LatencyMs = latencyMs
            };
        }
    }

    public bool MarkProtocolSuccess(int networkDeviceId, string deviceName, int? latencyMs = null)
    {
        lock (_stateLock)
        {
            var now = DateTimeOffset.UtcNow;
            var existing = GetOrCreateSnapshot(networkDeviceId, deviceName);
            var currentCount = _protocolSuccessCounts.TryGetValue(networkDeviceId, out var count)
                ? count
                : existing.IsConnected ? RequiredProtocolSuccessCount : 0;
            currentCount = Math.Min(RequiredProtocolSuccessCount, currentCount + 1);
            _protocolSuccessCounts[networkDeviceId] = currentCount;

            var isStableOnline = currentCount >= RequiredProtocolSuccessCount;
            var nextState = isStableOnline
                ? PlcConnectionState.Connected
                : PlcConnectionState.Connecting;
            _snapshots[networkDeviceId] = existing with
            {
                DeviceName = deviceName,
                IsConnected = isStableOnline,
                ConnectionState = nextState,
                LastAttemptAtUtc = now,
                LastReadAtUtc = now,
                LastConnectedAtUtc = isStableOnline
                    ? now
                    : existing.LastConnectedAtUtc,
                StateChangedAtUtc = ResolveStateChangedAt(existing, nextState, isStableOnline, now),
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
            var now = DateTimeOffset.UtcNow;
            var existing = GetOrCreateSnapshot(networkDeviceId, deviceName);
            _protocolSuccessCounts[networkDeviceId] = 0;
            var nextState = string.IsNullOrWhiteSpace(error)
                ? PlcConnectionState.Disconnected
                : PlcConnectionState.Retrying;
            _snapshots[networkDeviceId] = existing with
            {
                DeviceName = deviceName,
                IsConnected = false,
                ConnectionState = nextState,
                LastFailureAtUtc = string.IsNullOrWhiteSpace(error)
                    ? existing.LastFailureAtUtc
                    : now,
                StateChangedAtUtc = ResolveStateChangedAt(existing, nextState, false, now),
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
            var now = DateTimeOffset.UtcNow;
            var existing = GetOrCreateSnapshot(networkDeviceId, deviceName);
            _protocolSuccessCounts[networkDeviceId] = 0;
            _snapshots[networkDeviceId] = existing with
            {
                DeviceName = deviceName,
                IsConnected = false,
                ConnectionState = PlcConnectionState.Faulted,
                LastFailureAtUtc = now,
                StateChangedAtUtc = ResolveStateChangedAt(existing, PlcConnectionState.Faulted, false, now),
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
            ConnectionState = PlcConnectionState.Connecting,
            LastAttemptAtUtc = DateTimeOffset.UtcNow,
            StateChangedAtUtc = DateTimeOffset.UtcNow
        };
    }

    private static DateTimeOffset ResolveStateChangedAt(
        PlcConnectionRuntimeSnapshot existing,
        PlcConnectionState nextState,
        bool nextConnected,
        DateTimeOffset now)
        => existing.ConnectionState != nextState || existing.IsConnected != nextConnected
            ? now
            : existing.StateChangedAtUtc ?? now;
}
