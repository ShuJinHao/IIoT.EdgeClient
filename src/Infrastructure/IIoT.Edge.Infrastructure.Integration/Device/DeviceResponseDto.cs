namespace IIoT.Edge.Infrastructure.Integration.Device;

/// <summary>
/// Minimal payload returned by the device bootstrap endpoint.
/// </summary>
internal sealed class DeviceResponseDto
{
    public Guid Id { get; set; }
    public string DeviceName { get; set; } = string.Empty;
    public Guid ProcessId { get; set; }
}
