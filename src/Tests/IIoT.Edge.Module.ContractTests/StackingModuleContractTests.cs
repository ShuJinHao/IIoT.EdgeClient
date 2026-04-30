using IIoT.Edge.Application.Abstractions.Modules;
using IIoT.Edge.Module.Stacking;
using IIoT.Edge.Module.Stacking.Integration;
using StackingCloudUploadChannel = IIoT.Edge.Application.Modules.Cloud.ICloudUploadChannel<
    IIoT.Edge.Module.Stacking.Payload.StackingCellData,
    object>;

namespace IIoT.Edge.Module.ContractTests;

public sealed class StackingModuleContractTests : ModuleContractTestBase<DependencyInjection>
{
    protected override bool RequiresHardwareProfile => true;
    protected override int ExpectedRuntimeTaskCount => 1;
    protected override int MinimumRouteCount => 7;

    protected override void ConfigureRuntimeServices(IServiceCollection services)
    {
        AddDefaultRuntimeServices(services);
    }

    [Fact]
    public void RegisterServices_ShouldRegisterCloudChannelAbstractionAndProcessUploader()
    {
        var result = new ModuleContractFixture().RegisterModule(new DependencyInjection());

        Assert.Contains(
            result.Services,
            descriptor => descriptor.ServiceType == typeof(StackingCloudUploader)
                          && descriptor.ImplementationType == typeof(StackingCloudUploader));
        Assert.Contains(
            result.Services,
            descriptor => descriptor.ServiceType == typeof(StackingCloudUploadChannel)
                          && descriptor.ImplementationFactory is not null);
        Assert.Contains(
            result.Services,
            descriptor => descriptor.ServiceType == typeof(IProcessCloudUploader)
                          && descriptor.ImplementationFactory is not null);
    }
}
