namespace IIoT.Edge.Application.Abstractions.Device;

public sealed record EdgeUploadGateSnapshot
{
    public EdgeUploadGateState State { get; init; } = EdgeUploadGateState.Unknown;

    public EdgeUploadBlockReason Reason { get; init; } = EdgeUploadBlockReason.DeviceUnidentified;

    public DateTimeOffset? TokenExpiresAtUtc { get; init; }

    public DateTimeOffset? LastBootstrapAttemptedAtUtc { get; init; }

    public DateTimeOffset? LastBootstrapSucceededAtUtc { get; init; }

    public DateTimeOffset? LastBootstrapFailedAtUtc { get; init; }
}
