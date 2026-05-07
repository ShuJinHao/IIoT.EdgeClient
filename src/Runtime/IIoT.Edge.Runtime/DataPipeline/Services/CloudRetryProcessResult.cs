namespace IIoT.Edge.Runtime.DataPipeline.Services;

public enum CloudRetryProcessResult
{
    Continue,
    PauseForRecovery,
    Failed
}
