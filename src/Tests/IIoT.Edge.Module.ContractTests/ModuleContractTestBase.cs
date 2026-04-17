namespace IIoT.Edge.Module.ContractTests;

public abstract class ModuleContractTestBase<TModule>
    where TModule : IEdgeStationModule, new()
{
    private readonly ModuleContractFixture _fixture = new();

    protected virtual bool RequiresHardwareProfile => false;
    protected virtual bool RequiresMesUploader => false;

    protected virtual int MinimumRouteCount => 1;

    protected TModule CreateModule() => new();

    [Fact]
    public void RegisterPipeline_ShouldPopulateRequiredModuleContracts()
    {
        var module = CreateModule();
        var result = _fixture.RegisterModule(module);

        Assert.True(result.CellDataRegistry.IsRegistered(module.ProcessType));
        Assert.True(result.RuntimeRegistry.HasFactory(module.ModuleId));
        Assert.True(result.IntegrationRegistry.HasCloudUploader(module.ProcessType));
        Assert.Equal(
            RequiresMesUploader,
            result.IntegrationRegistry.HasMesUploader(module.ProcessType));

        var moduleRoutes = result.ViewRegistry.GetAllViewRegistrations()
            .Where(x => x.ViewId.StartsWith($"{module.ModuleId}.", StringComparison.Ordinal))
            .ToArray();
        Assert.True(moduleRoutes.Length >= MinimumRouteCount,
            $"Module '{module.ModuleId}' should register at least {MinimumRouteCount} route(s).");

        var moduleMenus = result.ViewRegistry.GetAllMenus()
            .Where(x => x.ViewId.StartsWith($"{module.ModuleId}.", StringComparison.Ordinal))
            .ToArray();
        Assert.NotEmpty(moduleMenus);
    }

    [Fact]
    public void RegisterServices_ShouldRegisterCloudUploaderAndOptionalHardwareProfile()
    {
        var module = CreateModule();
        var result = _fixture.RegisterModule(module);

        var cloudUploaderDescriptors = result.Services
            .Where(static x => x.ServiceType == typeof(IProcessCloudUploader))
            .ToArray();
        Assert.NotEmpty(cloudUploaderDescriptors);

        var hardwareProfileDescriptors = result.Services
            .Where(static x => x.ServiceType == typeof(IModuleHardwareProfileProvider))
            .ToArray();

        if (RequiresHardwareProfile)
        {
            Assert.NotEmpty(hardwareProfileDescriptors);
        }
        else
        {
            Assert.Empty(hardwareProfileDescriptors);
        }

        var mesUploaderDescriptors = result.Services
            .Where(static x => x.ServiceType == typeof(IProcessMesUploader))
            .ToArray();

        if (RequiresMesUploader)
        {
            Assert.NotEmpty(mesUploaderDescriptors);
        }
    }

    [Fact]
    public void RegisterViews_ShouldKeepModuleRoutesOutOfCoreNamespace()
    {
        var module = CreateModule();
        var result = _fixture.RegisterModule(module);

        Assert.All(
            result.ViewRegistry.GetAllViewRegistrations()
                .Where(x => x.ViewId.StartsWith($"{module.ModuleId}.", StringComparison.Ordinal)),
            view => Assert.True(
                view.ViewId.StartsWith($"{module.ModuleId}.", StringComparison.Ordinal),
                $"View '{view.ViewId}' must use the '{module.ModuleId}.' prefix."));

        Assert.All(
            result.ViewRegistry.GetAllMenus()
                .Where(x => x.ViewId.StartsWith($"{module.ModuleId}.", StringComparison.Ordinal)),
            menu => Assert.True(
                menu.ViewId.StartsWith($"{module.ModuleId}.", StringComparison.Ordinal),
                $"Menu view id '{menu.ViewId}' must use the '{module.ModuleId}.' prefix."));
    }
}
