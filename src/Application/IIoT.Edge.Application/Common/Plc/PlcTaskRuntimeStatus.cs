namespace IIoT.Edge.Application.Common.Plc;

public enum PlcTaskRuntimeState
{
    WaitingForRuntime,
    WaitingForConnection,
    Starting,
    Running,
    Stopping,
    Faulted
}

public sealed record PlcTaskRuntimeSnapshot(
    string PlcCode,
    string TaskKey,
    PlcTaskRuntimeState State,
    DateTimeOffset StateChangedAtUtc,
    DateTimeOffset? LastSuccessfulAtUtc,
    string? ErrorCode,
    string? ExceptionType);

public sealed class PlcTaskRuntimeStatusChangedEventArgs(
    string plcCode,
    string taskKey,
    PlcTaskRuntimeSnapshot? snapshot) : EventArgs
{
    public string PlcCode { get; } = plcCode;

    public string TaskKey { get; } = taskKey;

    public PlcTaskRuntimeSnapshot? Snapshot { get; } = snapshot;
}

public interface IPlcTaskRuntimeStatusReader
{
    event EventHandler<PlcTaskRuntimeStatusChangedEventArgs>? StatusChanged;

    PlcTaskRuntimeSnapshot? GetSnapshot(string plcCode, string taskKey);

    IReadOnlyCollection<PlcTaskRuntimeSnapshot> GetSnapshots(string plcCode);
}

public interface IPlcTaskRuntimeStatusWriter
{
    void SetState(
        string plcCode,
        string taskKey,
        PlcTaskRuntimeState state,
        string? errorCode = null,
        string? exceptionType = null);

    void Remove(string plcCode, string taskKey);

    void RemoveAll(string plcCode);
}

public static class PlcTaskRuntimeErrorCodes
{
    public const string TransportDisconnected = nameof(TransportDisconnected);
    public const string Timeout = nameof(Timeout);
    public const string ProtocolRejected = nameof(ProtocolRejected);
    public const string InvalidResponse = nameof(InvalidResponse);
    public const string ConfigurationInvalid = nameof(ConfigurationInvalid);
    public const string TaskFault = nameof(TaskFault);
    public const string TaskStartFailed = nameof(TaskStartFailed);
    public const string TaskUnexpectedExit = nameof(TaskUnexpectedExit);
    public const string TaskStopFailed = nameof(TaskStopFailed);
    public const string TaskStopTimeout = nameof(TaskStopTimeout);
    public const string RuntimeInitializationFailed = nameof(RuntimeInitializationFailed);
    public const string RuntimeQuarantined = nameof(RuntimeQuarantined);
    public const string ConnectionTaskFault = nameof(ConnectionTaskFault);
    public const string ConnectionSupervisorFault = nameof(ConnectionSupervisorFault);
    public const string PeriodicReadFault = nameof(PeriodicReadFault);
}

public sealed class PlcTaskRuntimeStatusStore(TimeProvider? timeProvider = null)
    : IPlcTaskRuntimeStatusReader, IPlcTaskRuntimeStatusWriter
{
    private readonly object _stateLock = new();
    private readonly Dictionary<string, Dictionary<string, PlcTaskRuntimeSnapshot>> _snapshots =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;

    public event EventHandler<PlcTaskRuntimeStatusChangedEventArgs>? StatusChanged;

    public PlcTaskRuntimeSnapshot? GetSnapshot(string plcCode, string taskKey)
    {
        var normalizedPlcCode = NormalizeIdentity(plcCode, nameof(plcCode));
        var normalizedTaskKey = NormalizeIdentity(taskKey, nameof(taskKey));
        lock (_stateLock)
        {
            return _snapshots.TryGetValue(normalizedPlcCode, out var tasks)
                   && tasks.TryGetValue(normalizedTaskKey, out var snapshot)
                ? snapshot
                : null;
        }
    }

    public IReadOnlyCollection<PlcTaskRuntimeSnapshot> GetSnapshots(string plcCode)
    {
        var normalizedPlcCode = NormalizeIdentity(plcCode, nameof(plcCode));
        lock (_stateLock)
        {
            return _snapshots.TryGetValue(normalizedPlcCode, out var tasks)
                ? tasks.Values
                    .OrderBy(static snapshot => snapshot.TaskKey, StringComparer.OrdinalIgnoreCase)
                    .ToArray()
                : [];
        }
    }

    public void SetState(
        string plcCode,
        string taskKey,
        PlcTaskRuntimeState state,
        string? errorCode = null,
        string? exceptionType = null)
    {
        var normalizedPlcCode = NormalizeIdentity(plcCode, nameof(plcCode));
        var normalizedTaskKey = NormalizeIdentity(taskKey, nameof(taskKey));
        var normalizedErrorCode = NormalizeDiagnosticToken(errorCode, nameof(errorCode));
        var normalizedExceptionType = NormalizeDiagnosticToken(exceptionType, nameof(exceptionType));
        if (!Enum.IsDefined(state))
        {
            throw new ArgumentOutOfRangeException(nameof(state), state, "PLC 任务运行状态无效。");
        }

        if (state != PlcTaskRuntimeState.Faulted
            && (normalizedErrorCode is not null || normalizedExceptionType is not null))
        {
            throw new ArgumentException("只有 Faulted 状态可以携带错误码或异常类型。", nameof(state));
        }

        PlcTaskRuntimeSnapshot snapshot;
        lock (_stateLock)
        {
            if (!_snapshots.TryGetValue(normalizedPlcCode, out var tasks))
            {
                tasks = new Dictionary<string, PlcTaskRuntimeSnapshot>(StringComparer.OrdinalIgnoreCase);
                _snapshots.Add(normalizedPlcCode, tasks);
            }

            if (tasks.TryGetValue(normalizedTaskKey, out var existing)
                && existing.State == state
                && string.Equals(existing.ErrorCode, normalizedErrorCode, StringComparison.Ordinal)
                && string.Equals(existing.ExceptionType, normalizedExceptionType, StringComparison.Ordinal))
            {
                return;
            }

            var now = _timeProvider.GetUtcNow();
            snapshot = new PlcTaskRuntimeSnapshot(
                existing?.PlcCode ?? normalizedPlcCode,
                existing?.TaskKey ?? normalizedTaskKey,
                state,
                now,
                state == PlcTaskRuntimeState.Running
                    ? now
                    : existing?.LastSuccessfulAtUtc,
                normalizedErrorCode,
                normalizedExceptionType);
            tasks[normalizedTaskKey] = snapshot;
        }

        Publish(new PlcTaskRuntimeStatusChangedEventArgs(
            snapshot.PlcCode,
            snapshot.TaskKey,
            snapshot));
    }

    public void Remove(string plcCode, string taskKey)
    {
        var normalizedPlcCode = NormalizeIdentity(plcCode, nameof(plcCode));
        var normalizedTaskKey = NormalizeIdentity(taskKey, nameof(taskKey));
        PlcTaskRuntimeSnapshot? removed = null;
        lock (_stateLock)
        {
            if (_snapshots.TryGetValue(normalizedPlcCode, out var tasks)
                && tasks.Remove(normalizedTaskKey, out removed)
                && tasks.Count == 0)
            {
                _snapshots.Remove(normalizedPlcCode);
            }
        }

        if (removed is not null)
        {
            Publish(new PlcTaskRuntimeStatusChangedEventArgs(
                removed.PlcCode,
                removed.TaskKey,
                snapshot: null));
        }
    }

    public void RemoveAll(string plcCode)
    {
        var normalizedPlcCode = NormalizeIdentity(plcCode, nameof(plcCode));
        PlcTaskRuntimeSnapshot[] removed;
        lock (_stateLock)
        {
            if (!_snapshots.Remove(normalizedPlcCode, out var tasks))
            {
                return;
            }

            removed = tasks.Values.ToArray();
        }

        foreach (var snapshot in removed)
        {
            Publish(new PlcTaskRuntimeStatusChangedEventArgs(
                snapshot.PlcCode,
                snapshot.TaskKey,
                snapshot: null));
        }
    }

    private void Publish(PlcTaskRuntimeStatusChangedEventArgs args)
    {
        var handlers = StatusChanged;
        if (handlers is null)
        {
            return;
        }

        foreach (EventHandler<PlcTaskRuntimeStatusChangedEventArgs> handler in handlers.GetInvocationList())
        {
            try
            {
                handler(this, args);
            }
            catch (Exception)
            {
                // 状态观察者不得反向打断 PLC 任务生命周期。
            }
        }
    }

    private static string NormalizeIdentity(string value, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);
        return value.Trim();
    }

    private static string? NormalizeDiagnosticToken(string? value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var normalized = value.Trim();
        if (normalized.Length > 128
            || normalized.Any(static character =>
                !(char.IsAsciiLetterOrDigit(character)
                  || character is '_' or '-' or '.' or '`')))
        {
            throw new ArgumentException(
                "PLC 任务诊断只能保存稳定错误码或异常类型，禁止保存原始异常消息。",
                parameterName);
        }

        return normalized;
    }
}
