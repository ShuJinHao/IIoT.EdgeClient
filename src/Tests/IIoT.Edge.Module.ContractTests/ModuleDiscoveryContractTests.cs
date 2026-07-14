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
    public void DiscoverDirectoryPlugins_ShouldFindTestPluginFixture()
    {
        AssertStagedModuleLayout(
            "TestPlugin",
            "test-plugin.module.json",
            "IIoT.Edge.TestPlugin.dll");

        var pluginRoot = ContractTestPathHelper.CreatePluginRuntimeRoot("TestPlugin");
        try
        {
            var discovery = DiscoverPlugins(pluginRoot);

            Assert.Empty(discovery.Issues);
            Assert.Equal(
                ["TestPlugin"],
                discovery.Modules.Select(x => x.ModuleId).OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToArray());
        }
        finally
        {
            ContractTestPathHelper.DeleteDirectory(pluginRoot);
        }
    }

    private static void AssertStagedModuleLayout(
        string moduleId,
        string configFileName,
        string entryAssemblyName,
        bool hasLanguageResources = false)
    {
        var runtimeDirectory = ContractTestPathHelper.GetModuleRuntimeDirectory(moduleId);

        Assert.Equal(
            Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "Modules", moduleId)),
            Path.GetFullPath(runtimeDirectory));
        Assert.True(File.Exists(Path.Combine(runtimeDirectory, "plugin.json")));
        Assert.True(File.Exists(Path.Combine(runtimeDirectory, entryAssemblyName)));
        Assert.True(File.Exists(Path.Combine(runtimeDirectory, "Config", configFileName)));
        Assert.Empty(Directory.GetFiles(runtimeDirectory, "*.module.json", SearchOption.TopDirectoryOnly));
        Assert.Empty(Directory.GetFiles(runtimeDirectory, "*.axaml", SearchOption.TopDirectoryOnly));

        if (hasLanguageResources)
        {
            Assert.True(File.Exists(Path.Combine(runtimeDirectory, "Resources", "Languages", "en-US.axaml")));
            Assert.True(File.Exists(Path.Combine(runtimeDirectory, "Resources", "Languages", "zh-CN.axaml")));
        }
    }

    [Fact]
    public void CreateAllModules_ShouldInstantiateAllDiscoveredPluginsWithoutDuplicateIdentity()
    {
        var pluginRoot = ContractTestPathHelper.CreatePluginRuntimeRoot("TestPlugin");
        try
        {
            var modules = CreateModuleCatalog().CreateAllModules(DiscoverPlugins(pluginRoot).Modules);

            Assert.Single(modules);
            Assert.Single(modules.Select(x => x.ModuleId).Distinct(StringComparer.OrdinalIgnoreCase));
            Assert.Single(modules.Select(x => x.ProcessType).Distinct(StringComparer.OrdinalIgnoreCase));
            Assert.Contains(modules, x => string.Equals(x.ModuleId, "TestPlugin", StringComparison.OrdinalIgnoreCase));
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
        var pluginRoot = ContractTestPathHelper.CreatePluginRuntimeRoot("TestPlugin");
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
            Assert.Empty(integrationRegistry.GetCloudUploaders());
            Assert.Empty(integrationRegistry.GetMesUploaders());
            Assert.Empty(moduleParamRegistry.GetRegistrations());
            Assert.NotNull(viewRegistry.GetViewRegistration("TestPlugin.DataView"));
        }
        finally
        {
            ContractTestPathHelper.DeleteDirectory(pluginRoot);
        }
    }

    [Fact]
    public void RegisterAdditionalTestModule_ShouldRequireZeroHostChanges()
    {
        var result = new ModuleContractFixture().RegisterModule(new AdditionalTestProcessModule());

        Assert.True(result.CellDataRegistry.IsRegistered(AdditionalTestProcessModule.Process));
        Assert.True(result.RuntimeRegistry.HasFactory(AdditionalTestProcessModule.Module));
        Assert.True(result.IntegrationRegistry.HasCloudUploader(AdditionalTestProcessModule.Process));
        Assert.False(result.IntegrationRegistry.HasMesUploader(AdditionalTestProcessModule.Process));
        Assert.True(result.ModuleParamRegistry.TryGetRegistration(
            typeof(MockMesParam),
            typeof(MockCloudParam),
            typeof(MockBusinessParam),
            out _));
        Assert.NotNull(result.ViewRegistry.GetViewRegistration("AdditionalTestPlugin.DataView"));
        Assert.Contains(
            result.ViewRegistry.GetAllMenus(),
            x => x.ViewId == "AdditionalTestPlugin.DataView" && x.Title == "Additional Test Plugin");
    }

    [Fact]
    public void ProductModules_ShouldUseStandardProductionAndSampleDirectories()
    {
        var repoRoot = ContractTestPathHelper.FindRepoRoot();

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

    private sealed class AdditionalTestProcessModule : EdgeProcessModuleBase<AdditionalTestCellData>
    {
        public const string Module = "AdditionalTestPlugin";
        public const string Process = "AdditionalTestPlugin";

        public override string ModuleId => Module;

        public override string DisplayName => "Additional Test Plugin";

        protected override ProcessUploadMode? CloudUploadMode => ProcessUploadMode.Single;

        protected override IStationRuntimeFactory CreateRuntimeFactory()
            => new AdditionalTestRuntimeFactory();

        protected override void ConfigureModuleServices(IEdgeProcessModuleBuilder builder)
            => builder.RegisterParameters<MockMesParam, MockCloudParam, MockBusinessParam>();

        protected override void RegisterModuleViews(IEdgeProcessModuleBuilder builder)
        {
            builder.RegisterRoute("AdditionalTestPlugin.DataView", typeof(object), typeof(object));
            builder.RegisterMenu(new ModuleMenuDescriptor
            {
                Title = "Additional Test Plugin",
                ViewId = "AdditionalTestPlugin.DataView",
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

    private sealed class AdditionalTestCellData : CellDataBase
    {
        public override string ProcessType => AdditionalTestProcessModule.Process;
    }

    private sealed class AdditionalTestRuntimeFactory : IStationRuntimeFactory
    {
        public string ModuleId => AdditionalTestProcessModule.Module;

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
