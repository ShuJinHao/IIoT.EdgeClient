using IIoT.Edge.SharedKernel.DataPipeline.CellData;

namespace IIoT.Edge.Module.Homogenization.Payload;

public sealed class HomogenizationCellData : CellDataBase
{
    public override string ProcessType => "Homogenization";

    public override string DisplayLabel => Barcode;

    public string Barcode { get; set; } = string.Empty;

    public string WorkOrderNo { get; set; } = string.Empty;

    public string RuntimeStatus { get; set; } = "Pending";
}