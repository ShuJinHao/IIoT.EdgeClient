namespace IIoT.Edge.Application.Abstractions.Device;

/// <summary>
/// Device session resolved from cloud bootstrap.
/// DeviceId is used for business uploads, DeviceName for UI display,
/// and ClientCode keeps the configured workstation code used during bootstrap.
/// </summary>
public record DeviceSession
{
    public Guid DeviceId { get; init; }

    public string DeviceName { get; init; } = string.Empty;

    public string ClientCode { get; init; } = string.Empty;

    public Guid ProcessId { get; init; }

    public string? UploadAccessToken { get; init; }

    public DateTimeOffset? UploadAccessTokenExpiresAtUtc { get; init; }
}
