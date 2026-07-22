using IIoT.Edge.Module.Contracts.DataPipeline.CellData;

namespace IIoT.Edge.Testing;

public sealed class TestCellData : CellDataBase
{
    public override string ProcessType => "OtherProcess";

    public override string DisplayLabel => string.IsNullOrWhiteSpace(Barcode) ? ProcessType : Barcode;

    public string Barcode { get; set; } = string.Empty;

    public string TrayCode { get; set; } = string.Empty;

    public int LayerCount { get; set; }

    public int SequenceNo { get; set; }

    public string RuntimeStatus { get; set; } = string.Empty;
}
