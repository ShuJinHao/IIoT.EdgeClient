namespace IIoT.Edge.Module.Stacking.Integration;

public sealed class StackingCloudDto
{
    public string Barcode { get; set; } = string.Empty;
    public string TrayCode { get; set; } = string.Empty;
    public int LayerCount { get; set; }
    public int SequenceNo { get; set; }
    public string CellResult { get; set; } = string.Empty;
    public DateTime CompletedTime { get; set; }
}
