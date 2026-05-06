using IIoT.Edge.SharedKernel.DataPipeline.CellData;

namespace IIoT.Edge.NonUiRegressionTests;

internal sealed class TestProcessCellData : CellDataBase
{
    public const string ProcessTypeKey = "TestProcess";

    public override string ProcessType => ProcessTypeKey;

    public override string DisplayLabel => string.IsNullOrWhiteSpace(Barcode) ? ProcessType : Barcode;

    public string Barcode { get; set; } = string.Empty;

    public string WorkOrderNo { get; set; } = string.Empty;

    public DateTime? ScanTime { get; set; }

    public double? MeasurementValue { get; set; }
}
