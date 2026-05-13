namespace IIoT.Edge.SharedKernel.DataPipeline.Capacity;

/// <summary>
/// 本地产能记录实体。
/// 每个电芯完成时记录一条，用于本地产量、良率统计与后续云端同步。
/// </summary>
public class CapacityRecord
{
    public long Id { get; set; }
    public string Barcode { get; set; } = string.Empty;
    public bool CellResult { get; set; }
    public string ShiftCode { get; set; } = string.Empty;
    public DateTime CompletedTime { get; set; }
    public DateTime CreatedAt { get; set; }
    public string PlcName { get; set; } = string.Empty;
}
