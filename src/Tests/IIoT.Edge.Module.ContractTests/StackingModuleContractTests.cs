using IIoT.Edge.Module.Stacking;

namespace IIoT.Edge.Module.ContractTests;

public sealed class StackingModuleContractTests : ModuleContractTestBase<StackingModule>
{
    protected override bool RequiresHardwareProfile => true;
}
