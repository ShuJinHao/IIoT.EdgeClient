namespace IIoT.Edge.Application.Abstractions.Integration;

public enum ExternalSystemKind
{
    Cloud = 0,
    Mes = 1
}

public enum ExternalHeartbeatState
{
    Unknown = 0,
    Ready = 1,
    NotReady = 2
}

public sealed record ExternalHeartbeatSnapshot(
    ExternalSystemKind System,
    ExternalHeartbeatState State,
    string ReasonCode,
    string? Message,
    DateTime? LastAttemptAtUtc,
    DateTime? LastSuccessAtUtc,
    DateTime? LastFailureAtUtc)
{
    public static ExternalHeartbeatSnapshot Unknown(ExternalSystemKind system, string reasonCode = "unknown")
        => new(system, ExternalHeartbeatState.Unknown, reasonCode, null, null, null, null);

    public bool IsReady => State == ExternalHeartbeatState.Ready;
}

public interface IExternalHeartbeatStateStore
{
    ExternalHeartbeatSnapshot Get(ExternalSystemKind system);

    void MarkReady(ExternalSystemKind system, DateTime? occurredAtUtc = null, string? message = null);

    void MarkNotReady(
        ExternalSystemKind system,
        string reasonCode,
        string? message = null,
        DateTime? occurredAtUtc = null);
}

public interface IMesHeartbeatProbe
{
    Task<ExternalHeartbeatSnapshot> ProbeAsync(CancellationToken cancellationToken = default);
}
