namespace IIoT.Edge.Application.Abstractions.Plc;

public sealed record PlcConnectionRuntimeSnapshot
{
    public int NetworkDeviceId { get; init; }

    public string DeviceName { get; init; } = string.Empty;

    public bool IsConnected { get; init; }

    public DateTimeOffset? LastConnectedAtUtc { get; init; }

    public DateTimeOffset? LastFailureAtUtc { get; init; }

    public string? LastError { get; init; }
}
