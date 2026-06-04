namespace IIoT.Edge.Application.Abstractions.Shared;

public sealed record UploadGateSnapshot(
    ExternalSystemKind System,
    bool CanUpload,
    string ReasonCode,
    string? Message)
{
    public static UploadGateSnapshot Ready(ExternalSystemKind system)
        => new(system, true, "ready", null);

    public static UploadGateSnapshot Blocked(
        ExternalSystemKind system,
        string reasonCode,
        string? message = null)
        => new(
            system,
            false,
            string.IsNullOrWhiteSpace(reasonCode) ? "upload_gate_blocked" : reasonCode,
            message);
}
public interface IExternalUploadGate
{
    ExternalSystemKind System { get; }

    UploadGateSnapshot GetSnapshot();
}
