using IIoT.Edge.SharedKernel.Modules.DryRun;

namespace IIoT.Edge.SharedKernel.DataPipeline.CellData;

public class DryRunCellData : CellDataBase
{
    public override string ProcessType => DryRunModuleConstants.ProcessType;

    public string ScenarioName { get; set; } = "DryRun";

    public string Status { get; set; } = "Pending";

    public override string DisplayLabel => ScenarioName;
}
