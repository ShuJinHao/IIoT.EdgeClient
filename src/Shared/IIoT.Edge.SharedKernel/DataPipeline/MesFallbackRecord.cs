namespace IIoT.Edge.SharedKernel.DataPipeline;

public class MesFallbackRecord
{
    public long Id { get; set; }
    public string ProcessType { get; set; } = string.Empty;
    public string CellDataJson { get; set; } = string.Empty;
    public string FailedTarget { get; set; } = string.Empty;
    public string ErrorMessage { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
}
