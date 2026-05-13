namespace IIoT.Edge.Application.Abstractions.Device;

public enum CloudCallOutcome
{
    Success = 0,
    SkippedUploadNotReady = 1,
    UnauthorizedAfterRetry = 2,
    HttpFailure = 3,
    NetworkFailure = 4,
    Exception = 5
}
