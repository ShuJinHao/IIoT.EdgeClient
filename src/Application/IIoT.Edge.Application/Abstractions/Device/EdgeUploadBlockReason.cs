namespace IIoT.Edge.Application.Abstractions.Device;

public enum EdgeUploadBlockReason
{
    None = 0,
    DeviceUnidentified = 1,
    MissingUploadToken = 2,
    ExpiredUploadToken = 3,
    BootstrapHttpFailure = 4,
    BootstrapTimeout = 5,
    BootstrapNetworkFailure = 6,
    BootstrapPayloadInvalid = 7,
    UploadTokenRejected = 8
}
