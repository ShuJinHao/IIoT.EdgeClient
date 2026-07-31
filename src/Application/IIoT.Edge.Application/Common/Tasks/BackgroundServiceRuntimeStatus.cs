namespace IIoT.Edge.Application.Common.Tasks;

public enum BackgroundServiceRuntimeState
{
    Stopped,
    Starting,
    Running,
    Stopping,
    Faulted
}

public sealed record BackgroundServiceRuntimeSnapshot(
    string ServiceName,
    BackgroundServiceRuntimeState State,
    DateTime ChangedAtUtc,
    string? ErrorCode);

public interface IBackgroundServiceRuntimeStatusReader
{
    IReadOnlyList<BackgroundServiceRuntimeSnapshot> GetAll();

    bool TryGet(string serviceName, out BackgroundServiceRuntimeSnapshot snapshot);

    event EventHandler<BackgroundServiceRuntimeSnapshot>? Changed;
}

public interface IBackgroundServiceRuntimeStatusWriter
{
    void Set(
        string serviceName,
        BackgroundServiceRuntimeState state,
        string? errorCode = null);
}

public sealed class BackgroundServiceRuntimeStatusStore(
    TimeProvider? timeProvider = null)
    : IBackgroundServiceRuntimeStatusReader,
      IBackgroundServiceRuntimeStatusWriter
{
    private readonly object _syncRoot = new();
    private readonly Dictionary<string, BackgroundServiceRuntimeSnapshot> _snapshots =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;

    public event EventHandler<BackgroundServiceRuntimeSnapshot>? Changed;

    public IReadOnlyList<BackgroundServiceRuntimeSnapshot> GetAll()
    {
        lock (_syncRoot)
        {
            return _snapshots.Values
                .OrderBy(static snapshot => snapshot.ServiceName, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }
    }

    public bool TryGet(
        string serviceName,
        out BackgroundServiceRuntimeSnapshot snapshot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(serviceName);
        lock (_syncRoot)
        {
            return _snapshots.TryGetValue(serviceName, out snapshot!);
        }
    }

    public void Set(
        string serviceName,
        BackgroundServiceRuntimeState state,
        string? errorCode = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(serviceName);
        var normalizedName = serviceName.Trim();
        var normalizedError = string.IsNullOrWhiteSpace(errorCode)
            ? null
            : errorCode.Trim();
        if (normalizedError is { Length: > 128 }
            || normalizedError?.Any(char.IsControl) == true)
        {
            throw new ArgumentException("后台服务错误码无效。", nameof(errorCode));
        }

        BackgroundServiceRuntimeSnapshot snapshot;
        lock (_syncRoot)
        {
            if (_snapshots.TryGetValue(normalizedName, out var current)
                && current.State == state
                && string.Equals(current.ErrorCode, normalizedError, StringComparison.Ordinal))
            {
                return;
            }

            snapshot = new BackgroundServiceRuntimeSnapshot(
                normalizedName,
                state,
                _timeProvider.GetUtcNow().UtcDateTime,
                normalizedError);
            _snapshots[normalizedName] = snapshot;
        }

        Changed?.Invoke(this, snapshot);
    }
}
