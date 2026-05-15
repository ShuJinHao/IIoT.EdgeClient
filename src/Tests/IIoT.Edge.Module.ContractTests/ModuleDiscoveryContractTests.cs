using IIoT.Edge.Application.Abstractions.Config;
using IIoT.Edge.Application.Abstractions.Plc;
using IIoT.Edge.Application.Abstractions.Plc.Store;
using IIoT.Edge.Application.Features.Config.ModuleParameters;
using IIoT.Edge.Application.Modules.Descriptors;
using IIoT.Edge.SharedKernel.Context;
using IIoT.Edge.SharedKernel.DataPipeline.CellData;
using Microsoft.Extensions.Configuration;
using System.Text.Json;

namespace IIoT.Edge.Module.ContractTests;

public sealed class ModuleDiscoveryContractTests
{
    [Fact]
    public void DiscoverDirectoryPlugins_ShouldFindAvaloniaProductModules()
    {
        var pluginRoot = ContractTestPathHelper.CreatePluginRuntimeRoot("Homogenization");
        try
        {
            var discovery = DiscoverPlugins(pluginRoot);

            Assert.Equal(
                ["Homogenization"],
                discovery.Modules.Select(x => x.ModuleId).OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToArray());
            Assert.All(discovery.Modules, descriptor =>
                Assert.EndsWith(".Avalonia", descriptor.AssemblyName, StringComparison.Ordinal));
        }
        finally
        {
            ContractTestPathHelper.DeleteDirectory(pluginRoot);
        }
    }

    [Fact]
    public void CreateAllModules_ShouldInstantiateAvaloniaPluginsWithoutDuplicateIdentity()
    {
        var pluginRoot = ContractTestPathHelper.CreatePluginRuntimeRoot("Homogenization");
        try
        {
            var modules = CreateModuleCatalog().CreateAllModules(DiscoverPlugins(pluginRoot).Modules);

            Assert.Single(modules);
            Assert.Single(modules.Select(x => x.ModuleId).Distinct(StringComparer.OrdinalIgnoreCase));
            Assert.Single(modules.Select(x => x.ProcessType).Distinct(StringComparer.OrdinalIgnoreCase));
            Assert.Contains(modules, x => string.Equals(x.ModuleId, "Homogenization", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            ContractTestPathHelper.DeleteDirectory(pluginRoot);
        }
    }

    [Fact]
    public void RegisterAllDiscoveredModules_ShouldRegisterAvaloniaRoutesAndRuntimeContracts()
    {
        var pluginRoot = ContractTestPathHelper.CreatePluginRuntimeRoot("Homogenization");
        try
        {
            var modules = CreateModuleCatalog().CreateAllModules(DiscoverPlugins(pluginRoot).Modules);
            var services = new ServiceCollection();
            var viewRegistry = new AvaloniaViewRegistry();
            var cellDataRegistry = new CellDataRegistry(new CellDataTypeRegistry());
            var runtimeRegistry = new StationRuntimeRegistry();
            var integrationRegistry = new ProcessIntegrationRegistry();
            var moduleParamRegistry = new ModuleParamRegistry();

            foreach (var module in modules)
            {
                module.Configure(new TestEdgeProcessModuleBuilder(
                    module.ModuleId,
                    module.ProcessType,
                    services,
                    new ConfigurationBuilder().Build(),
                    viewRegistry,
                    cellDataRegistry,
                    runtimeRegistry,
                    integrationRegistry,
                    moduleParamRegistry));
            }

            Assert.Single(cellDataRegistry.GetRegistrations());
            Assert.Single(runtimeRegistry.GetRegistrations());
            Assert.Single(integrationRegistry.GetCloudUploaders());
            Assert.Single(moduleParamRegistry.GetRegistrations());
            Assert.NotNull(viewRegistry.GetViewRegistration("Homogenization.DataView"));
        }
        finally
        {
            ContractTestPathHelper.DeleteDirectory(pluginRoot);
        }
    }

    [Fact]
    public void RegisterMockNewModule_ShouldRequireZeroHostChanges()
    {
        var result = new ModuleContractFixture().RegisterModule(new MockEdgeProcessModule());

        Assert.True(result.CellDataRegistry.IsRegistered(MockEdgeProcessModule.Process));
        Assert.True(result.RuntimeRegistry.HasFactory(MockEdgeProcessModule.Module));
        Assert.True(result.IntegrationRegistry.HasCloudUploader(MockEdgeProcessModule.Process));
        Assert.False(result.IntegrationRegistry.HasMesUploader(MockEdgeProcessModule.Process));
        Assert.True(result.ModuleParamRegistry.TryGetRegistration(
            typeof(MockMesParam),
            typeof(MockCloudParam),
            typeof(MockBusinessParam),
            out _));
        Assert.NotNull(result.ViewRegistry.GetViewRegistration("MockProcess.DataView"));
        Assert.Contains(
            result.ViewRegistry.GetAllMenus(),
            x => x.ViewId == "MockProcess.DataView" && x.TitleResourceKey == "MockProcess_Menu_Data");
    }

    [Fact]
    public void ProductModules_ShouldUseCoreRuntimeAndAvaloniaPluginShell()
    {
        var repoRoot = ContractTestPathHelper.FindRepoRoot();

        Assert.False(Directory.Exists(Path.Combine(
            repoRoot,
            "src",
            "Modules",
            "IIoT.Edge.Module.Homogenization")));
        Assert.True(File.Exists(Path.Combine(
            repoRoot,
            "src",
            "Modules",
            "IIoT.Edge.Module.Homogenization.Core",
            "Runtime",
            "HomogenizationStationRuntimeFactory.cs")));
        Assert.True(File.Exists(Path.Combine(
            repoRoot,
            "src",
            "Modules",
            "IIoT.Edge.Module.Homogenization.Avalonia",
            "Presentation",
            "HomogenizationAvaloniaNavigationRegistration.cs")));
    }

    [Fact]
    public void PluginManifest_ShouldPointToAvaloniaEntryAssembly()
    {
        var manifestPath = Path.Combine(
            ContractTestPathHelper.GetModuleSourceDirectory("Homogenization"),
            "plugin.json");

        using var document = JsonDocument.Parse(File.ReadAllText(manifestPath));
        var root = document.RootElement;

        Assert.Equal("Homogenization", root.GetProperty("moduleId").GetString());
        Assert.Equal("IIoT.Edge.Module.Homogenization.Avalonia.dll", root.GetProperty("entryAssembly").GetString());
        Assert.Equal(
            "IIoT.Edge.Module.Homogenization.Avalonia.DependencyInjection",
            root.GetProperty("entryType").GetString());
    }

    private static ModuleCatalogDiscoveryResult DiscoverPlugins(string pluginRoot)
    {
        var discovery = CreateModuleCatalog().DiscoverModules(pluginRoot);
        Assert.Empty(discovery.Issues);
        return discovery;
    }

    private static IModuleCatalog CreateModuleCatalog()
        => new DirectoryModuleCatalog(new ModulePluginLoader(new ModulePluginAssemblyResolver()));

    private sealed class MockEdgeProcessModule : EdgeProcessModuleBase<MockCellData>
    {
        public const string Module = "MockProcess";
        public const string Process = "MockProcess";

        public override string ModuleId => Module;

        public override string DisplayName => "模拟工序";

        protected override ProcessUploadMode CloudUploadMode => ProcessUploadMode.Single;

        protected override IStationRuntimeFactory CreateRuntimeFactory()
            => new MockRuntimeFactory();

        protected override void ConfigureModuleServices(IEdgeProcessModuleBuilder builder)
            => builder.RegisterParameters<MockMesParam, MockCloudParam, MockBusinessParam>();

        protected override void RegisterModuleViews(IEdgeProcessModuleBuilder builder)
        {
            builder.RegisterRoute("MockProcess.DataView", typeof(Avalonia.Controls.UserControl), typeof(object));
            builder.RegisterMenu(new ModuleMenuDescriptor
            {
                Title = "模拟工序",
                TitleResourceKey = "MockProcess_Menu_Data",
                ViewId = "MockProcess.DataView",
                Icon = "Shape",
                Order = 99
            });
        }
    }

    private enum MockMesParam
    {
        [ModuleParam(ParamValueKind.Bool, DefaultValue = "false")]
        Enabled
    }

    private enum MockCloudParam
    {
        [ModuleParam(ParamValueKind.Bool, DefaultValue = "false")]
        Enabled
    }

    private enum MockBusinessParam
    {
        [ModuleParam(ParamValueKind.Bool, DefaultValue = "false")]
        DuplicateCheckEnabled
    }

    private sealed class MockCellData : CellDataBase
    {
        public override string ProcessType => MockEdgeProcessModule.Process;
    }

    private sealed class MockRuntimeFactory : IStationRuntimeFactory
    {
        public string ModuleId => MockEdgeProcessModule.Module;

        public IReadOnlyCollection<TaskCandidate> GetTaskCandidates()
            => [];

        public List<IPlcTask> CreateTasks(
            IServiceProvider serviceProvider,
            IPlcBuffer buffer,
            ProductionContext context,
            IReadOnlySet<string> enabledTaskKeys)
            => [];
    }
}
