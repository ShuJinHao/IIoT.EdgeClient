using IIoT.Edge.SharedKernel.Identity;

namespace IIoT.Edge.Application.Abstractions.Plc;

public enum PlcConnectionState
{
    Unknown = 0,
    Connecting = 1,
    Connected = 2,
    Retrying = 3,
    Disconnected = 4,
    Faulted = 5
}

public sealed record PlcConnectionRuntimeSnapshot : IDeviceIdentifiable
{
    public int NetworkDeviceId { get; init; }

    public string DeviceName { get; init; } = string.Empty;

    public bool IsConnected { get; init; }

    public PlcConnectionState ConnectionState { get; init; } = PlcConnectionState.Unknown;

    public DateTimeOffset? LastAttemptAtUtc { get; init; }

    public DateTimeOffset? LastConnectedAtUtc { get; init; }

    public DateTimeOffset? LastReadAtUtc { get; init; }

    public DateTimeOffset? LastFailureAtUtc { get; init; }

    public DateTimeOffset? StateChangedAtUtc { get; init; }

    public string? LastError { get; init; }

    public int? LatencyMs { get; init; }
}
