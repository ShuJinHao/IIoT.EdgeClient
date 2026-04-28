using IIoT.Edge.SharedKernel.DataPipeline.CellData;

namespace IIoT.Edge.NonUiRegressionTests;

internal sealed class StackingLikeCellData : CellDataBase
{
    public override string ProcessType => "Stacking";

    public override string DisplayLabel => string.IsNullOrWhiteSpace(Barcode) ? ProcessType : Barcode;

    public string Barcode { get; set; } = string.Empty;

    public string TrayCode { get; set; } = string.Empty;

    public int LayerCount { get; set; }

    public int SequenceNo { get; set; }

    public string RuntimeStatus { get; set; } = string.Empty;
}
