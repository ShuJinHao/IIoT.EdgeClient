namespace IIoT.Edge.Application.Abstractions.DataPipeline;

public sealed record DataPipelineEnqueueResult
{
    public bool AcceptedToMemory { get; init; }

    public bool WasOverflow { get; init; }

    public int PersistedTargetCount { get; init; }

    public int SkippedBestEffortCount { get; init; }

    public string ReasonCode { get; init; } = string.Empty;

    /// <summary>
    /// 数据已经进入内存队列，或在内存队列溢出时至少写入一个 durable 本地补偿目标。
    /// </summary>
    public bool IsDurablyAccepted => AcceptedToMemory || (WasOverflow && PersistedTargetCount > 0);

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
