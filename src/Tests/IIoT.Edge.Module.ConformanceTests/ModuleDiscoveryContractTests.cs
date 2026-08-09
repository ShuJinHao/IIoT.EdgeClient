using IIoT.Edge.Module.Contracts.Config;
using IIoT.Edge.Module.Contracts.Plc;
using IIoT.Edge.Module.Contracts.Plc.Store;
using IIoT.Edge.Module.Contracts.Modules;
using IIoT.Edge.Application.Features.Config.ModuleParameters;
using IIoT.Edge.Module.Contracts.UI;
using IIoT.Edge.Module.Contracts.Runtime;
using IIoT.Edge.Module.Contracts.DataPipeline.CellData;
using Microsoft.Extensions.Configuration;
using System.Reflection;
using System.IO;
using System.Runtime.Loader;

namespace IIoT.Edge.Module.ConformanceTests;

public sealed class ModuleDiscoveryContractTests
{
    [Fact]
    public void AssemblyResolver_WhenNavigationSeamIsRequested_ShouldShareExactDefaultAssembly()
    {
        var pluginRoot = ContractTestPathHelper.CreatePluginRuntimeRoot("TestPlugin");
        try
        {
            var pluginDirectory = Path.Combine(pluginRoot, "TestPlugin");
            var stagedAssemblyPath = Path.Combine(pluginDirectory, "IIoT.Edge.TestPlugin.dll");
            var navigationAssembly = AssemblyLoadContext.Default.LoadFromAssemblyName(
                new AssemblyName("IIoT.Edge.Presentation.Navigation"));
            using var resolver = new ModulePluginAssemblyResolver();
            var pluginAssembly = resolver.LoadAssembly(stagedAssemblyPath, pluginDirectory);
            var pluginLoadContext = Assert.IsAssignableFrom<AssemblyLoadContext>(
                AssemblyLoadContext.GetLoadContext(pluginAssembly));

            var resolved = pluginLoadContext.LoadFromAssemblyName(navigationAssembly.GetName());

            Assert.Same(navigationAssembly, resolved);
            Assert.Same(AssemblyLoadContext.Default, AssemblyLoadContext.GetLoadContext(resolved));
        }
        finally
        {
            ContractTestPathHelper.DeleteDirectory(pluginRoot);
        }
    }

    [Fact]
    public void AssemblyResolver_WhenPluginOwnedCompanionIsLocallyStaged_ShouldLoadCompanionFromArtifactContext()
    {
        var pluginRoot = ContractTestPathHelper.CreatePluginRuntimeRoot("TestPlugin");
        try
        {
            var pluginDirectory = Path.Combine(pluginRoot, "TestPlugin");
            var stagedAssemblyPath = Path.Combine(pluginDirectory, "IIoT.Edge.TestPlugin.dll");
            using var resolver = new ModulePluginAssemblyResolver();

            var pluginAssembly = resolver.LoadAssembly(stagedAssemblyPath, pluginDirectory);
            var pluginLoadContext = Assert.IsAssignableFrom<AssemblyLoadContext>(
                AssemblyLoadContext.GetLoadContext(pluginAssembly));
            var bridge = pluginAssembly.GetType(
                "IIoT.Edge.TestPlugin.TestPluginCompanionBridge",
                throwOnError: true);
            var companionIdentity = bridge!
                .GetProperty("Identity", BindingFlags.Public | BindingFlags.Static)
                ?.GetValue(null);
            var companion = Assert.Single(
                pluginLoadContext.Assemblies,
                assembly => assembly.GetName().Name == "IIoT.Edge.Module.TestPlugin.Companion");

            Assert.Equal("neutral-test-plugin-companion", companionIdentity);
            Assert.Equal(
                PluginPathBoundary.ResolveExistingPhysicalPath(
                    Path.Combine(pluginDirectory, "IIoT.Edge.Module.TestPlugin.Companion.dll")),
                PluginPathBoundary.ResolveExistingPhysicalPath(companion.Location));
            Assert.Same(pluginLoadContext, AssemblyLoadContext.GetLoadContext(companion));
        }
        finally
        {
            ContractTestPathHelper.DeleteDirectory(pluginRoot);
        }
    }

    [Fact]
    public void AssemblyResolver_WhenValidNativeDllIsLocallyStaged_ShouldNotParseItAsManagedAssembly()
    {
        var pluginRoot = ContractTestPathHelper.CreatePluginRuntimeRoot("TestPlugin");
        try
        {
            var pluginDirectory = Path.Combine(pluginRoot, "TestPlugin");
            var stagedAssemblyPath = Path.Combine(pluginDirectory, "IIoT.Edge.TestPlugin.dll");
            var nativeLibraryPath = Path.Combine(
                AppContext.BaseDirectory,
                "runtimes",
                "win-x64",
                "native",
                "e_sqlite3.dll");
            Assert.True(File.Exists(nativeLibraryPath), $"Missing native test asset: {nativeLibraryPath}");
            File.Copy(
                nativeLibraryPath,
                Path.Combine(pluginDirectory, "e_sqlite3.dll"),
                overwrite: true);
            using var resolver = new ModulePluginAssemblyResolver();

            var pluginAssembly = resolver.LoadAssembly(stagedAssemblyPath, pluginDirectory);

            Assert.Equal(
                PluginPathBoundary.ResolveExistingPhysicalPath(stagedAssemblyPath),
                PluginPathBoundary.ResolveExistingPhysicalPath(pluginAssembly.Location));
        }
        finally
        {
            ContractTestPathHelper.DeleteDirectory(pluginRoot);
        }
    }

    [Theory]
    [InlineData("IIoT.Edge.Presentation.Panels")]
    [InlineData("IIoT.Edge.Host.Bootstrap")]
    [InlineData("IIoT.Edge.Host.DataPipeline")]
    [InlineData("IIoT.Edge.Infrastructure.DeviceComm")]
    public void AssemblyResolver_WhenForbiddenHostDependencyIsLocallyStaged_ShouldRejectBeforeEntryLoad(
        string assemblySimpleName)
    {
        var pluginRoot = ContractTestPathHelper.CreatePluginRuntimeRoot("TestPlugin");
        try
        {
            var pluginDirectory = Path.Combine(pluginRoot, "TestPlugin");
            var stagedAssemblyPath = Path.Combine(pluginDirectory, "IIoT.Edge.TestPlugin.dll");
            var defaultAssembly = AssemblyLoadContext.Default.LoadFromAssemblyName(
                new AssemblyName(assemblySimpleName));
            File.Copy(
                defaultAssembly.Location,
                Path.Combine(pluginDirectory, $"{assemblySimpleName}.dll"),
                overwrite: true);
            using var resolver = new ModulePluginAssemblyResolver();

            var exception = Assert.Throws<ModulePluginLoadException>(() =>
                resolver.LoadAssembly(stagedAssemblyPath, pluginDirectory));

            Assert.Contains("未授权宿主程序集", exception.Message, StringComparison.Ordinal);
        }
        finally
        {
            ContractTestPathHelper.DeleteDirectory(pluginRoot);
        }
    }

    [Fact]
    public void AssemblyResolver_WhenSameIdentityIsAlreadyLoaded_ShouldStillUseExactStagedArtifact()
    {
        var projectReferenceAssembly = typeof(IIoT.Edge.TestPlugin.TestPluginRuntimeFactory).Assembly;
        var pluginRoot = ContractTestPathHelper.CreatePluginRuntimeRoot("TestPlugin");
        try
        {
            var pluginDirectory = Path.Combine(pluginRoot, "TestPlugin");
            var stagedAssemblyPath = Path.GetFullPath(
                Path.Combine(pluginDirectory, "IIoT.Edge.TestPlugin.dll"));
            using var resolver = new ModulePluginAssemblyResolver();

            var loaded = resolver.LoadAssembly(stagedAssemblyPath, pluginDirectory);

            Assert.NotSame(projectReferenceAssembly, loaded);
            Assert.Equal(
                PluginPathBoundary.ResolveExistingPhysicalPath(stagedAssemblyPath),
                PluginPathBoundary.ResolveExistingPhysicalPath(loaded.Location));
            Assert.NotNull(loaded.GetType("IIoT.Edge.TestPlugin.DependencyInjection", throwOnError: false));
        }
        finally
        {
            ContractTestPathHelper.DeleteDirectory(pluginRoot);
        }
    }

    [Fact]
    public void AssemblyResolver_WhenTwoPluginDirectoriesContainSameIdentity_ShouldKeepLoadContextsIsolated()
    {
        var pluginRoot = ContractTestPathHelper.CreatePluginRuntimeRoot("TestPlugin");
        try
        {
            var firstDirectory = Path.Combine(pluginRoot, "TestPlugin");
            var secondDirectory = Path.Combine(pluginRoot, "TestPluginClone");
            ContractTestPathHelper.CopyDirectory(firstDirectory, secondDirectory);
            var assemblyName = "IIoT.Edge.TestPlugin.dll";
            var firstPath = Path.Combine(firstDirectory, assemblyName);
            var secondPath = Path.Combine(secondDirectory, assemblyName);
            using var resolver = new ModulePluginAssemblyResolver();

            var firstAssembly = resolver.LoadAssembly(firstPath, firstDirectory);
            var secondAssembly = resolver.LoadAssembly(secondPath, secondDirectory);

            Assert.NotSame(firstAssembly, secondAssembly);
            Assert.NotSame(
                AssemblyLoadContext.GetLoadContext(firstAssembly),
                AssemblyLoadContext.GetLoadContext(secondAssembly));
            Assert.Equal(
                PluginPathBoundary.ResolveExistingPhysicalPath(firstPath),
                PluginPathBoundary.ResolveExistingPhysicalPath(firstAssembly.Location));
            Assert.Equal(
                PluginPathBoundary.ResolveExistingPhysicalPath(secondPath),
                PluginPathBoundary.ResolveExistingPhysicalPath(secondAssembly.Location));
        }
        finally
        {
            ContractTestPathHelper.DeleteDirectory(pluginRoot);
        }
    }

    [Fact]
    public void AssemblyResolver_WhenSecondSameIdentityArtifactIsMissing_ShouldNotReuseFirstPluginAssembly()
    {
        var pluginRoot = ContractTestPathHelper.CreatePluginRuntimeRoot("TestPlugin");
        try
        {
            var firstDirectory = Path.Combine(pluginRoot, "TestPlugin");
            var secondDirectory = Path.Combine(pluginRoot, "TestPluginClone");
            ContractTestPathHelper.CopyDirectory(firstDirectory, secondDirectory);
            var assemblyName = "IIoT.Edge.TestPlugin.dll";
            var firstPath = Path.Combine(firstDirectory, assemblyName);
            var missingSecondPath = Path.Combine(secondDirectory, assemblyName);
            using var resolver = new ModulePluginAssemblyResolver();
            _ = resolver.LoadAssembly(firstPath, firstDirectory);
            File.Delete(missingSecondPath);

            var exception = Assert.Throws<ModulePluginLoadException>(() =>
                resolver.LoadAssembly(missingSecondPath, secondDirectory));

            Assert.IsType<FileNotFoundException>(exception.InnerException);
        }
        finally
        {
            ContractTestPathHelper.DeleteDirectory(pluginRoot);
        }
    }

    [Fact]
    public void DiscoverDirectoryPlugins_ShouldFindTestPluginFixture()
    {
        AssertStagedModuleLayout(
            "TestPlugin",
            "testplugin.module.schema.json",
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
        Assert.Empty(Directory.GetFiles(
            Path.Combine(runtimeDirectory, "Config"),
            "*.module.json",
            SearchOption.TopDirectoryOnly));
        Assert.Empty(Directory.GetFiles(runtimeDirectory, "*.axaml", SearchOption.TopDirectoryOnly));

        if (hasLanguageResources)
        {
            Assert.True(File.Exists(Path.Combine(runtimeDirectory, "Resources", "Languages", "en-US.axaml")));
            Assert.True(File.Exists(Path.Combine(runtimeDirectory, "Resources", "Languages", "zh-CN.axaml")));
        }
    }

    [Fact]
    public void CreateEnabledModules_ShouldInstantiateConfiguredDiscoveredPluginsWithoutDuplicateIdentity()
    {
        var pluginRoot = ContractTestPathHelper.CreatePluginRuntimeRoot("TestPlugin");
        try
        {
            var discovery = DiscoverPlugins(pluginRoot);
            var activation = ActivateAllForTest(discovery.Modules);
            var issues = activation.Issues.ToList();
            var modules = IIoT.Edge.Host.Bootstrap.DependencyInjection
                .BindLegacyProcessTypesFromManifests(
                    activation.Modules,
                    discovery.Modules,
                    issues);

            Assert.Empty(issues);
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
        var assemblyPath = Path.Combine(
            ContractTestPathHelper.GetModuleRuntimeDirectory("TestPlugin"),
            "IIoT.Edge.Module.TestPlugin.Companion.dll");
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
            "IIoT.Edge.Module.TestPlugin.Companion.TestPluginCompanionMarker",
            Path.GetDirectoryName(assemblyPath)!,
            Path.Combine(Path.GetDirectoryName(assemblyPath)!, "plugin.json"),
            assemblyPath);

        var activation = ActivateAllForTest([descriptor]);

        var issue = Assert.Single(activation.Issues, issue => issue.Code == "PLUGIN_LOAD_FAILED");
        Assert.Contains(nameof(IEdgeProcessModule), issue.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("IEdgeStationModule", issue.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RegisterEnabledDiscoveredModules_ShouldNotProduceViewOrRegistrationConflicts()
    {
        var pluginRoot = ContractTestPathHelper.CreatePluginRuntimeRoot("TestPlugin");
        try
        {
            var discovery = DiscoverPlugins(pluginRoot);
            var activation = ActivateAllForTest(discovery.Modules);
            var issues = activation.Issues.ToList();
            var modules = IIoT.Edge.Host.Bootstrap.DependencyInjection
                .BindLegacyProcessTypesFromManifests(
                    activation.Modules,
                    discovery.Modules,
                    issues);
            Assert.Empty(issues);
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

    private static ModuleCatalogActivationResult ActivateAllForTest(
        IReadOnlyList<ModulePluginDescriptor> descriptors)
    {
        var settings = descriptors
            .Select((descriptor, index) => new KeyValuePair<string, string?>(
                $"Modules:Enabled:{index}",
                descriptor.ModuleId));
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(settings)
            .Build();
        return CreateModuleCatalog().CreateEnabledModules(
            configuration,
            "Modules",
            descriptors);
    }

    private sealed class NonModuleEntry
    {
    }

    private sealed class AdditionalTestProcessModule : EdgeProcessModuleBase<AdditionalTestCellData>
    {
        public const string Module = "AdditionalTestPlugin";
        public const string Process = "AdditionalTestPlugin";

        public override string ModuleId => Module;

        public override string ProcessType => Process;

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
