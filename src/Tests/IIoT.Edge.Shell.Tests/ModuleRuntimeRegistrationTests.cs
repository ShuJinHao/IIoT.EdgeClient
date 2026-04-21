using IIoT.Edge.Application.Abstractions.Context;
using IIoT.Edge.Application.Abstractions.DataPipeline;
using IIoT.Edge.Application.Abstractions.Logging;
using IIoT.Edge.Application.Abstractions.Modules;
using IIoT.Edge.Application.Abstractions.Plc;
using IIoT.Edge.Application.Abstractions.Plc.Store;
using IIoT.Edge.Application.Abstractions.Recipe;
using IIoT.Edge.Application.Abstractions.Tasks;
using IIoT.Edge.Application.Common.Models;
using IIoT.Edge.Domain.Hardware.Aggregates;
using IIoT.Edge.Infrastructure.DeviceComm.Plc.Store;
using IIoT.Edge.Infrastructure.Persistence.Dapper;
using IIoT.Edge.Infrastructure.Persistence.EfCore;
using IIoT.Edge.Host.Bootstrap;
using IIoT.Edge.Module.Abstractions;
using IIoT.Edge.Module.Stacking.Constants;
using IIoT.Edge.Module.Stacking.Payload;
using IIoT.Edge.Module.Stacking.Runtime;
using IIoT.Edge.Presentation.Navigation;
using IIoT.Edge.SharedKernel.Context;
using IIoT.Edge.SharedKernel.DataPipeline;
using IIoT.Edge.SharedKernel.DataPipeline.Capacity;
using IIoT.Edge.SharedKernel.DataPipeline.Recipe;
using IIoT.Edge.SharedKernel.Enums;
using IIoT.Edge.SharedKernel.Repository;
using IIoT.Edge.Shell.Core;
using IIoT.Edge.Shell.Modules;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace IIoT.Edge.Shell.Tests;

public sealed class ModuleRuntimeRegistrationTests
{
    [Fact]
    public void ConfiguredCatalog_WhenNoModulesSectionExists_ShouldEnableAllDiscoveredModules()
    {
        var pluginRoot = CreatePluginRuntimeRoot();
        try
        {
            var discovery = DiscoverTestPlugins(pluginRoot);
            var activation = ShellModuleCatalog.CreateEnabledModules(CreateConfiguration(), discovery.Modules);

            Assert.Empty(discovery.Issues);
            Assert.Empty(activation.Issues);
            Assert.Equal(
                ["DryRun", "Injection", "Stacking"],
                activation.Modules.Select(x => x.ModuleId).OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToArray());
        }
        finally
        {
            DeleteDirectory(pluginRoot);
        }
    }

    [Fact]
    public void DiscoverDirectoryPlugins_ShouldFindInjectionStackingAndDryRun()
    {
        var pluginRoot = CreatePluginRuntimeRoot();
        try
        {
            var discovery = DiscoverTestPlugins(pluginRoot);

            Assert.Equal(
                ["DryRun", "Injection", "Stacking"],
                discovery.Modules.Select(x => x.ModuleId).OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToArray());
        }
        finally
        {
            DeleteDirectory(pluginRoot);
        }
    }

    [Fact]
    public void ConfiguredCatalog_WhenInjectionAndStackingAreEnabled_ShouldLoadBothModules()
    {
        var pluginRoot = CreatePluginRuntimeRoot();
        try
        {
            var discovery = DiscoverTestPlugins(pluginRoot);
            var activation = ShellModuleCatalog.CreateEnabledModules(
                CreateConfiguration(["Injection", "Stacking"]),
                discovery.Modules);

            Assert.Empty(activation.Issues);
            Assert.Equal(2, activation.Modules.Count);
            Assert.Equal(["Injection", "Stacking"], activation.Modules.Select(module => module.ModuleId).ToArray());
        }
        finally
        {
            DeleteDirectory(pluginRoot);
        }
    }

    [Fact]
    public void ConfiguredCatalog_WhenUnknownModuleIsConfigured_ShouldReportActivationIssue()
    {
        var pluginRoot = CreatePluginRuntimeRoot();
        try
        {
            var discovery = DiscoverTestPlugins(pluginRoot);
            var activation = ShellModuleCatalog.CreateEnabledModules(
                CreateConfiguration(["Injection", "UnknownModule"]),
                discovery.Modules);

            Assert.Single(activation.Modules);
            Assert.Equal("Injection", activation.Modules[0].ModuleId);
            var issue = Assert.Single(activation.Issues);
            Assert.Equal("PLUGIN_ENABLED_NOT_FOUND", issue.Code);
            Assert.Contains("Unknown module configured", issue.Message, StringComparison.Ordinal);
        }
        finally
        {
            DeleteDirectory(pluginRoot);
        }
    }

    [Fact]
    public async Task AppLifecycleManager_WhenOnlyInjectionIsEnabled_ShouldReportPluginLifecycleStates()
    {
        await using var harness = await AppLifecycleHarness.CreateAsync(
            enabledModules: ["Injection"],
            deviceModuleIds: ["Injection"]);

        var result = await harness.Manager.StartAsync();

        Assert.True(result.Success, result.Message);

        var report = harness.StartupDiagnosticsStore.Current;
        Assert.Equal(["Injection"], report.EnabledModules);
        Assert.Equal(["Injection"], report.ActivatedModules);

        var injectionState = Assert.Single(
            report.PluginStates,
            x => string.Equals(x.ModuleId, "Injection", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(PluginLifecycleState.Activated, injectionState.State);

        var stackingState = Assert.Single(
            report.PluginStates,
            x => string.Equals(x.ModuleId, "Stacking", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(PluginLifecycleState.DisabledByConfig, stackingState.State);
    }

    [Fact]
    public void ValidationCatalog_ShouldRegisterInjectionStackingAndDryRunWithoutConflicts()
    {
        var pluginRoot = CreatePluginRuntimeRoot();
        try
        {
            var modules = ShellModuleCatalog.CreateAllModulesForValidation(DiscoverTestPlugins(pluginRoot).Modules);
            var viewRegistry = new ViewRegistry();
            var cellDataRegistry = new CellDataRegistry();
            var runtimeRegistry = new StationRuntimeRegistry();
            var integrationRegistry = new ProcessIntegrationRegistry();

            foreach (var module in modules)
            {
                module.RegisterCellData(cellDataRegistry);
                module.RegisterRuntime(runtimeRegistry);
                module.RegisterIntegrations(integrationRegistry);
                module.RegisterViews(new ModuleViewRegistry(viewRegistry, module.ModuleId));
            }

            Assert.Equal(3, modules.Count);
            Assert.Equal(3, cellDataRegistry.GetRegistrations().Count);
            Assert.Equal(3, runtimeRegistry.GetRegistrations().Count);
            Assert.Equal(3, integrationRegistry.GetCloudUploaders().Count);
            Assert.NotNull(viewRegistry.GetViewRegistration("Injection.DataView"));
            Assert.NotNull(viewRegistry.GetViewRegistration("Stacking.PlaceholderDashboard"));
            Assert.NotNull(viewRegistry.GetViewRegistration("DryRun.Dashboard"));
        }
        finally
        {
            DeleteDirectory(pluginRoot);
        }
    }

    [Fact]
    public void ModuleViewRegistry_ShouldRejectCorePrefixedRoutes()
    {
        var registry = new ModuleViewRegistry(new ViewRegistry(), "Injection");

        var ex = Assert.Throws<InvalidOperationException>(() =>
            registry.RegisterRoute("Core.BadRoute", typeof(object), typeof(object)));

        Assert.Contains("Injection.", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ViewRegistry_ShouldRejectCorePrefixedRoutesOutsideAnchorables()
    {
        var registry = new ViewRegistry();

        var ex = Assert.Throws<InvalidOperationException>(() =>
            registry.RegisterRoute("Core.IllegalRoute", typeof(object), typeof(object)));

        Assert.Contains("Core-prefixed", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void HostBootstrap_ShouldRegisterDiagnosticsCoreView()
    {
        var services = new ServiceCollection();
        var viewRegistry = new ViewRegistry();
        var configuration = CreateConfiguration();
        var dbDir = Path.Combine(Path.GetTempPath(), "edge-host-bootstrap-" + Guid.NewGuid().ToString("N"));
        var pluginRoot = CreatePluginRuntimeRoot();

        try
        {
            var discovery = DiscoverTestPlugins(pluginRoot);
            var activation = ShellModuleCatalog.CreateEnabledModules(configuration, discovery.Modules);
            services.AddEdgeHostBootstrap(
                viewRegistry,
                configuration,
                dbDir,
                discovery.Modules,
                [.. discovery.Issues, .. activation.Issues],
                activation.EnabledModuleIds,
                activation.Modules);

            var diagnosticsRegistration = viewRegistry.GetViewRegistration(CoreViewIds.Diagnostics);
            Assert.NotNull(diagnosticsRegistration);
            Assert.Contains(
                viewRegistry.GetAllMenus(),
                menu => string.Equals(menu.ViewId, CoreViewIds.Diagnostics, StringComparison.Ordinal));
        }
        finally
        {
            if (Directory.Exists(dbDir))
            {
                Directory.Delete(dbDir, recursive: true);
            }

            DeleteDirectory(pluginRoot);
        }
    }

    [Fact]
    public async Task AppLifecycleManager_WhenDefaultModulesAreUsed_ShouldBindInjectionFactoryAndRestoreState()
    {
        await using var harness = await AppLifecycleHarness.CreateAsync(
            enabledModules: [],
            deviceModuleIds: ["Injection"]);

        var result = await harness.Manager.StartAsync();

        Assert.True(result.Success, result.Message);
        Assert.Single(harness.PlcManager.RegisteredFactories);
        Assert.Equal(1, harness.ContextStore.LoadCallCount);
        Assert.Equal(1, harness.BackgroundCoordinator.StartCallCount);
        Assert.True(harness.PlcManager.RegisteredFactories.TryGetValue("PLC-A", out var factory));

        var tasks = factory!(
            new PlcBuffer(8, 8),
            new ProductionContext { DeviceName = "PLC-A" });

        Assert.Empty(tasks);
    }

    [Fact]
    public async Task AppLifecycleManager_WhenInjectionAndStackingAreEnabled_ShouldBindBothFactories()
    {
        await using var harness = await AppLifecycleHarness.CreateAsync(
            enabledModules: ["Injection", "Stacking"],
            deviceModuleIds: ["Injection", "Stacking"]);

        var result = await harness.Manager.StartAsync();

        Assert.True(result.Success, result.Message);
        Assert.Equal(2, harness.PlcManager.RegisteredFactories.Count);
        Assert.Equal(1, harness.ContextStore.LoadCallCount);
        Assert.Equal(1, harness.BackgroundCoordinator.StartCallCount);
        Assert.True(harness.PlcManager.RegisteredFactories.ContainsKey("PLC-A"));
        Assert.True(harness.PlcManager.RegisteredFactories.ContainsKey("PLC-B"));

        var injectionTasks = harness.PlcManager.RegisteredFactories["PLC-A"](
            new PlcBuffer(8, 8),
            new ProductionContext { DeviceName = "PLC-A" });
        var stackingTasks = harness.PlcManager.RegisteredFactories["PLC-B"](
            new PlcBuffer(8, 8),
            new ProductionContext { DeviceName = "PLC-B" });

        Assert.Empty(injectionTasks);
        Assert.Single(stackingTasks);
        Assert.IsType<StackingSignalCaptureTask>(stackingTasks[0]);
    }

    [Fact]
    public async Task AppLifecycleManager_WhenDeviceUsesDisabledModule_ShouldFailStartupValidation()
    {
        await using var harness = await AppLifecycleHarness.CreateAsync(
            enabledModules: ["Injection"],
            deviceModuleIds: ["Stacking"]);

        var result = await harness.Manager.StartAsync();

        Assert.False(result.Success);
        Assert.Contains("MODULE_NOT_ENABLED", result.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(harness.PlcManager.RegisteredFactories);
        Assert.Equal(0, harness.BackgroundCoordinator.StartCallCount);
    }

    [Fact]
    public async Task AppLifecycleManager_WhenDevelopmentSamplesAreEnabled_ShouldSeedStackingDeviceAndContext()
    {
        await using var harness = await AppLifecycleHarness.CreateAsync(
            enabledModules: ["Injection", "Stacking"],
            deviceModuleIds: [],
            environmentName: "Development",
            developmentSamplesEnabled: true,
            seedStackingModule: true);

        var result = await harness.Manager.StartAsync();

        Assert.True(result.Success, result.Message);

        var devices = await harness.GetNetworkDevicesAsync();
        var stackingDevice = Assert.Single(devices);
        Assert.Equal(StackingModuleConstants.ModuleId, stackingDevice.ModuleId);
        Assert.Equal("PLC-STACKING-DEV", stackingDevice.DeviceName);

        var mappings = await harness.GetIoMappingsAsync(stackingDevice.Id);
        Assert.Equal(4, mappings.Count);
        Assert.Equal(
            ["Stacking.Sequence", "Stacking.LayerCount", "Stacking.ResultCode", "Stacking.Ack"],
            mappings.OrderBy(x => x.SortOrder).Select(x => x.Label).ToArray());
        Assert.Equal(
            ["DB1.DBW0", "DB1.DBW2", "DB1.DBW4", "DB1.DBW6"],
            mappings.OrderBy(x => x.SortOrder).Select(x => x.PlcAddress).ToArray());

        var context = Assert.Single(harness.ContextStore.GetAll());
        var sampleCell = Assert.Single(context.CurrentCells.Values.OfType<StackingCellData>());
        Assert.Equal("ST-DEV-0001", sampleCell.Barcode);
        Assert.Equal("DevelopmentSample", sampleCell.RuntimeStatus);
    }

    [Fact]
    public async Task AppLifecycleManager_WhenDevelopmentSamplesRunTwice_ShouldRemainIdempotent()
    {
        await using var harness = await AppLifecycleHarness.CreateAsync(
            enabledModules: ["Injection", "Stacking"],
            deviceModuleIds: [],
            environmentName: "Development",
            developmentSamplesEnabled: true,
            seedStackingModule: true);

        var firstStart = await harness.Manager.StartAsync();
        var secondStart = await harness.Manager.StartAsync();

        Assert.True(firstStart.Success, firstStart.Message);
        Assert.True(secondStart.Success, secondStart.Message);

        var devices = await harness.GetNetworkDevicesAsync();
        var stackingDevice = Assert.Single(devices);
        var mappings = await harness.GetIoMappingsAsync(stackingDevice.Id);
        Assert.Equal(4, mappings.Count);

        var context = Assert.Single(harness.ContextStore.GetAll());
        Assert.Single(context.CurrentCells.Values.OfType<StackingCellData>());
    }

    [Fact]
    public async Task AppLifecycleManager_WhenDevelopmentSamplesAreDisabled_ShouldNotSeedStackingDevice()
    {
        await using var harness = await AppLifecycleHarness.CreateAsync(
            enabledModules: ["Injection", "Stacking"],
            deviceModuleIds: [],
            environmentName: "Development",
            developmentSamplesEnabled: false,
            seedStackingModule: false);

        var result = await harness.Manager.StartAsync();

        Assert.True(result.Success, result.Message);
        Assert.Empty(await harness.GetNetworkDevicesAsync());
        Assert.Empty(harness.ContextStore.GetAll());
    }

    [Fact]
    public async Task AppLifecycleManager_WhenEnvironmentIsProduction_ShouldNotSeedDevelopmentSamples()
    {
        await using var harness = await AppLifecycleHarness.CreateAsync(
            enabledModules: ["Injection", "Stacking"],
            deviceModuleIds: [],
            environmentName: "Production",
            developmentSamplesEnabled: true,
            seedStackingModule: true);

        var result = await harness.Manager.StartAsync();

        Assert.True(result.Success, result.Message);
        Assert.Empty(await harness.GetNetworkDevicesAsync());
        Assert.Empty(harness.ContextStore.GetAll());
    }

    [Fact]
    public async Task AppLifecycleManager_WhenStackingMappingIsMissing_ShouldFailStartupValidation()
    {
        await using var harness = await AppLifecycleHarness.CreateAsync(
            enabledModules: ["Injection", "Stacking"],
            deviceModuleIds: ["Stacking"]);

        var device = Assert.Single(await harness.GetNetworkDevicesAsync());
        await harness.ReplaceIoMappingsAsync(device.Id,
        [
            new IoMappingEntity(device.Id, "Stacking.Sequence", "DB1.DBW0", 1, "Int16", "Read") { SortOrder = 1 },
            new IoMappingEntity(device.Id, "Stacking.LayerCount", "DB1.DBW2", 1, "Int16", "Read") { SortOrder = 2 },
            new IoMappingEntity(device.Id, "Stacking.ResultCode", "DB1.DBW4", 1, "Int16", "Read") { SortOrder = 3 }
        ]);

        var result = await harness.Manager.StartAsync();

        Assert.False(result.Success);
        Assert.Contains("missing Stacking.Ack", result.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AppLifecycleManager_WhenStackingAckDirectionIsWrong_ShouldFailStartupValidation()
    {
        await using var harness = await AppLifecycleHarness.CreateAsync(
            enabledModules: ["Injection", "Stacking"],
            deviceModuleIds: ["Stacking"]);

        var device = Assert.Single(await harness.GetNetworkDevicesAsync());
        await harness.ReplaceIoMappingsAsync(device.Id,
        [
            new IoMappingEntity(device.Id, "Stacking.Sequence", "DB1.DBW0", 1, "Int16", "Read") { SortOrder = 1 },
            new IoMappingEntity(device.Id, "Stacking.LayerCount", "DB1.DBW2", 1, "Int16", "Read") { SortOrder = 2 },
            new IoMappingEntity(device.Id, "Stacking.ResultCode", "DB1.DBW4", 1, "Int16", "Read") { SortOrder = 3 },
            new IoMappingEntity(device.Id, "Stacking.Ack", "DB1.DBW6", 1, "Int16", "Read") { SortOrder = 4 }
        ]);

        var result = await harness.Manager.StartAsync();

        Assert.False(result.Success);
        Assert.Contains("Stacking.Ack direction mismatch", result.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AppLifecycleManager_WhenStackingAddressCountIsInvalid_ShouldFailStartupValidation()
    {
        await using var harness = await AppLifecycleHarness.CreateAsync(
            enabledModules: ["Injection", "Stacking"],
            deviceModuleIds: ["Stacking"]);

        var device = Assert.Single(await harness.GetNetworkDevicesAsync());
        await harness.ReplaceIoMappingsAsync(device.Id,
        [
            new IoMappingEntity(device.Id, "Stacking.Sequence", "DB1.DBW0", 1, "Int16", "Read") { SortOrder = 1 },
            new IoMappingEntity(device.Id, "Stacking.LayerCount", "DB1.DBW2", 1, "Int16", "Read") { SortOrder = 2 },
            new IoMappingEntity(device.Id, "Stacking.ResultCode", "DB1.DBW4", 2, "Int16", "Read") { SortOrder = 3 },
            new IoMappingEntity(device.Id, "Stacking.Ack", "DB1.DBW6", 1, "Int16", "Write") { SortOrder = 4 }
        ]);

        var result = await harness.Manager.StartAsync();

        Assert.False(result.Success);
        Assert.Contains("Stacking.ResultCode address count mismatch", result.Message, StringComparison.Ordinal);
    }

    private static IConfiguration CreateConfiguration(
        string[]? enabledModules = null,
        string environmentName = "Production",
        bool developmentSamplesEnabled = false,
        bool seedStackingModule = false)
    {
        var settings = new Dictionary<string, string?>
        {
            ["CloudApi:BaseUrl"] = "https://cloud.test",
            ["CloudApi:ClientCode"] = "CLIENT-01",
            ["Shell:Environment"] = environmentName,
            ["DevelopmentSamples:Enabled"] = developmentSamplesEnabled.ToString(),
            ["DevelopmentSamples:SeedStackingModule"] = seedStackingModule.ToString(),
            ["DevelopmentSamples:StackingDeviceName"] = "PLC-STACKING-DEV",
            ["DevelopmentSamples:SampleBarcode"] = "ST-DEV-0001",
            ["DevelopmentSamples:SampleTrayCode"] = "TRAY-STACK-DEV",
            ["DevelopmentSamples:SampleLayerCount"] = "12"
        };

        enabledModules ??= [];
        for (var i = 0; i < enabledModules.Length; i++)
        {
            settings[$"Modules:Enabled:{i}"] = enabledModules[i];
        }

        return new ConfigurationBuilder()
            .AddInMemoryCollection(settings)
            .Build();
    }

    private static ModuleCatalogDiscoveryResult DiscoverTestPlugins(string pluginRoot)
    {
        return ShellModuleCatalog.DiscoverModules(pluginRoot);
    }

    private static string CreatePluginRuntimeRoot(string? targetRoot = null)
    {
        var pluginRoot = targetRoot ?? Path.Combine(Path.GetTempPath(), "edge-shell-plugin-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(pluginRoot);

        var runtimeModulesRoot = ShellModuleCatalog.GetPluginRootPath(AppContext.BaseDirectory);
        foreach (var moduleId in new[] { "DryRun", "Injection", "Stacking" })
        {
            var sourceModuleDirectory = Path.Combine(runtimeModulesRoot, moduleId);
            if (!Directory.Exists(sourceModuleDirectory))
            {
                sourceModuleDirectory = GetModuleRuntimeDirectory(moduleId);
            }

            var targetModuleDirectory = Path.Combine(pluginRoot, moduleId);
            CopyDirectory(sourceModuleDirectory, targetModuleDirectory);

            var sourceManifestPath = Path.Combine(GetModuleSourceDirectory(moduleId), "plugin.json");
            File.Copy(sourceManifestPath, Path.Combine(targetModuleDirectory, "plugin.json"), overwrite: true);
        }

        return pluginRoot;
    }

    private static string GetModuleSourceDirectory(string moduleId)
        => moduleId switch
        {
            "Injection" => Path.Combine(FindRepoRoot(), "src", "Modules", "IIoT.Edge.Module.Injection"),
            "Stacking" => Path.Combine(FindRepoRoot(), "src", "Modules", "IIoT.Edge.Module.Stacking"),
            "DryRun" => Path.Combine(FindRepoRoot(), "src", "Tools", "ModuleSamples", "IIoT.Edge.Module.DryRun"),
            _ => throw new InvalidOperationException($"Unsupported module id '{moduleId}'.")
        };

    private static string GetModuleRuntimeDirectory(string moduleId)
    {
        var runtimeDirectory = Path.Combine(GetModuleSourceDirectory(moduleId), "bin", "Debug", "net10.0-windows");
        if (!Directory.Exists(runtimeDirectory))
        {
            throw new DirectoryNotFoundException($"Module runtime directory was not found: '{runtimeDirectory}'.");
        }

        return runtimeDirectory;
    }

    private static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "IIoT.EdgeClient.slnx")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Could not locate IIoT.EdgeClient repository root.");
    }

    private static void CopyDirectory(string sourceDirectory, string targetDirectory)
    {
        Directory.CreateDirectory(targetDirectory);

        foreach (var directory in Directory.GetDirectories(sourceDirectory, "*", SearchOption.AllDirectories))
        {
            Directory.CreateDirectory(directory.Replace(sourceDirectory, targetDirectory, StringComparison.OrdinalIgnoreCase));
        }

        foreach (var file in Directory.GetFiles(sourceDirectory, "*", SearchOption.AllDirectories))
        {
            var targetFile = file.Replace(sourceDirectory, targetDirectory, StringComparison.OrdinalIgnoreCase);
            Directory.CreateDirectory(Path.GetDirectoryName(targetFile)!);
            File.Copy(file, targetFile, overwrite: true);
        }
    }

    private static void DeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch
        {
        }
    }

    private sealed class AppLifecycleHarness : IAsyncDisposable
    {
        private readonly ServiceProvider _serviceProvider;
        private readonly string _tempDirectory;

        private AppLifecycleHarness(
            ServiceProvider serviceProvider,
            string tempDirectory,
            AppLifecycleManager manager,
            SpyPlcConnectionManager plcManager,
            SpyProductionContextStore contextStore,
            SpyBackgroundServiceCoordinator backgroundCoordinator,
            IStartupDiagnosticsStore startupDiagnosticsStore)
        {
            _serviceProvider = serviceProvider;
            _tempDirectory = tempDirectory;
            Manager = manager;
            PlcManager = plcManager;
            ContextStore = contextStore;
            BackgroundCoordinator = backgroundCoordinator;
            StartupDiagnosticsStore = startupDiagnosticsStore;
        }

        public AppLifecycleManager Manager { get; }

        public SpyPlcConnectionManager PlcManager { get; }

        public SpyProductionContextStore ContextStore { get; }

        public SpyBackgroundServiceCoordinator BackgroundCoordinator { get; }

        public IStartupDiagnosticsStore StartupDiagnosticsStore { get; }

        public async Task<List<NetworkDeviceEntity>> GetNetworkDevicesAsync()
            => await _serviceProvider
                .GetRequiredService<IRepository<NetworkDeviceEntity>>()
                .GetListAsync(x => x.DeviceType == DeviceType.PLC, includes: null, cancellationToken: default)
                .ConfigureAwait(false);

        public async Task<List<IoMappingEntity>> GetIoMappingsAsync(int networkDeviceId)
            => await _serviceProvider
                .GetRequiredService<IRepository<IoMappingEntity>>()
                .GetListAsync(x => x.NetworkDeviceId == networkDeviceId, includes: null, cancellationToken: default)
                .ConfigureAwait(false);

        public async Task ReplaceIoMappingsAsync(int networkDeviceId, IReadOnlyCollection<IoMappingEntity> mappings)
        {
            var repo = _serviceProvider.GetRequiredService<IRepository<IoMappingEntity>>();
            var existing = await repo.GetListAsync(x => x.NetworkDeviceId == networkDeviceId, includes: null, cancellationToken: default)
                .ConfigureAwait(false);

            foreach (var item in existing)
            {
                repo.Delete(item);
            }

            foreach (var mapping in mappings)
            {
                repo.Add(mapping);
            }

            await repo.SaveChangesAsync().ConfigureAwait(false);
        }

        public static async Task<AppLifecycleHarness> CreateAsync(
            string[] enabledModules,
            string[] deviceModuleIds,
            string environmentName = "Production",
            bool developmentSamplesEnabled = false,
            bool seedStackingModule = false)
        {
            var tempDirectory = Path.Combine(Path.GetTempPath(), "edge-shell-tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDirectory);
            var dbPath = Path.Combine(tempDirectory, "edge.db");

            var services = new ServiceCollection();
            services.AddEfCorePersistenceInfrastructure(dbPath);
            services.AddDapperPersistenceInfrastructure(tempDirectory);

            var configuration = CreateConfiguration(
                enabledModules,
                environmentName,
                developmentSamplesEnabled,
                seedStackingModule);
            var pluginRoot = CreatePluginRuntimeRoot(Path.Combine(tempDirectory, "Modules"));

            var shiftConfig = new ShiftConfig
            {
                DayStart = "08:00",
                DayEnd = "20:00"
            };

            var plcManager = new SpyPlcConnectionManager();
            var contextStore = new SpyProductionContextStore();
            var backgroundCoordinator = new SpyBackgroundServiceCoordinator();
            var logger = new SpyLogService();
            var recipeService = new SpyRecipeService();

            services.AddSingleton<IConfiguration>(configuration);
            services.AddSingleton(shiftConfig);
            services.AddSingleton<IPlcConnectionManager>(plcManager);
            services.AddSingleton<IProductionContextStore>(contextStore);
            services.AddSingleton<IBackgroundServiceCoordinator>(backgroundCoordinator);
            services.AddSingleton<ILogService>(logger);
            services.AddSingleton<IRecipeService>(recipeService);
            services.AddSingleton<IDataPipelineService, SpyDataPipelineService>();
            var discovery = DiscoverTestPlugins(pluginRoot);
            var activation = ShellModuleCatalog.CreateEnabledModules(configuration, discovery.Modules);
            foreach (var module in activation.Modules)
            {
                services.AddSingleton<IEdgeStationModule>(module);
                module.RegisterServices(services);
            }

            services.AddSingleton<IDevelopmentSampleInitializer, DevelopmentSampleInitializer>();
            services.AddSingleton<IStartupDiagnosticsStore, StartupDiagnosticsStore>();

            var serviceProvider = services.BuildServiceProvider();
            serviceProvider.ApplyMigrations();

            var cellDataRegistry = new CellDataRegistry();
            var runtimeRegistry = new StationRuntimeRegistry();
            var integrationRegistry = new ProcessIntegrationRegistry();

            foreach (var module in activation.Modules)
            {
                module.RegisterCellData(cellDataRegistry);
                module.RegisterRuntime(runtimeRegistry);
                module.RegisterIntegrations(integrationRegistry);
            }

            await SeedDevicesAsync(serviceProvider, deviceModuleIds).ConfigureAwait(false);

            var manager = new AppLifecycleManager(
                serviceProvider,
                configuration,
                shiftConfig,
                serviceProvider.GetRequiredService<IRepository<NetworkDeviceEntity>>(),
                serviceProvider.GetRequiredService<IRepository<IoMappingEntity>>(),
                contextStore,
                recipeService,
                backgroundCoordinator,
                logger,
                plcManager,
                serviceProvider.GetRequiredService<IDevelopmentSampleInitializer>(),
                cellDataRegistry,
                runtimeRegistry,
                integrationRegistry,
                serviceProvider.GetRequiredService<IStartupDiagnosticsStore>(),
                discovery.Modules,
                [.. discovery.Issues, .. activation.Issues],
                activation.EnabledModuleIds,
                activation.Modules,
                serviceProvider.GetServices<IModuleHardwareProfileProvider>());

            return new AppLifecycleHarness(
                serviceProvider,
                tempDirectory,
                manager,
                plcManager,
                contextStore,
                backgroundCoordinator,
                serviceProvider.GetRequiredService<IStartupDiagnosticsStore>());
        }

        public async ValueTask DisposeAsync()
        {
            await Manager.StopAsync().ConfigureAwait(false);
            await _serviceProvider.DisposeAsync().ConfigureAwait(false);

            try
            {
                if (Directory.Exists(_tempDirectory))
                {
                    Directory.Delete(_tempDirectory, recursive: true);
                }
            }
            catch
            {
            }
        }

        private static async Task SeedDevicesAsync(IServiceProvider serviceProvider, IReadOnlyList<string> moduleIds)
        {
            var networkRepo = serviceProvider.GetRequiredService<IRepository<NetworkDeviceEntity>>();
            var ioRepo = serviceProvider.GetRequiredService<IRepository<IoMappingEntity>>();
            var hardwareProfiles = serviceProvider.GetServices<IModuleHardwareProfileProvider>()
                .ToDictionary(x => x.ModuleId, StringComparer.OrdinalIgnoreCase);

            for (var index = 0; index < moduleIds.Count; index++)
            {
                var deviceName = $"PLC-{(char)('A' + index)}";
                var device = new NetworkDeviceEntity(deviceName, DeviceType.PLC, "127.0.0.1", 102 + index)
                {
                    DeviceModel = PlcType.S7.ToString(),
                    ModuleId = moduleIds[index],
                    ConnectTimeout = 3000,
                    IsEnabled = true
                };

                networkRepo.Add(device);
                await networkRepo.SaveChangesAsync().ConfigureAwait(false);

                if (hardwareProfiles.TryGetValue(moduleIds[index], out var provider))
                {
                    foreach (var mapping in provider.GetDefaultIoTemplate())
                    {
                        ioRepo.Add(new IoMappingEntity(
                            device.Id,
                            mapping.Label,
                            mapping.PlcAddress,
                            mapping.AddressCount,
                            mapping.DataType,
                            mapping.Direction)
                        {
                            SortOrder = mapping.SortOrder
                        });
                    }
                }
                else
                {
                    ioRepo.Add(new IoMappingEntity(device.Id, $"Signal-{index + 1}", $"DB1.DBW{index * 2}", 1, "Int16", "Read")
                    {
                        SortOrder = index + 1
                    });
                }

                await ioRepo.SaveChangesAsync().ConfigureAwait(false);
            }
        }
    }

    private sealed class SpyPlcConnectionManager : IPlcConnectionManager
    {
        public Dictionary<string, Func<IPlcBuffer, ProductionContext, List<IPlcTask>>> RegisteredFactories { get; }
            = new(StringComparer.OrdinalIgnoreCase);

        public Task InitializeAsync(CancellationToken ct = default) => Task.CompletedTask;

        public Task StopAsync(CancellationToken ct = default) => Task.CompletedTask;

        public Task ReloadAsync(string deviceName, CancellationToken ct = default) => Task.CompletedTask;

        public Task StopDeviceAsync(int networkDeviceId, CancellationToken ct = default) => Task.CompletedTask;

        public void RegisterTasks(string deviceName, Func<IPlcBuffer, ProductionContext, List<IPlcTask>> factory)
        {
            RegisteredFactories[deviceName] = factory;
        }

        public IPlcService? GetPlc(int networkDeviceId) => null;

        public ProductionContext? GetContext(string deviceName) => null;

        public void Dispose()
        {
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class SpyDataPipelineService : IDataPipelineService
    {
        private readonly Queue<CellCompletedRecord> _queue = new();

        public int PendingCount => _queue.Count;
        public int OverflowCount => 0;
        public int SpillCount => 0;

        public ValueTask<DataPipelineEnqueueResult> EnqueueAsync(
            CellCompletedRecord record,
            CancellationToken cancellationToken = default)
        {
            _queue.Enqueue(record);
            return ValueTask.FromResult(DataPipelineEnqueueResult.Accepted());
        }

        public bool TryDequeue(out CellCompletedRecord? record)
        {
            if (_queue.Count == 0)
            {
                record = null;
                return false;
            }

            record = _queue.Dequeue();
            return true;
        }

        public ValueTask<bool> WaitToReadAsync(CancellationToken cancellationToken = default)
            => ValueTask.FromResult(_queue.Count > 0);
    }

    private sealed class SpyProductionContextStore : IProductionContextStore
    {
        private readonly Dictionary<string, ProductionContext> _contexts = new(StringComparer.OrdinalIgnoreCase);

        public int LoadCallCount { get; private set; }

        public int SaveCallCount { get; private set; }

        public ProductionContext GetOrCreate(string deviceName)
        {
            if (!_contexts.TryGetValue(deviceName, out var context))
            {
                context = new ProductionContext { DeviceName = deviceName };
                _contexts[deviceName] = context;
            }

            return context;
        }

        public IReadOnlyCollection<ProductionContext> GetAll() => _contexts.Values.ToList().AsReadOnly();

        public ProductionContextPersistenceDiagnostics GetPersistenceDiagnostics() => new(0, null);

        public void LoadFromFile() => LoadCallCount++;

        public void SaveToFile() => SaveCallCount++;

        public Task StartAutoSaveAsync(CancellationToken ct, int intervalSeconds = 30) => Task.CompletedTask;
    }

    private sealed class SpyBackgroundServiceCoordinator : IBackgroundServiceCoordinator
    {
        public int StartCallCount { get; private set; }

        public int StopCallCount { get; private set; }

        public Task StartAsync(CancellationToken cancellationToken = default)
        {
            StartCallCount++;
            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken = default)
        {
            StopCallCount++;
            return Task.CompletedTask;
        }
    }

    private sealed class SpyRecipeService : IRecipeService
    {
        public RecipeSource ActiveSource => RecipeSource.Local;

        public RecipeData? ActiveRecipe => null;

        public RecipeData? CloudRecipe => null;

        public RecipeData? LocalRecipe => null;

#pragma warning disable CS0067
        public event Action? RecipeChanged;
#pragma warning restore CS0067

        public void SwitchSource(RecipeSource source)
        {
        }

        public RecipeParam? GetParam(string name) => null;

        public IReadOnlyDictionary<string, RecipeParam> GetAllParams()
            => new Dictionary<string, RecipeParam>();

        public Task<bool> PullFromCloudAsync() => Task.FromResult(false);

        public void SetLocalParam(string name, double? min, double? max, string unit)
        {
        }

        public void RemoveLocalParam(string name)
        {
        }

        public void LoadFromFile()
        {
        }

        public void SaveToFile()
        {
        }
    }

    private sealed class SpyLogService : ILogService
    {
        public List<LogEntry> Entries { get; } = [];

        public event Action<LogEntry>? EntryAdded;

        public void Debug(string message) => Write("Debug", message);

        public void Info(string message) => Write("Info", message);

        public void Warn(string message) => Write("Warn", message);

        public void Error(string message) => Write("Error", message);

        public void Fatal(string message) => Write("Fatal", message);

        private void Write(string level, string message)
        {
            var entry = new LogEntry
            {
                Time = DateTime.UtcNow,
                Level = level,
                Message = message
            };

            Entries.Add(entry);
            EntryAdded?.Invoke(entry);
        }
    }
}
