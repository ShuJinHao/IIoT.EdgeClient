namespace IIoT.Edge.SharedKernel.DataPipeline;

public enum DeadLetterStage
{
    QueueOverflowPersist = 0,
    PrimaryRetryPersist = 1,
    FallbackPersist = 2,
    FallbackRecoverDeserialize = 3,
    RetryDeserialize = 4,
    CapacityBlocked = 5,
    InvalidPayload = 6,
    DurableShutdownPersist = 7
}
