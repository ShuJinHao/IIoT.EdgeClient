using IIoT.Edge.Module.Homogenization;

namespace IIoT.Edge.Module.ContractTests;

public sealed class HomogenizationModuleContractTests : ModuleContractTestBase<HomogenizationModule>
{
    protected override int ExpectedRuntimeTaskCount => 0;
    protected override int MinimumRouteCount => 7;
}
