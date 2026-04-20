namespace IIoT.Edge.Application.Abstractions.DataPipeline;

public sealed record DataPipelineEnqueueResult
{
    public bool AcceptedToMemory { get; init; }

    public bool WasOverflow { get; init; }

    public int PersistedTargetCount { get; init; }

    public int SkippedBestEffortCount { get; init; }

    public string ReasonCode { get; init; } = string.Empty;

    public static DataPipelineEnqueueResult Accepted()
        => new()
        {
            AcceptedToMemory = true,
            ReasonCode = "queued"
        };

    public static DataPipelineEnqueueResult Rejected(string reasonCode)
        => new()
        {
            ReasonCode = reasonCode
        };

    public static DataPipelineEnqueueResult OverflowPersisted(
        int persistedTargetCount,
        int skippedBestEffortCount)
        => new()
        {
            WasOverflow = true,
            PersistedTargetCount = persistedTargetCount,
            SkippedBestEffortCount = skippedBestEffortCount,
            ReasonCode = persistedTargetCount > 0
                ? "overflow_persisted"
                : "overflow_skipped_best_effort"
        };
}
