namespace IIoT.Edge.Host.DataPipeline.Services;

public enum CloudRetryProcessResult
{
    Continue,
    PauseForRecovery,
    Failed
}
