using IIoT.Edge.SharedKernel.Modules.Stacking;

namespace IIoT.Edge.SharedKernel.DataPipeline.CellData;

/// <summary>
/// Minimal stacking runtime sample data used for development bootstrap
/// and the first runtime capture slice.
/// </summary>
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
