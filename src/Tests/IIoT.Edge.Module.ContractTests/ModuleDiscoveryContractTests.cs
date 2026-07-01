using IIoT.Edge.Application.Abstractions.Config;
using IIoT.Edge.Application.Abstractions.Plc;
using IIoT.Edge.Application.Abstractions.Plc.Store;
using IIoT.Edge.Application.Abstractions.Modules;
using IIoT.Edge.Application.Features.Config.ModuleParameters;
using IIoT.Edge.Application.Modules.Descriptors;
using IIoT.Edge.SharedKernel.Context;
using IIoT.Edge.SharedKernel.DataPipeline.CellData;
using Microsoft.Extensions.Configuration;
using System.IO;
using System.Text.Json;

namespace IIoT.Edge.Module.ContractTests;

public sealed class ModuleDiscoveryContractTests
{
    [Fact]
    public void DiscoverDirectoryPlugins_ShouldFindProductModules()
    {
        var pluginRoot = ContractTestPathHelper.CreatePluginRuntimeRoot("Homogenization", "DieCuttingAnode", "DieCuttingCathode");
        try
        {
            var discovery = DiscoverPlugins(pluginRoot);

            Assert.Equal(
                ["DieCuttingAnode", "DieCuttingCathode", "Homogenization"],
                discovery.Modules.Select(x => x.ModuleId).OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToArray());
        }
        finally
        {
            ContractTestPathHelper.DeleteDirectory(pluginRoot);
        }
    }

    [Fact]
    public void CreateAllModules_ShouldInstantiateAllDiscoveredPluginsWithoutDuplicateIdentity()
    {
        var pluginRoot = ContractTestPathHelper.CreatePluginRuntimeRoot("Homogenization", "DieCuttingAnode", "DieCuttingCathode");
        try
        {
            var modules = CreateModuleCatalog().CreateAllModules(DiscoverPlugins(pluginRoot).Modules);

            Assert.Equal(3, modules.Count);
            Assert.Equal(3, modules.Select(x => x.ModuleId).Distinct(StringComparer.OrdinalIgnoreCase).Count());
            Assert.Equal(3, modules.Select(x => x.ProcessType).Distinct(StringComparer.OrdinalIgnoreCase).Count());
            Assert.Contains(modules, x => string.Equals(x.ModuleId, "DieCuttingAnode", StringComparison.OrdinalIgnoreCase));
            Assert.Contains(modules, x => string.Equals(x.ModuleId, "DieCuttingCathode", StringComparison.OrdinalIgnoreCase));
            Assert.Contains(modules, x => string.Equals(x.ModuleId, "Homogenization", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            ContractTestPathHelper.DeleteDirectory(pluginRoot);
        }
    }

    [Fact]
    public void CreateModule_WhenEntryTypeDoesNotImplementProcessModule_ShouldRejectWithClearContractMessage()
    {
        var assemblyPath = typeof(NonModuleEntry).Assembly.Location;
        var descriptor = new ModulePluginDescriptor(
            "BadModule",
            "BadProcess",
            "错误模块",
            "1.0.0",
            ModulePluginHostRuntime.HostApiVersion,
            "1.0.0",
            "99.0.0",
            [],
            Path.GetFileNameWithoutExtension(assemblyPath),
            typeof(NonModuleEntry).FullName!,
            Path.GetDirectoryName(assemblyPath)!,
            Path.Combine(Path.GetDirectoryName(assemblyPath)!, "plugin.json"),
            assemblyPath);

        var ex = Assert.Throws<InvalidOperationException>(() => CreateModuleCatalog().CreateAllModules([descriptor]));

        Assert.Contains(nameof(IEdgeProcessModule), ex.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("IEdgeStationModule", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RegisterAllDiscoveredModules_ShouldNotProduceViewOrRegistrationConflicts()
    {
        var pluginRoot = ContractTestPathHelper.CreatePluginRuntimeRoot("Homogenization");
        try
        {
            var modules = CreateModuleCatalog().CreateAllModules(DiscoverPlugins(pluginRoot).Modules);
            var services = new ServiceCollection();
            var viewRegistry = new ViewRegistry();
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
                    new ModuleViewRegistry(viewRegistry, module.ModuleId),
                    cellDataRegistry,
                    runtimeRegistry,
                    integrationRegistry,
                moduleParamRegistry));
            }

            Assert.Single(cellDataRegistry.GetRegistrations());
            Assert.Single(runtimeRegistry.GetRegistrations());
            Assert.Single(integrationRegistry.GetCloudUploaders());
            Assert.True(integrationRegistry.TryGetCloudUploader("Homogenization", out var cloudRegistration));
            Assert.Equal(ProcessUploadMode.Batch, cloudRegistration.UploadMode);
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
            x => x.ViewId == "MockProcess.DataView" && x.Title == "模拟工序");
    }

    [Fact]
    public void ProductModules_ShouldUseStandardProductionAndSampleDirectories()
    {
        var repoRoot = ContractTestPathHelper.FindRepoRoot();

        Assert.False(Directory.Exists(Path.Combine(
            repoRoot,
            "src",
            "Modules",
            "IIoT.Edge.Module.Stacking")));
        Assert.True(File.Exists(Path.Combine(
            repoRoot,
            "src",
            "Modules",
            "IIoT.Edge.Module.DieCuttingAnode",
            "plugin.json")));
        Assert.True(File.Exists(Path.Combine(
            repoRoot,
            "src",
            "Modules",
            "IIoT.Edge.Module.DieCuttingCathode",
            "plugin.json")));
        Assert.False(File.Exists(Path.Combine(
            repoRoot,
            "src",
            "Modules",
            "IIoT.Edge.Module.DieCutting.Shared",
            "plugin.json")));
        Assert.True(File.Exists(Path.Combine(
            repoRoot,
            "src",
            "Modules",
            "IIoT.Edge.Module.DieCutting.Shared",
            "Production",
            "DieCuttingStationRuntimeFactory.cs")));
        Assert.True(File.Exists(Path.Combine(
            repoRoot,
            "src",
            "Modules",
            "IIoT.Edge.Module.DieCutting.Shared",
            "Samples",
            "DieCuttingDevelopmentSampleContributor.cs")));
        Assert.True(File.Exists(Path.Combine(
            repoRoot,
            "src",
            "Modules",
            "IIoT.Edge.Module.Homogenization",
            "Production",
            "HomogenizationStationRuntimeFactory.cs")));
        Assert.False(File.Exists(Path.Combine(
            repoRoot,
            "src",
            "Modules",
            "IIoT.Edge.Module.Homogenization",
            "Production",
            "HomogenizationDevelopmentSampleContributor.cs")));
        Assert.True(File.Exists(Path.Combine(
            repoRoot,
            "src",
            "Modules",
            "IIoT.Edge.Module.Homogenization",
            "Samples",
            "HomogenizationDevelopmentSampleContributor.cs")));
        Assert.False(File.Exists(Path.Combine(
            repoRoot,
            "src",
            "Modules",
            "IIoT.Edge.Module.Homogenization",
            "Config",
            "HomogenizationDevelopmentSampleContributor.cs")));
    }

    [Fact]
    public void PluginBundles_ShouldContainHomogenizationSingleLineBundle()
    {
        var repoRoot = ContractTestPathHelper.FindRepoRoot();
        var bundlePath = Path.Combine(repoRoot, "scripts", "PluginBundles", "homogenization-line.json");

        Assert.True(File.Exists(bundlePath));
        using var document = JsonDocument.Parse(File.ReadAllText(bundlePath));
        Assert.Equal("homogenization-line", document.RootElement.GetProperty("bundleId").GetString());
        Assert.Equal("Homogenization", document.RootElement.GetProperty("includeModules")[0].GetString());
        Assert.Equal("HomogenizationLine", document.RootElement.GetProperty("machineProfiles")[0].GetString());
    }

    [Fact]
    public void PluginBundles_ShouldContainPolaritySpecificDieCuttingBundles()
    {
        var repoRoot = ContractTestPathHelper.FindRepoRoot();
        Assert.False(File.Exists(Path.Combine(repoRoot, "scripts", "PluginBundles", "diecutting-line.json")));

        AssertDieCuttingBundle(
            repoRoot,
            "diecutting-anode-line.json",
            "diecutting-anode-line",
            "DieCuttingAnode",
            "DieCuttingAnodeLine");
        AssertDieCuttingBundle(
            repoRoot,
            "diecutting-cathode-line.json",
            "diecutting-cathode-line",
            "DieCuttingCathode",
            "DieCuttingCathodeLine");
    }

    private static void AssertDieCuttingBundle(
        string repoRoot,
        string fileName,
        string expectedBundleId,
        string expectedModuleId,
        string expectedMachineProfile)
    {
        var bundlePath = Path.Combine(repoRoot, "scripts", "PluginBundles", fileName);

        Assert.True(File.Exists(bundlePath));
        using var document = JsonDocument.Parse(File.ReadAllText(bundlePath));
        Assert.Equal(expectedBundleId, document.RootElement.GetProperty("bundleId").GetString());
        Assert.Equal(expectedModuleId, document.RootElement.GetProperty("includeModules")[0].GetString());
        Assert.Equal(expectedMachineProfile, document.RootElement.GetProperty("machineProfiles")[0].GetString());
    }

    private static ModuleCatalogDiscoveryResult DiscoverPlugins(string pluginRoot)
    {
        var discovery = CreateModuleCatalog().DiscoverModules(pluginRoot);
        Assert.Empty(discovery.Issues);
        return discovery;
    }

    private static IModuleCatalog CreateModuleCatalog()
        => new DirectoryModuleCatalog(
            new ModulePluginLoader(new ModulePluginAssemblyResolver()),
            new ModulePluginCompatibilityPolicy());

    private sealed class NonModuleEntry
    {
    }

    private sealed class MockEdgeProcessModule : EdgeProcessModuleBase<MockCellData>
    {
        public const string Module = "MockProcess";
        public const string Process = "MockProcess";

        public override string ModuleId => Module;

        public override string DisplayName => "模拟工序";

        protected override ProcessUploadMode? CloudUploadMode => ProcessUploadMode.Single;

        protected override IStationRuntimeFactory CreateRuntimeFactory()
            => new MockRuntimeFactory();

        protected override void ConfigureModuleServices(IEdgeProcessModuleBuilder builder)
            => builder.RegisterParameters<MockMesParam, MockCloudParam, MockBusinessParam>();

        protected override void RegisterModuleViews(IEdgeProcessModuleBuilder builder)
        {
            builder.RegisterRoute("MockProcess.DataView", typeof(object), typeof(object));
            builder.RegisterMenu(new ModuleMenuDescriptor
            {
                Title = "模拟工序",
                ViewId = "MockProcess.DataView",
                Icon = "Shape",
                Order = 99
            });
        }
    }

    private enum MockMesParam
    {
        [ModuleParam(ParamValueKind.Bool, DefaultValue = "false")]
        启用
    }

    private enum MockCloudParam
    {
        [ModuleParam(ParamValueKind.Bool, DefaultValue = "false")]
        启用
    }

    private enum MockBusinessParam
    {
        [ModuleParam(ParamValueKind.Bool, DefaultValue = "false")]
        启用重码验证
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
