using IIoT.Edge.Application.Abstractions.Integration;

namespace IIoT.Edge.Shell.Core;

public sealed class ExternalHeartbeatStateStore : IExternalHeartbeatStateStore
{
    private readonly object _sync = new();
    private readonly Dictionary<ExternalSystemKind, ExternalHeartbeatSnapshot> _snapshots = new();

    public ExternalHeartbeatSnapshot Get(ExternalSystemKind system)
    {
        lock (_sync)
        {
            return _snapshots.TryGetValue(system, out var snapshot)
                ? snapshot
                : ExternalHeartbeatSnapshot.Unknown(system);
        }
    }

    public void MarkReady(ExternalSystemKind system, DateTime? occurredAtUtc = null, string? message = null)
    {
        var occurredAt = occurredAtUtc ?? DateTime.UtcNow;
        lock (_sync)
        {
            var previous = GetWithoutLock(system);
            _snapshots[system] = previous with
            {
                State = ExternalHeartbeatState.Ready,
                ReasonCode = "ready",
                Message = message,
                LastAttemptAtUtc = occurredAt,
                LastSuccessAtUtc = occurredAt
            };
        }
    }

    public void MarkNotReady(
        ExternalSystemKind system,
        string reasonCode,
        string? message = null,
        DateTime? occurredAtUtc = null)
    {
        var occurredAt = occurredAtUtc ?? DateTime.UtcNow;
        lock (_sync)
        {
            var previous = GetWithoutLock(system);
            _snapshots[system] = previous with
            {
                State = ExternalHeartbeatState.NotReady,
                ReasonCode = string.IsNullOrWhiteSpace(reasonCode) ? "not_ready" : reasonCode,
                Message = message,
                LastAttemptAtUtc = occurredAt,
                LastFailureAtUtc = occurredAt
            };
        }
    }

    private ExternalHeartbeatSnapshot GetWithoutLock(ExternalSystemKind system)
        => _snapshots.TryGetValue(system, out var snapshot)
            ? snapshot
            : ExternalHeartbeatSnapshot.Unknown(system);
}
