using IIoT.Edge.Application.Abstractions.Modules;
using IIoT.Edge.Module.Injection;
using IIoT.Edge.Module.Injection.Integration;
using InjectionCloudUploadChannel = IIoT.Edge.Application.Modules.Cloud.ICloudUploadChannel<
    IIoT.Edge.Module.Injection.Payload.InjectionCellData,
    object>;

namespace IIoT.Edge.Module.ContractTests;

public sealed class InjectionModuleContractTests : ModuleContractTestBase<DependencyInjection>
{
    [Fact]
    public void RegisterServices_ShouldRegisterCloudChannelAbstractionAndProcessUploader()
    {
        var result = new ModuleContractFixture().RegisterModule(new DependencyInjection());

        Assert.Contains(
            result.Services,
            descriptor => descriptor.ServiceType == typeof(InjectionCloudUploader)
                          && descriptor.ImplementationType == typeof(InjectionCloudUploader));
        Assert.Contains(
            result.Services,
            descriptor => descriptor.ServiceType == typeof(InjectionCloudUploadChannel)
                          && descriptor.ImplementationFactory is not null);
        Assert.Contains(
            result.Services,
            descriptor => descriptor.ServiceType == typeof(IProcessCloudUploader)
                          && descriptor.ImplementationFactory is not null);
    }
}
