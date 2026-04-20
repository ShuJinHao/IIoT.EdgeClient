using IIoT.Edge.Module.Stacking.Constants;
using IIoT.Edge.SharedKernel.DataPipeline.CellData;

namespace IIoT.Edge.Module.Stacking.Payload;

public class StackingCellData : CellDataBase
{
    public override string ProcessType => StackingModuleConstants.ProcessType;

    public string Barcode { get; set; } = string.Empty;

    public string TrayCode { get; set; } = string.Empty;

    public int LayerCount { get; set; }

    public int SequenceNo { get; set; }

    public string RuntimeStatus { get; set; } = string.Empty;

    public override string DisplayLabel => string.IsNullOrWhiteSpace(Barcode) ? ProcessType : Barcode;
}
