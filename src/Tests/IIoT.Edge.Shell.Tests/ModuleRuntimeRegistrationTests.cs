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
using IIoT.Edge.Module.Abstractions;
using IIoT.Edge.Host.Bootstrap;
using IIoT.Edge.Presentation.Navigation;
using IIoT.Edge.Runtime.Stations.Stacking.Tasks;
using IIoT.Edge.SharedKernel.Context;
using IIoT.Edge.SharedKernel.DataPipeline;
using IIoT.Edge.SharedKernel.DataPipeline.Capacity;
using IIoT.Edge.SharedKernel.DataPipeline.CellData;
using IIoT.Edge.SharedKernel.DataPipeline.Recipe;
using IIoT.Edge.SharedKernel.Enums;
using IIoT.Edge.SharedKernel.Modules.Stacking;
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
    public void ConfiguredCatalog_WhenNoModulesSectionExists_ShouldDefaultToInjectionOnly()
    {
        var modules = ShellModuleCatalog.CreateEnabledModules(CreateConfiguration());

        Assert.Single(modules);
        Assert.Equal("Injection", modules[0].ModuleId);
    }

    [Fact]
    public void DiscoverCompiledModules_ShouldFindInjectionStackingAndDryRun()
    {
        var modules = ShellModuleCatalog.DiscoverCompiledModules();

        Assert.Equal(
            ["DryRun", "Injection", "Stacking"],
            modules.Select(x => x.ModuleId).OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToArray());
    }

    [Fact]
    public void ConfiguredCatalog_WhenInjectionAndStackingAreEnabled_ShouldLoadBothModules()
    {
        var modules = ShellModuleCatalog.CreateEnabledModules(CreateConfiguration(["Injection", "Stacking"]));

        Assert.Equal(2, modules.Count);
        Assert.Equal(["Injection", "Stacking"], modules.Select(module => module.ModuleId).ToArray());
    }

    [Fact]
    public void ConfiguredCatalog_WhenUnknownModuleIsConfigured_ShouldThrow()
    {
        var ex = Assert.Throws<InvalidOperationException>(() =>
            ShellModuleCatalog.CreateEnabledModules(CreateConfiguration(["Injection", "UnknownModule"])));

        Assert.Contains("Unknown module configured", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ValidationCatalog_ShouldRegisterInjectionStackingAndDryRunWithoutConflicts()
    {
        var modules = ShellModuleCatalog.CreateAllModulesForValidation();
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
        var discoveredModules = ShellModuleCatalog.DiscoverCompiledModules();
        var enabledModules = ShellModuleCatalog.CreateEnabledModules(configuration, discoveredModules);

        try
        {
            services.AddEdgeHostBootstrap(viewRegistry, configuration, dbDir, discoveredModules, enabledModules);

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
        Assert.Contains("缺少 Stacking.Ack", result.Message, StringComparison.Ordinal);
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
        Assert.Contains("Stacking.Ack 方向错误", result.Message, StringComparison.Ordinal);
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
        Assert.Contains("Stacking.ResultCode 地址数量非法", result.Message, StringComparison.Ordinal);
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
            SpyBackgroundServiceCoordinator backgroundCoordinator)
        {
            _serviceProvider = serviceProvider;
            _tempDirectory = tempDirectory;
            Manager = manager;
            PlcManager = plcManager;
            ContextStore = contextStore;
            BackgroundCoordinator = backgroundCoordinator;
        }

        public AppLifecycleManager Manager { get; }

        public SpyPlcConnectionManager PlcManager { get; }

        public SpyProductionContextStore ContextStore { get; }

        public SpyBackgroundServiceCoordinator BackgroundCoordinator { get; }

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
            var discoveredModules = ShellModuleCatalog.DiscoverCompiledModules();
            var modules = ShellModuleCatalog.CreateEnabledModules(configuration, discoveredModules);
            foreach (var module in modules)
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

            foreach (var module in modules)
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
                discoveredModules,
                modules,
                serviceProvider.GetServices<IModuleHardwareProfileProvider>());

            return new AppLifecycleHarness(
                serviceProvider,
                tempDirectory,
                manager,
                plcManager,
                contextStore,
                backgroundCoordinator);
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

        public void RegisterTasks(string deviceName, Func<IPlcBuffer, ProductionContext, List<IPlcTask>> factory)
        {
            RegisteredFactories[deviceName] = factory;
        }

        public IPlcService? GetPlc(int networkDeviceId) => null;

        public ProductionContext? GetContext(string deviceName) => null;

        public void Dispose()
        {
        }
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
