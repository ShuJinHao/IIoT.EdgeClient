namespace IIoT.Edge.SharedKernel.DataPipeline;

public class DeadLetterRecord
{
    public long Id { get; set; }

    public string ProcessType { get; set; } = string.Empty;

    public string CellDataJson { get; set; } = string.Empty;

    public string FailedTarget { get; set; } = string.Empty;

    public string SourceTable { get; set; } = string.Empty;

    public long? SourceRecordId { get; set; }

    public string FailureStage { get; set; } = string.Empty;

    public string FailureReason { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; }

    public int? NetworkDeviceId { get; set; }

    public string DeviceName { get; set; } = string.Empty;

    public string ModuleId { get; set; } = string.Empty;

    public string TaskKey { get; set; } = string.Empty;

    public string PlanSessionId { get; set; } = string.Empty;

    public string MainPlanCode { get; set; } = string.Empty;

    public string TraceBatchNumber { get; set; } = string.Empty;
}
