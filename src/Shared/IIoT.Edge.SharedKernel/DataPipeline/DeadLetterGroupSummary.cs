namespace IIoT.Edge.SharedKernel.DataPipeline;

public sealed class DeadLetterGroupSummary
{
    public string ProcessType { get; set; } = string.Empty;

    public string? ProcessDisplayName { get; set; }

    public string FailureStage { get; set; } = string.Empty;

    public int Count { get; set; }

    public DateTime? LastCreatedAt { get; set; }
}
