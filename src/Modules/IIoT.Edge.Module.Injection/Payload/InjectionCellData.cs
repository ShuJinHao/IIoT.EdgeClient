using IIoT.Edge.SharedKernel.DataPipeline.CellData;

namespace IIoT.Edge.Module.Injection.Payload;

public class InjectionCellData : CellDataBase
{
    public override string ProcessType => InjectionModule.ModuleKey;

    public string WorkOrderNo { get; set; } = string.Empty;

    public string Barcode { get; set; } = string.Empty;

    public DateTime? ScanTime { get; set; }

    public double PreInjectionWeight { get; set; }

    public double PostInjectionWeight { get; set; }

    public double InjectionVolume { get; set; }

    public override string DisplayLabel => string.IsNullOrEmpty(Barcode) ? ProcessType : Barcode;
}
