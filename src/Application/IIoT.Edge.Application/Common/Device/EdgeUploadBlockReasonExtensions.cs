using IIoT.Edge.Module.Contracts.Device;

using IIoT.Edge.Module.Contracts.Cloud;
namespace IIoT.Edge.Application.Common.Device;

public static class EdgeUploadBlockReasonExtensions
{
    public static string ToReasonCode(this EdgeUploadBlockReason reason) => reason switch
    {
        EdgeUploadBlockReason.None => "none",
        EdgeUploadBlockReason.DeviceUnidentified => "device_unidentified",
        EdgeUploadBlockReason.MissingUploadToken => "missing_upload_token",
        EdgeUploadBlockReason.ExpiredUploadToken => "expired_upload_token",
        EdgeUploadBlockReason.BootstrapHttpFailure => "bootstrap_http_failure",
        EdgeUploadBlockReason.BootstrapTimeout => "bootstrap_timeout",
        EdgeUploadBlockReason.BootstrapNetworkFailure => "bootstrap_network_failure",
        EdgeUploadBlockReason.BootstrapPayloadInvalid => "bootstrap_payload_invalid",
        EdgeUploadBlockReason.UploadTokenRejected => "upload_token_rejected",
        EdgeUploadBlockReason.CloudUploadDisabled => "cloud_upload_disabled",
        _ => "unknown"
    };
}
