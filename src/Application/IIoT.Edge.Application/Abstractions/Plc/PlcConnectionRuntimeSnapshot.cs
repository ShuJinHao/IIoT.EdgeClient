using IIoT.Edge.SharedKernel.Identity;

namespace IIoT.Edge.Application.Abstractions.Plc;

public sealed record PlcConnectionRuntimeSnapshot : IDeviceIdentifiable
{
    public int NetworkDeviceId { get; init; }

    public string DeviceName { get; init; } = string.Empty;

    public bool IsConnected { get; init; }

    public DateTimeOffset? LastConnectedAtUtc { get; init; }

    public DateTimeOffset? LastFailureAtUtc { get; init; }

    public string? LastError { get; init; }
}
