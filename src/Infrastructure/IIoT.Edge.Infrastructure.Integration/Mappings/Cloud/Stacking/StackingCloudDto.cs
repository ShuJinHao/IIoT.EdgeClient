namespace IIoT.Edge.Infrastructure.Integration.Mappings.Cloud.Stacking;

/// <summary>
/// Cloud DTO item for POST /api/v1/edge/pass-stations/stacking.
/// </summary>
public sealed class StackingCloudDto
{
    public string Barcode { get; set; } = string.Empty;
    public string TrayCode { get; set; } = string.Empty;
    public int LayerCount { get; set; }
    public int SequenceNo { get; set; }
    public string CellResult { get; set; } = string.Empty;
    public DateTime CompletedTime { get; set; }
}
