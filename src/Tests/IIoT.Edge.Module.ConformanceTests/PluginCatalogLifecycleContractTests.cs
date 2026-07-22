using IIoT.Edge.Module.Contracts.Modules;
using Microsoft.Extensions.Configuration;
using System.Reflection;
using System.Text.Json.Nodes;

namespace IIoT.Edge.Module.ConformanceTests;

public sealed class PluginCatalogLifecycleContractTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DiscoverModules_WhenEntryAssemblyEscapesStagedDirectory_ShouldRejectManifest(bool useAbsolutePath)
    {
        var pluginRoot = CreatePluginRoot("TestPlugin");
        try
        {
            var pluginDirectory = Path.Combine(pluginRoot, "TestPlugin");
            var manifestPath = Path.Combine(pluginDirectory, "plugin.json");
            var outsideAssembly = Path.Combine(pluginRoot, "outside-test-plugin.dll");
            File.Copy(
                Path.Combine(pluginDirectory, "IIoT.Edge.TestPlugin.dll"),
                outsideAssembly,
                overwrite: true);
            var manifest = JsonNode.Parse(File.ReadAllText(manifestPath))!.AsObject();
            manifest["entryAssembly"] = useAbsolutePath
                ? outsideAssembly
                : "../outside-test-plugin.dll";
            File.WriteAllText(manifestPath, manifest.ToJsonString(new() { WriteIndented = true }));

            var discovery = CreateModuleCatalog().DiscoverModules(pluginRoot);

            Assert.Empty(discovery.Modules);
            Assert.Equal("PLUGIN_MANIFEST_INVALID", Assert.Single(discovery.Issues).Code);
        }
        finally
        {
            ContractTestPathHelper.DeleteDirectory(pluginRoot);
        }
    }

    [Fact]
    public void DiscoverModules_WhenEntryAssemblySymlinkEscapesStagedDirectory_ShouldRejectManifest()
    {
        var pluginRoot = CreatePluginRoot("TestPlugin");
        try
        {
            var pluginDirectory = Path.Combine(pluginRoot, "TestPlugin");
            var entryAssemblyPath = Path.Combine(pluginDirectory, "IIoT.Edge.TestPlugin.dll");
            var outsideAssemblyPath = Path.Combine(pluginRoot, "outside-test-plugin.dll");
            File.Copy(entryAssemblyPath, outsideAssemblyPath, overwrite: true);
            File.Delete(entryAssemblyPath);
            File.CreateSymbolicLink(entryAssemblyPath, outsideAssemblyPath);

            var discovery = CreateModuleCatalog().DiscoverModules(pluginRoot);

            Assert.Empty(discovery.Modules);
            var issue = Assert.Single(discovery.Issues);
            Assert.Equal("PLUGIN_MANIFEST_INVALID", issue.Code);
            Assert.Contains("staged", issue.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            ContractTestPathHelper.DeleteDirectory(pluginRoot);
        }
    }

    [Fact]
    public void DiscoverModules_WhenPluginDirectorySymlinkEscapesPluginRoot_ShouldRejectManifest()
    {
        var outsideRoot = CreatePluginRoot("TestPlugin");
        var pluginRoot = ContractTestPathHelper.CreateTempDirectory("edge-plugin-root-boundary-tests");
        try
        {
            Directory.CreateSymbolicLink(
                Path.Combine(pluginRoot, "TestPlugin"),
                Path.Combine(outsideRoot, "TestPlugin"));

            var discovery = CreateModuleCatalog().DiscoverModules(pluginRoot);

            Assert.Empty(discovery.Modules);
            var issue = Assert.Single(discovery.Issues);
            Assert.Equal("PLUGIN_MANIFEST_INVALID", issue.Code);
            Assert.Contains("真实路径", issue.Message, StringComparison.Ordinal);
        }
        finally
        {
            ContractTestPathHelper.DeleteDirectory(pluginRoot);
            ContractTestPathHelper.DeleteDirectory(outsideRoot);
        }
    }

    [Fact]
    public void DiscoverModules_WhenManifestSymlinkEscapesStagedDirectory_ShouldRejectManifest()
    {
        var pluginRoot = CreatePluginRoot("TestPlugin");
        try
        {
            var pluginDirectory = Path.Combine(pluginRoot, "TestPlugin");
            var manifestPath = Path.Combine(pluginDirectory, "plugin.json");
            var outsideManifestPath = Path.Combine(pluginRoot, "outside-plugin.json");
            File.Copy(manifestPath, outsideManifestPath, overwrite: true);
            File.Delete(manifestPath);
            File.CreateSymbolicLink(manifestPath, outsideManifestPath);

            var discovery = CreateModuleCatalog().DiscoverModules(pluginRoot);

            Assert.Empty(discovery.Modules);
            var issue = Assert.Single(discovery.Issues);
            Assert.Equal("PLUGIN_MANIFEST_INVALID", issue.Code);
            Assert.Contains("staged", issue.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            ContractTestPathHelper.DeleteDirectory(pluginRoot);
        }
    }

    [Fact]
    public void AssemblyResolver_WhenDependencySymlinkEscapesStagedDirectory_ShouldRejectPlugin()
    {
        var pluginRoot = CreatePluginRoot("TestPlugin");
        try
        {
            var pluginDirectory = Path.Combine(pluginRoot, "TestPlugin");
            var entryAssemblyPath = Path.Combine(pluginDirectory, "IIoT.Edge.TestPlugin.dll");
            var outsideDependencyPath = Path.Combine(pluginRoot, "outside-dependency.dll");
            File.Copy(entryAssemblyPath, outsideDependencyPath, overwrite: true);
            File.CreateSymbolicLink(
                Path.Combine(pluginDirectory, "EscapedDependency.dll"),
                outsideDependencyPath);
            using var resolver = new ModulePluginAssemblyResolver();

            var exception = Assert.Throws<ModulePluginLoadException>(() =>
                resolver.LoadAssembly(entryAssemblyPath, pluginDirectory));

            Assert.Contains("依赖程序集", exception.Message, StringComparison.Ordinal);
            Assert.Contains("staged", exception.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            ContractTestPathHelper.DeleteDirectory(pluginRoot);
        }
    }

    [Fact]
    public void HostRuntimeVersion_ShouldBeParseableAndDerivedFromHostAssemblyVersion()
    {
        var assemblyVersion = typeof(ModulePluginHostRuntime).Assembly.GetName().Version;

        Assert.True(ModulePluginHostRuntime.TryParseVersion(ModulePluginHostRuntime.HostVersion, out var hostVersion));
        Assert.NotNull(assemblyVersion);
        Assert.Equal(assemblyVersion!.Major, hostVersion.Major);
        Assert.Equal(assemblyVersion.Minor, hostVersion.Minor);
        Assert.Equal(Math.Max(assemblyVersion.Build, 0), hostVersion.Build);
        Assert.Equal("2.0.0", ModulePluginHostRuntime.HostApiVersion);
    }

    [Fact]
    public void DiscoverModules_WhenManifestMissesHostApiVersion_ShouldReportManifestInvalid()
    {
        var pluginRoot = CreatePluginRoot("TestPlugin");
        try
        {
            var manifestPath = Path.Combine(pluginRoot, "TestPlugin", "plugin.json");
            var manifest = JsonNode.Parse(File.ReadAllText(manifestPath))!.AsObject();
            manifest.Remove("hostApiVersion");
            File.WriteAllText(manifestPath, manifest.ToJsonString(new() { WriteIndented = true }));

            var discovery = CreateModuleCatalog().DiscoverModules(pluginRoot);

            Assert.Empty(discovery.Modules);
            var issue = Assert.Single(discovery.Issues);
            Assert.Equal("PLUGIN_MANIFEST_INVALID", issue.Code);
        }
        finally
        {
            ContractTestPathHelper.DeleteDirectory(pluginRoot);
        }
    }

    [Fact]
    public void CreateEnabledModules_WhenHostApiVersionDoesNotMatch_ShouldReportCompatibilityIssue()
    {
        var pluginRoot = CreatePluginRoot("TestPlugin");
        try
        {
            var manifestPath = Path.Combine(pluginRoot, "TestPlugin", "plugin.json");
            var manifest = JsonNode.Parse(File.ReadAllText(manifestPath))!.AsObject();
            manifest["hostApiVersion"] = "99.0.0";
            File.WriteAllText(manifestPath, manifest.ToJsonString(new() { WriteIndented = true }));

            var catalog = CreateModuleCatalog();
            var discovery = catalog.DiscoverModules(pluginRoot);
            var activation = catalog.CreateEnabledModules(
                new ConfigurationBuilder()
                    .AddInMemoryCollection(new Dictionary<string, string?>
                    {
                        ["Modules:Enabled:0"] = "TestPlugin"
                    })
                    .Build(),
                "Modules",
                discovery.Modules);

            Assert.Contains(activation.Issues, issue => string.Equals(issue.Code, "PLUGIN_HOST_VERSION_INCOMPATIBLE", StringComparison.OrdinalIgnoreCase));
            Assert.Empty(activation.Modules);
        }
        finally
        {
            ContractTestPathHelper.DeleteDirectory(pluginRoot);
        }
    }

    [Fact]
    public void CreateEnabledModules_WhenHostVersionIsOutsideSupportedRange_ShouldReportCompatibilityIssue()
    {
        var pluginRoot = CreatePluginRoot("TestPlugin");
        try
        {
            var manifestPath = Path.Combine(pluginRoot, "TestPlugin", "plugin.json");
            var manifest = JsonNode.Parse(File.ReadAllText(manifestPath))!.AsObject();
            manifest["maxHostVersion"] = "0.9.0";
            File.WriteAllText(manifestPath, manifest.ToJsonString(new() { WriteIndented = true }));

            var catalog = CreateModuleCatalog();
            var discovery = catalog.DiscoverModules(pluginRoot);
            var activation = catalog.CreateEnabledModules(
                new ConfigurationBuilder()
                    .AddInMemoryCollection(new Dictionary<string, string?>
                    {
                        ["Modules:Enabled:0"] = "TestPlugin"
                    })
                    .Build(),
                "Modules",
                discovery.Modules);

            Assert.Contains(activation.Issues, issue => string.Equals(issue.Code, "PLUGIN_HOST_VERSION_INCOMPATIBLE", StringComparison.OrdinalIgnoreCase));
            Assert.Empty(activation.Modules);
        }
        finally
        {
            ContractTestPathHelper.DeleteDirectory(pluginRoot);
        }
    }

    [Fact]
    public void CreateEnabledModules_WhenDependencyIsNotEnabled_ShouldReportDependencyIssue()
    {
        var pluginRoot = CreatePluginRoot("TestPlugin");
        try
        {
            var manifestPath = Path.Combine(pluginRoot, "TestPlugin", "plugin.json");
            var manifest = JsonNode.Parse(File.ReadAllText(manifestPath))!.AsObject();
            manifest["dependencies"] = new JsonArray("MissingProcess");
            File.WriteAllText(manifestPath, manifest.ToJsonString(new() { WriteIndented = true }));

            var catalog = CreateModuleCatalog();
            var discovery = catalog.DiscoverModules(pluginRoot);
            var activation = catalog.CreateEnabledModules(
                new ConfigurationBuilder()
                    .AddInMemoryCollection(new Dictionary<string, string?>
                    {
                        ["Modules:Enabled:0"] = "TestPlugin"
                    })
                    .Build(),
                "Modules",
                discovery.Modules);

            Assert.Contains(activation.Issues, issue => string.Equals(issue.Code, "PLUGIN_DEPENDENCY_MISSING", StringComparison.OrdinalIgnoreCase));
            Assert.Empty(activation.Modules);
        }
        finally
        {
            ContractTestPathHelper.DeleteDirectory(pluginRoot);
        }
    }

    [Theory]
    [InlineData("moduleId", "ManifestAlias", "ModuleId")]
    [InlineData("supportedProcessType", "ManifestProcessAlias", "ProcessType")]
    public void CreateEnabledModules_WhenManifestIdentityDiffersFromRuntime_ShouldRejectActivation(
        string manifestProperty,
        string manifestValue,
        string runtimeProperty)
    {
        var pluginRoot = CreatePluginRoot("TestPlugin");
        try
        {
            var manifestPath = Path.Combine(pluginRoot, "TestPlugin", "plugin.json");
            var manifest = JsonNode.Parse(File.ReadAllText(manifestPath))!.AsObject();
            manifest[manifestProperty] = manifestValue;
            File.WriteAllText(manifestPath, manifest.ToJsonString(new() { WriteIndented = true }));
            var catalog = CreateModuleCatalog();
            var discovery = catalog.DiscoverModules(pluginRoot);
            Assert.Empty(discovery.Issues);
            var descriptor = Assert.Single(discovery.Modules);
            var activation = catalog.CreateEnabledModules(
                new ConfigurationBuilder()
                    .AddInMemoryCollection(new Dictionary<string, string?>
                    {
                        ["Modules:Enabled:0"] = descriptor.ModuleId
                    })
                    .Build(),
                "Modules",
                discovery.Modules);

            Assert.Empty(activation.Modules);
            var issue = Assert.Single(activation.Issues, issue => issue.Code == "PLUGIN_LOAD_FAILED");
            Assert.Contains(runtimeProperty, issue.Message, StringComparison.Ordinal);
            Assert.Contains("不一致", issue.Message, StringComparison.Ordinal);
        }
        finally
        {
            ContractTestPathHelper.DeleteDirectory(pluginRoot);
        }
    }

    [Fact]
    public void CreateEnabledModules_WhenOnePluginHasApprovedLoadFailure_ShouldContinueIndependentPlugin()
    {
        var loader = new DelegatingModulePluginLoader(descriptor =>
            descriptor.ModuleId == "BrokenPlugin"
                ? throw new ModulePluginLoadException("approved plugin load failure")
                : new StubProcessModule(descriptor.ModuleId, descriptor.ProcessType));
        var catalog = new DirectoryModuleCatalog(loader, new ModulePluginCompatibilityPolicy());

        var activation = catalog.CreateEnabledModules(
            CreateEnabledConfiguration("BrokenPlugin", "HealthyPlugin"),
            "Modules",
            [CreateDescriptor("BrokenPlugin"), CreateDescriptor("HealthyPlugin")]);

        Assert.Equal(["HealthyPlugin"], activation.Modules.Select(module => module.ModuleId).ToArray());
        var issue = Assert.Single(activation.Issues);
        Assert.Equal("PLUGIN_LOAD_FAILED", issue.Code);
        Assert.Equal("BrokenPlugin", issue.ModuleId);
        Assert.Equal(1, loader.GetCallCount("BrokenPlugin"));
        Assert.Equal(1, loader.GetCallCount("HealthyPlugin"));
    }

    [Fact]
    public void CreateEnabledModules_WhenLoaderThrowsUnknownException_ShouldPropagateSameInstanceExactlyOnce()
    {
        AssertActivationExceptionPropagates(new InvalidOperationException("unexpected loader failure"));
    }

    [Fact]
    public void CreateEnabledModules_WhenLoaderThrowsOperationCanceledException_ShouldPropagateSameInstanceExactlyOnce()
    {
        AssertActivationExceptionPropagates(new OperationCanceledException("loader canceled"));
    }

    private static void AssertActivationExceptionPropagates(Exception expected)
    {
        var loader = new DelegatingModulePluginLoader(_ => throw expected);
        var catalog = new DirectoryModuleCatalog(loader, new ModulePluginCompatibilityPolicy());

        var actual = Assert.Throws(expected.GetType(), () =>
            catalog.CreateEnabledModules(
                CreateEnabledConfiguration("TestPlugin"),
                "Modules",
                [CreateDescriptor("TestPlugin")]));

        Assert.Same(expected, actual);
        Assert.Equal(1, loader.GetCallCount("TestPlugin"));
    }

    [Fact]
    public void ModulePluginLoader_WhenResolverThrowsUnknownException_ShouldPropagateSameInstanceExactlyOnce()
    {
        AssertResolverExceptionPropagates(new InvalidOperationException("unexpected resolver failure"));
    }

    [Fact]
    public void ModulePluginLoader_WhenResolverThrowsOperationCanceledException_ShouldPropagateSameInstanceExactlyOnce()
    {
        AssertResolverExceptionPropagates(new OperationCanceledException("resolver canceled"));
    }

    private static void AssertResolverExceptionPropagates(Exception expected)
    {
        var resolver = new ThrowingAssemblyResolver(expected);
        var loader = new ModulePluginLoader(resolver);

        var actual = Assert.Throws(expected.GetType(), () =>
            loader.CreateModule(CreateDescriptor("TestPlugin")));

        Assert.Same(expected, actual);
        Assert.Equal(1, resolver.CallCount);
    }

    [Fact]
    public void ModulePluginLoader_WhenEntryAssemblyIsInvalid_ShouldWrapApprovedAssemblyFailureAndPreserveInnerException()
    {
        var pluginDirectory = ContractTestPathHelper.CreateTempDirectory("edge-plugin-invalid-assembly-tests");
        try
        {
            var assemblyPath = Path.Combine(pluginDirectory, "InvalidPlugin.dll");
            File.WriteAllText(assemblyPath, "not a managed assembly");
            var descriptor = CreateDescriptor("InvalidPlugin") with
            {
                PluginDirectory = pluginDirectory,
                ManifestPath = Path.Combine(pluginDirectory, "plugin.json"),
                EntryAssemblyPath = assemblyPath
            };
            var loader = new ModulePluginLoader(new ModulePluginAssemblyResolver());

            var exception = Assert.Throws<ModulePluginLoadException>(() => loader.CreateModule(descriptor));

            Assert.IsType<BadImageFormatException>(exception.InnerException);
        }
        finally
        {
            ContractTestPathHelper.DeleteDirectory(pluginDirectory);
        }
    }

    [Theory]
    [InlineData(typeof(BadImageFormatException))]
    [InlineData(typeof(FileLoadException))]
    [InlineData(typeof(FileNotFoundException))]
    public void AssemblyResolver_WhenSharedLoaderThrowsApprovedAssemblyException_ShouldReturnNullExactlyOnce(
        Type exceptionType)
    {
        var expected = (Exception)Activator.CreateInstance(exceptionType, "recoverable shared load failure")!;
        var callCount = 0;
        using var resolver = new ModulePluginAssemblyResolver(
            _ => null,
            _ =>
            {
                callCount++;
                throw expected;
            });

        var assembly = resolver.ResolveSharedAssembly(new AssemblyName("Avalonia.Remote.Protocol"));

        Assert.Null(assembly);
        Assert.Equal(1, callCount);
    }

    [Fact]
    public void AssemblyResolver_WhenSharedLoaderThrowsUnknownException_ShouldPropagateSameInstanceExactlyOnce()
    {
        AssertSharedAssemblyLoaderExceptionPropagates(new InvalidOperationException("unexpected shared load failure"));
    }

    [Fact]
    public void AssemblyResolver_WhenSharedLoaderThrowsOperationCanceledException_ShouldPropagateSameInstanceExactlyOnce()
    {
        AssertSharedAssemblyLoaderExceptionPropagates(new OperationCanceledException("shared load canceled"));
    }

    private static void AssertSharedAssemblyLoaderExceptionPropagates(Exception expected)
    {
        var callCount = 0;
        using var resolver = new ModulePluginAssemblyResolver(
            _ => null,
            _ =>
            {
                callCount++;
                throw expected;
            });

        var actual = Assert.Throws(expected.GetType(), () =>
            resolver.ResolveSharedAssembly(new AssemblyName("Avalonia.Remote.Protocol")));

        Assert.Same(expected, actual);
        Assert.Equal(1, callCount);
    }

    private static IConfiguration CreateEnabledConfiguration(params string[] moduleIds)
    {
        var values = moduleIds
            .Select((moduleId, index) => new KeyValuePair<string, string?>($"Modules:Enabled:{index}", moduleId));
        return new ConfigurationBuilder()
            .AddInMemoryCollection(values)
            .Build();
    }

    private static ModulePluginDescriptor CreateDescriptor(string moduleId)
        => new(
            moduleId,
            moduleId + "Process",
            moduleId,
            "1.0.0",
            ModulePluginHostRuntime.HostApiVersion,
            "1.0.0",
            "99.0.0",
            [],
            moduleId,
            moduleId + ".DependencyInjection",
            "/plugins/" + moduleId,
            "/plugins/" + moduleId + "/plugin.json",
            "/plugins/" + moduleId + "/" + moduleId + ".dll");

    private static string CreatePluginRoot(params string[] moduleIds)
    {
        return ContractTestPathHelper.CreatePluginRuntimeRoot(moduleIds);
    }

    private static IModuleCatalog CreateModuleCatalog()
        => new DirectoryModuleCatalog(
            new ModulePluginLoader(new ModulePluginAssemblyResolver()),
            new ModulePluginCompatibilityPolicy());

    private sealed class DelegatingModulePluginLoader(
        Func<ModulePluginDescriptor, IEdgeProcessModule> factory) : IModulePluginLoader
    {
        private readonly Dictionary<string, int> _callCounts = new(StringComparer.OrdinalIgnoreCase);

        public IEdgeProcessModule CreateModule(ModulePluginDescriptor descriptor)
        {
            _callCounts[descriptor.ModuleId] = GetCallCount(descriptor.ModuleId) + 1;
            return factory(descriptor);
        }

        public int GetCallCount(string moduleId) => _callCounts.GetValueOrDefault(moduleId);
    }

    private sealed class ThrowingAssemblyResolver(Exception exception) : IModulePluginAssemblyResolver
    {
        public int CallCount { get; private set; }

        public Assembly LoadAssembly(string assemblyPath, string pluginDirectory)
        {
            CallCount++;
            throw exception;
        }
    }

    private sealed class StubProcessModule(string moduleId, string processType) : IEdgeProcessModule
    {
        public string ModuleId { get; } = moduleId;

        public string ProcessType { get; } = processType;

        public string DisplayName => ModuleId;

        public void Configure(IEdgeProcessModuleBuilder builder)
        {
        }
    }
}
