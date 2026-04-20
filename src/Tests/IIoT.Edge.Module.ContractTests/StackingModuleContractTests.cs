using IIoT.Edge.Module.Stacking;

namespace IIoT.Edge.Module.ContractTests;

public sealed class StackingModuleContractTests : ModuleContractTestBase<StackingModule>
{
    protected override bool RequiresHardwareProfile => true;
    protected override int ExpectedRuntimeTaskCount => 1;

    protected override void ConfigureRuntimeServices(IServiceCollection services)
    {
        AddDefaultRuntimeServices(services);
    }
}
