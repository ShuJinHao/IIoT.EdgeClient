using IIoT.Edge.Module.DryRun.Constants;
using IIoT.Edge.SharedKernel.DataPipeline.CellData;

namespace IIoT.Edge.Module.DryRun.Payload;

public class DryRunCellData : CellDataBase
{
    public override string ProcessType => DryRunModuleConstants.ProcessType;

    public string ScenarioName { get; set; } = "DryRun";

    public string Status { get; set; } = "Pending";

    public override string DisplayLabel => ScenarioName;
}
