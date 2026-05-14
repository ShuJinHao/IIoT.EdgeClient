using IIoT.Edge.Application.Modules.Diagnostics;
using IIoT.Edge.Application.Context;
using IIoT.Edge.Application.Abstractions.Context;
using IIoT.Edge.Application.Abstractions.Config;
using IIoT.Edge.Application.Abstractions.DataPipeline;
using IIoT.Edge.Application.Abstractions.Logging;
using IIoT.Edge.Application.Abstractions.Modules;
using IIoT.Edge.Application.Abstractions.Plc;
using IIoT.Edge.Application.Abstractions.Plc.Store;
using IIoT.Edge.Application.Abstractions.Recipe;
using IIoT.Edge.Application.Abstractions.Tasks;
using IIoT.Edge.Application.Common.Models;
using IIoT.Edge.Application.Features.Config.ModuleParameters;
using IIoT.Edge.Application.Features.Hardware.PlcTaskBindings;
using IIoT.Edge.Domain.Hardware.Aggregates;
using IIoT.Edge.Infrastructure.DeviceComm.Plc.Store;
using IIoT.Edge.Infrastructure.Persistence.Dapper;
using IIoT.Edge.Infrastructure.Persistence.EfCore;
using IIoT.Edge.Host.Bootstrap;
using IIoT.Edge.Host.Bootstrap.Modules;
using IIoT.Edge.Module.Homogenization;
using IIoT.Edge.Presentation.Navigation;
using IIoT.Edge.SharedKernel.Context;
using IIoT.Edge.SharedKernel.DataPipeline;
using IIoT.Edge.SharedKernel.DataPipeline.Capacity;
using IIoT.Edge.SharedKernel.DataPipeline.CellData;
using IIoT.Edge.SharedKernel.DataPipeline.Recipe;
using IIoT.Edge.SharedKernel.Enums;
using IIoT.Edge.SharedKernel.Repository;
using IIoT.Edge.Runtime.Signals;
using IIoT.Edge.Shell.Core;
using IIoT.Edge.Shell.Modules;
using IIoT.Edge.UI.Shared.PluginSystem;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using System.Reflection;
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
            var activation = CreateShellModuleCatalog().CreateEnabledModules(CreateConfiguration(), discovery.Modules);

            Assert.Empty(discovery.Issues);
            Assert.Empty(activation.Issues);
            Assert.Equal(
                ["Homogenization"],
                activation.Modules.Select(x => x.ModuleId).OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToArray());
        }
        finally
        {
            DeleteDirectory(pluginRoot);
        }
    }

    [Fact]
    public void DiscoverDirectoryPlugins_ShouldFindProductModules()
    {
        var pluginRoot = CreatePluginRuntimeRoot();
        try
        {
            var discovery = DiscoverTestPlugins(pluginRoot);

            Assert.Equal(
                ["Homogenization"],
                discovery.Modules.Select(x => x.ModuleId).OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToArray());
        }
        finally
        {
            DeleteDirectory(pluginRoot);
        }
    }

    [Fact]
    public void ConfiguredCatalog_WhenHomogenizationIsEnabled_ShouldLoadModule()
    {
        var pluginRoot = CreatePluginRuntimeRoot();
        try
        {
            var discovery = DiscoverTestPlugins(pluginRoot);
            var activation = CreateShellModuleCatalog().CreateEnabledModules(
                CreateConfiguration(["Homogenization"]),
                discovery.Modules);

            Assert.Empty(activation.Issues);
            Assert.Single(activation.Modules);
            Assert.Equal(["Homogenization"], activation.Modules.Select(module => module.ModuleId).ToArray());
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
            var activation = CreateShellModuleCatalog().CreateEnabledModules(
                CreateConfiguration(["Homogenization", "UnknownModule"]),
                discovery.Modules);

            Assert.Single(activation.Modules);
            Assert.Equal("Homogenization", activation.Modules[0].ModuleId);
            var issue = Assert.Single(activation.Issues);
            Assert.Equal("PLUGIN_ENABLED_NOT_FOUND", issue.Code);
            Assert.Contains("未知模块", issue.Message, StringComparison.Ordinal);
        }
        finally
        {
            DeleteDirectory(pluginRoot);
        }
    }

    [Fact]
    public async Task AppLifecycleManager_WhenOnlyHomogenizationIsEnabled_ShouldReportPluginLifecycleStates()
    {
        await using var harness = await AppLifecycleHarness.CreateAsync(
            enabledModules: ["Homogenization"],
            deviceModuleIds: ["Homogenization"]);

        var result = await harness.Manager.StartAsync();

        Assert.True(result.Success, result.Message);

        var report = harness.StartupDiagnosticsStore.Current;
        Assert.Equal(["Homogenization"], report.EnabledModules);
        Assert.Equal(["Homogenization"], report.ActivatedModules);

        var homogenizationState = Assert.Single(
            report.PluginStates,
            x => string.Equals(x.ModuleId, "Homogenization", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(PluginLifecycleState.Activated, homogenizationState.State);
    }

    [Fact]
    public async Task AppLifecycleManager_WhenBootstrapSecretIsMissing_ShouldFailStartupValidation()
    {
        await using var harness = await AppLifecycleHarness.CreateAsync(
            enabledModules: ["Homogenization"],
            deviceModuleIds: ["Homogenization"],
            bootstrapSecret: null);

        var result = await harness.Manager.StartAsync();

        Assert.False(result.Success);
        Assert.Contains("CloudApi:BootstrapSecret", result.Message, StringComparison.Ordinal);
        Assert.Contains(
            harness.StartupDiagnosticsStore.Current.Issues,
            issue => string.Equals(issue.Code, "CONFIG_INVALID", StringComparison.OrdinalIgnoreCase)
                     && issue.Message.Contains("CloudApi:BootstrapSecret", StringComparison.Ordinal));
    }

    [Fact]
    public async Task AppLifecycleManager_WhenCloudApiPathIsMissing_ShouldFailStartupValidation()
    {
        await using var harness = await AppLifecycleHarness.CreateAsync(
            enabledModules: ["Homogenization"],
            deviceModuleIds: ["Homogenization"],
            omittedCloudPathKey: "CloudApi:Paths:DeviceInstance");

        var result = await harness.Manager.StartAsync();

        Assert.False(result.Success);
        Assert.Contains("CloudApi:Paths:DeviceInstance", result.Message, StringComparison.Ordinal);
        Assert.Contains(
            harness.StartupDiagnosticsStore.Current.Issues,
            issue => string.Equals(issue.Code, "CONFIG_INVALID", StringComparison.OrdinalIgnoreCase)
                     && issue.Message.Contains("CloudApi:Paths:DeviceInstance", StringComparison.Ordinal));
    }

    [Fact]
    public async Task AppLifecycleManager_WhenProcessUploadPathIsMissing_ShouldFailStartupValidation()
    {
        await using var harness = await AppLifecycleHarness.CreateAsync(
            enabledModules: ["Homogenization"],
            deviceModuleIds: ["Homogenization"],
            omittedCloudPathKey: "CloudApi:Paths:ProcessUpload");

        var result = await harness.Manager.StartAsync();

        Assert.False(result.Success);
        Assert.Contains("CloudApi:Paths:ProcessUpload", result.Message, StringComparison.Ordinal);
        Assert.Contains(
            harness.StartupDiagnosticsStore.Current.Issues,
            issue => string.Equals(issue.Code, "CONFIG_INVALID", StringComparison.OrdinalIgnoreCase)
                     && issue.Message.Contains("CloudApi:Paths:ProcessUpload", StringComparison.Ordinal));
    }

    [Fact]
    public async Task AppLifecycleManager_WhenRecipePathMissingDeviceIdPlaceholder_ShouldFailStartupValidation()
    {
        await using var harness = await AppLifecycleHarness.CreateAsync(
            enabledModules: ["Homogenization"],
            deviceModuleIds: ["Homogenization"],
            recipeByDeviceTemplate: "/api/v1/edge/recipes/device");

        var result = await harness.Manager.StartAsync();

        Assert.False(result.Success);
        Assert.Contains("{deviceId}", result.Message, StringComparison.Ordinal);
        Assert.Contains(
            harness.StartupDiagnosticsStore.Current.Issues,
            issue => string.Equals(issue.Code, "CONFIG_INVALID", StringComparison.OrdinalIgnoreCase)
                     && issue.Message.Contains("CloudApi:Paths:RecipeByDeviceTemplate", StringComparison.Ordinal));
    }

    [Fact]
    public void CloudApiProductionCode_ShouldNotContainApiRouteDefaults()
    {
        var repoRoot = FindRepoRoot();
        var files = new[]
        {
            Path.Combine(repoRoot, "src", "Infrastructure", "IIoT.Edge.Infrastructure.Integration", "Config", "CloudApiConfig.cs"),
            Path.Combine(repoRoot, "src", "Infrastructure", "IIoT.Edge.Infrastructure.Integration", "Config", "CloudApiEndpointProvider.cs"),
            Path.Combine(repoRoot, "src", "Edge", "IIoT.Edge.Host.Bootstrap.Core", "StartupDiagnosticsReportBuilder.cs")
        };

        foreach (var file in files)
        {
            Assert.DoesNotContain("/api/v1/", File.ReadAllText(file), StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task AppLifecycleManager_WhenEnabledTaskSignalIsMissing_ShouldMarkRuntimeFaultAndSkipTaskRegistration()
    {
        await using var harness = await AppLifecycleHarness.CreateAsync(
            enabledModules: ["Homogenization"],
            deviceModuleIds: ["Homogenization"],
            environmentName: "Production");
        var device = Assert.Single(await harness.GetNetworkDevicesAsync());
        await harness.SaveTaskBindingsAsync(
            device.Id,
            "Homogenization",
            new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase)
            {
                ["Homogenization.Heartbeat"] = false,
                ["Homogenization.Inbound"] = true,
                ["Homogenization.Outbound"] = false,
                ["Homogenization.Recipe"] = false,
                ["Homogenization.EquipmentStatus"] = false,
                ["Homogenization.Realtime"] = false
            });
        var mappings = await harness.GetIoMappingsAsync(device.Id);
        var incompleteMappings = mappings
            .Where(static x => !string.Equals(x.SignalKey, "Homogenization.Interaction.Inbound", StringComparison.OrdinalIgnoreCase)
                               || !string.Equals(x.Direction, "Write", StringComparison.OrdinalIgnoreCase))
            .ToArray();
        await harness.ReplaceIoMappingsAsync(device.Id, incompleteMappings);

        var result = await harness.Manager.StartAsync();

        Assert.True(result.Success, result.Message);
        Assert.Empty(harness.PlcManager.RegisteredFactories);
        var fault = Assert.Single(harness.PlcManager.RuntimeFaults);
        Assert.Equal(device.Id, fault.NetworkDeviceId);
        Assert.Contains("任务绑定校验失败", fault.Error, StringComparison.Ordinal);
        Assert.Contains("Homogenization.Inbound", fault.Error, StringComparison.Ordinal);
        Assert.Contains("Homogenization.Interaction.Inbound/Write", fault.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void ValidationCatalog_ShouldRegisterProductModulesWithoutConflicts()
    {
        var pluginRoot = CreatePluginRuntimeRoot();
        try
        {
            var modules = CreateShellModuleCatalog().CreateAllModulesForValidation(DiscoverTestPlugins(pluginRoot).Modules);
            var viewRegistry = new ViewRegistry();
        var cellDataRegistry = new CellDataRegistry(new CellDataTypeRegistry());
            var runtimeRegistry = new StationRuntimeRegistry();
            var integrationRegistry = new ProcessIntegrationRegistry();
            var moduleParamRegistry = new ModuleParamRegistry();

            foreach (var module in modules)
            {
                module.Configure(new EdgeProcessModuleBuilder(
                    module.ModuleId,
                    module.ProcessType,
                    new ServiceCollection(),
                    CreateConfiguration(),
                    new ModuleViewRegistry(viewRegistry, module.ModuleId),
                    cellDataRegistry,
                    runtimeRegistry,
                    integrationRegistry,
                    moduleParamRegistry));
            }

            Assert.Single(modules);
            Assert.Single(cellDataRegistry.GetRegistrations());
            Assert.Single(runtimeRegistry.GetRegistrations());
            Assert.Single(integrationRegistry.GetCloudUploaders());
            Assert.Single(moduleParamRegistry.GetRegistrations());
            Assert.NotNull(viewRegistry.GetViewRegistration("Homogenization.DataView"));
        }
        finally
        {
            DeleteDirectory(pluginRoot);
        }
    }

    [Fact]
    public void ModuleViewRegistry_ShouldRejectCorePrefixedRoutes()
    {
        var registry = new ModuleViewRegistry(new ViewRegistry(), "Homogenization");

        var ex = Assert.Throws<InvalidOperationException>(() =>
            registry.RegisterRoute("Core.BadRoute", typeof(object), typeof(object)));

        Assert.Contains("Homogenization.", ex.Message, StringComparison.Ordinal);
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
    public Task NavigationService_WhenViewModelFactoryIsRegistered_ShouldUseFactory()
        => RunOnStaThreadAsync(() =>
        {
            var services = new ServiceCollection().BuildServiceProvider();
            var registry = new ViewRegistry();
            registry.RegisterRoute(
                "Plugin.Factory",
                typeof(TestNavigationView),
                typeof(DefaultNavigationViewModel),
                _ => new FactoryNavigationViewModel(),
                cacheView: false);

            var navigation = new NavigationService(services, registry, new SpyLogService());

            navigation.NavigateTo("Plugin.Factory");

            Assert.IsType<FactoryNavigationViewModel>(navigation.CurrentViewModel);
            Assert.IsType<TestNavigationView>(navigation.CurrentView);
            return Task.CompletedTask;
        });

    [Fact]
    public Task NavigationService_WhenViewModelFactoryIsMissing_ShouldResolveViewModelFromContainer()
        => RunOnStaThreadAsync(() =>
        {
            var services = new ServiceCollection()
                .AddTransient<DefaultNavigationViewModel>()
                .BuildServiceProvider();
            var registry = new ViewRegistry();
            registry.RegisterRoute(
                "Plugin.Default",
                typeof(TestNavigationView),
                typeof(DefaultNavigationViewModel),
                cacheView: false);

            var navigation = new NavigationService(services, registry, new SpyLogService());

            navigation.NavigateTo("Plugin.Default");

            Assert.IsType<DefaultNavigationViewModel>(navigation.CurrentViewModel);
            Assert.IsType<TestNavigationView>(navigation.CurrentView);
            return Task.CompletedTask;
        });

    [Fact]
    public void HostBootstrap_ShouldRegisterDiagnosticsCoreView()
    {
        var services = new ServiceCollection();
        var viewRegistry = new ViewRegistry();
        var configuration = CreateConfiguration();
        var hostRoot = Path.Combine(Path.GetTempPath(), "edge-host-bootstrap-" + Guid.NewGuid().ToString("N"));
        var pluginRoot = CreatePluginRuntimeRoot();

        try
        {
            var runtimePaths = CreateRuntimePaths(hostRoot, configuration);
            var discovery = DiscoverTestPlugins(pluginRoot);
            var activation = CreateShellModuleCatalog().CreateEnabledModules(configuration, discovery.Modules);
            services.AddEdgeHostBootstrap(
                viewRegistry,
                configuration,
                runtimePaths,
                "Production",
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
            if (Directory.Exists(hostRoot))
            {
                Directory.Delete(hostRoot, recursive: true);
            }

            DeleteDirectory(pluginRoot);
        }
    }

    private static IConfiguration CreateConfiguration(
        string[]? enabledModules = null,
        string environmentName = "Production",
        bool developmentSamplesEnabled = false,
        string? bootstrapSecret = "bootstrap-secret",
        string? omittedCloudPathKey = null,
        string? recipeByDeviceTemplate = null)
    {
        var settings = new Dictionary<string, string?>
        {
            ["CloudApi:BaseUrl"] = "https://cloud.test",
            ["CloudApi:ClientCode"] = "CLIENT-01",
            ["CloudApi:Paths:DeviceInstance"] = "/api/v1/bootstrap/device-instance",
            ["CloudApi:Paths:BootstrapRefresh"] = "/api/v1/bootstrap/edge-refresh",
            ["CloudApi:Paths:IdentityDeviceLogin"] = "/api/v1/bootstrap/edge-login",
            ["CloudApi:Paths:HumanIdentityRefresh"] = "/api/v1/human/identity/refresh",
            ["CloudApi:Paths:DeviceLog"] = "/api/v1/edge/device-logs",
            ["CloudApi:Paths:ProcessUpload"] = "/api/v1/edge/process-records",
            ["CloudApi:Paths:CapacityHourly"] = "/api/v1/edge/capacity/hourly",
            ["CloudApi:Paths:CapacitySummary"] = "/api/v1/edge/capacity/summary",
            ["CloudApi:Paths:CapacitySummaryRange"] = "/api/v1/edge/capacity/summary/range",
            ["CloudApi:Paths:RecipeByDeviceTemplate"] = recipeByDeviceTemplate ?? "/api/v1/edge/recipes/device/{deviceId}",
            ["Shell:Environment"] = environmentName,
            ["DevelopmentSamples:Enabled"] = developmentSamplesEnabled.ToString(),
            ["DevelopmentSamples:SampleBarcode"] = "ST-DEV-0001",
            ["DevelopmentSamples:SampleLayerCount"] = "12"
        };

        if (bootstrapSecret is not null)
        {
            settings["CloudApi:BootstrapSecret"] = bootstrapSecret;
        }

        if (!string.IsNullOrWhiteSpace(omittedCloudPathKey))
        {
            settings.Remove(omittedCloudPathKey);
        }

        enabledModules ??= [];
        for (var i = 0; i < enabledModules.Length; i++)
        {
            settings[$"Modules:Enabled:{i}"] = enabledModules[i];
        }

        return new ConfigurationBuilder()
            .AddInMemoryCollection(settings)
            .Build();
    }

    private static EdgeRuntimePaths CreateRuntimePaths(string baseDirectory, IConfiguration configuration)
        => new ShellRuntimePathResolver().Resolve(baseDirectory, configuration);

    private static IShellModuleCatalog CreateShellModuleCatalog()
        => new ShellModuleCatalog(CreateModuleCatalog());

    private static IModuleCatalog CreateModuleCatalog()
        => new DirectoryModuleCatalog(new ModulePluginLoader(new ModulePluginAssemblyResolver()));

    private sealed class TestHostEnvironment(string environmentName) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = environmentName;

        public string ApplicationName { get; set; } = "IIoT.Edge.Shell.Tests";

        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;

        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }

    private static ModuleCatalogDiscoveryResult DiscoverTestPlugins(string pluginRoot)
    {
        return CreateShellModuleCatalog().DiscoverModules(pluginRoot);
    }

    private static string CreatePluginRuntimeRoot(string? targetRoot = null)
    {
        var pluginRoot = targetRoot ?? Path.Combine(Path.GetTempPath(), "edge-shell-plugin-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(pluginRoot);

        var runtimeModulesRoot = CreateShellModuleCatalog().GetPluginRootPath(AppContext.BaseDirectory);
        foreach (var moduleId in new[] { "Homogenization" })
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
            "Homogenization" => Path.Combine(FindRepoRoot(), "src", "Modules", "IIoT.Edge.Module.Homogenization"),
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

    private static string? GetStringProperty(object target, string propertyName)
        => target.GetType()
            .GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public)
            ?.GetValue(target) as string;

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

    private static IoMappingEntity CreateIoMapping(
        int networkDeviceId,
        string signalKey,
        string plcAddress,
        int addressCount,
        string dataType,
        string direction,
        int sortOrder)
    {
        var entity = IoMappingEntity.Create(networkDeviceId, signalKey, plcAddress, addressCount, dataType, direction);
        entity.UpdateSortOrder(sortOrder);
        return entity;
    }

    private static Task RunOnStaThreadAsync(Func<Task> testBody)
    {
        var completion = new TaskCompletionSource<object?>();
        var thread = new Thread(async () =>
        {
            try
            {
                await testBody().ConfigureAwait(false);
                completion.SetResult(null);
            }
            catch (Exception ex)
            {
                completion.SetException(ex);
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return completion.Task;
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
            SpyLogService logger,
            IStartupDiagnosticsStore startupDiagnosticsStore)
        {
            _serviceProvider = serviceProvider;
            _tempDirectory = tempDirectory;
            Manager = manager;
            PlcManager = plcManager;
            ContextStore = contextStore;
            BackgroundCoordinator = backgroundCoordinator;
            Logger = logger;
            StartupDiagnosticsStore = startupDiagnosticsStore;
        }

        public AppLifecycleManager Manager { get; }

        public SpyPlcConnectionManager PlcManager { get; }

        public SpyProductionContextStore ContextStore { get; }

        public SpyBackgroundServiceCoordinator BackgroundCoordinator { get; }

        public SpyLogService Logger { get; }

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

        public async Task SaveTaskBindingsAsync(
            int networkDeviceId,
            string moduleId,
            IReadOnlyDictionary<string, bool> taskStates)
            => await _serviceProvider
                .GetRequiredService<IPlcTaskBindingService>()
                .SaveDeviceBindingsAsync(networkDeviceId, moduleId, taskStates)
                .ConfigureAwait(false);

        public static async Task<AppLifecycleHarness> CreateAsync(
            string[] enabledModules,
            string[] deviceModuleIds,
            string environmentName = "Production",
            bool developmentSamplesEnabled = false,
            string? bootstrapSecret = "bootstrap-secret",
            string? omittedCloudPathKey = null,
            string? recipeByDeviceTemplate = null)
        {
            var tempDirectory = Path.Combine(Path.GetTempPath(), "edge-shell-tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDirectory);

            var configuration = CreateConfiguration(
                enabledModules,
                environmentName,
                developmentSamplesEnabled,
                bootstrapSecret,
                omittedCloudPathKey,
                recipeByDeviceTemplate);
            var runtimePaths = CreateRuntimePaths(tempDirectory, configuration);

            var services = new ServiceCollection();
            services.AddSingleton(runtimePaths);
            services.AddEfCorePersistenceInfrastructure(Path.Combine(runtimePaths.DatabaseDirectory, "edge.db"));
            services.AddDapperPersistenceInfrastructure(runtimePaths.DatabaseDirectory);
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
            services.AddSingleton<IHostEnvironment>(new TestHostEnvironment(environmentName));
            services.AddSingleton(shiftConfig);
            services.AddSingleton<IPlcConnectionManager>(plcManager);
            services.AddSingleton<IProductionContextStore>(contextStore);
            services.AddSingleton<IBackgroundServiceCoordinator>(backgroundCoordinator);
            services.AddSingleton<ILogService>(logger);
            services.AddSingleton<IRecipeService>(recipeService);
            services.AddSingleton<IDataPipelineService, SpyDataPipelineService>();
            services.AddSingleton<IProductionContextSignalBindingStore, ProductionContextSignalBindingStore>();
            services.AddTransient<IPlcTaskBindingService, PlcTaskBindingService>();
            var discovery = DiscoverTestPlugins(pluginRoot);
            var activation = CreateShellModuleCatalog().CreateEnabledModules(configuration, discovery.Modules);
            var moduleViewRegistry = new ViewRegistry();
        var cellDataRegistry = new CellDataRegistry(new CellDataTypeRegistry());
            var runtimeRegistry = new StationRuntimeRegistry();
            var integrationRegistry = new ProcessIntegrationRegistry();
            var moduleParamRegistry = new ModuleParamRegistry();
            services.AddSingleton<IStationRuntimeRegistry>(runtimeRegistry);

            foreach (var module in activation.Modules)
            {
                services.AddSingleton<IEdgeProcessModule>(module);
                module.Configure(new EdgeProcessModuleBuilder(
                    module.ModuleId,
                    module.ProcessType,
                    services,
                    configuration,
                    new ModuleViewRegistry(moduleViewRegistry, module.ModuleId),
                    cellDataRegistry,
                    runtimeRegistry,
                    integrationRegistry,
                    moduleParamRegistry));
            }

            services.AddSingleton<IDevelopmentSampleInitializer, DevelopmentSampleInitializer>();
            services.AddSingleton<IStartupDiagnosticsStore, StartupDiagnosticsStore>();

            var serviceProvider = services.BuildServiceProvider();
            serviceProvider.ApplyMigrations();

            await SeedDevicesAsync(serviceProvider, deviceModuleIds).ConfigureAwait(false);

            var diagnosticsStore = serviceProvider.GetRequiredService<IStartupDiagnosticsStore>();
            var developmentSampleInitializer = serviceProvider.GetRequiredService<IDevelopmentSampleInitializer>();
            var networkDevices = serviceProvider.GetRequiredService<IRepository<NetworkDeviceEntity>>();
            var ioMappings = serviceProvider.GetRequiredService<IRepository<IoMappingEntity>>();
            var diagnosticsReportBuilder = new StartupDiagnosticsReportBuilder(
                configuration,
                runtimePaths,
                shiftConfig,
                networkDevices,
                ioMappings,
                cellDataRegistry,
                runtimeRegistry,
                integrationRegistry,
                new StartupPluginLifecycleSnapshotBuilder(),
                discovery.Modules,
                [.. discovery.Issues, .. activation.Issues],
                activation.EnabledModuleIds,
                activation.Modules,
                serviceProvider.GetServices<IModuleHardwareProfileProvider>());
            var manager = new AppLifecycleManager(
                new AppStartupInitializer(
                    serviceProvider,
                    developmentSampleInitializer,
                    logger),
                diagnosticsReportBuilder,
                diagnosticsStore,
                new PlcRuntimeTaskBinder(
                    serviceProvider,
                    networkDevices,
                    ioMappings,
                    plcManager,
                    runtimeRegistry,
                    serviceProvider.GetRequiredService<IPlcTaskBindingService>(),
                    serviceProvider.GetRequiredService<IProductionContextSignalBindingStore>(),
                    logger),
                new AppRuntimeStateCoordinator(
                    contextStore,
                    recipeService,
                    developmentSampleInitializer,
                    logger),
                backgroundCoordinator,
                logger);

            return new AppLifecycleHarness(
                serviceProvider,
                tempDirectory,
                manager,
                plcManager,
                contextStore,
                backgroundCoordinator,
                logger,
                diagnosticsStore);
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
                var device = NetworkDeviceEntity.Create(deviceName, DeviceType.PLC, "127.0.0.1", 102 + index);
                device.AssignModule(moduleIds[index], PlcType.S7.ToString());
                device.UpdateEndpoint("127.0.0.1", 102 + index, null, 3000);
                device.Enable();

                networkRepo.Add(device);
                await networkRepo.SaveChangesAsync().ConfigureAwait(false);

                if (hardwareProfiles.TryGetValue(moduleIds[index], out var provider))
                {
                    foreach (var mapping in provider.GetDefaultIoTemplate().Where(static x => !string.IsNullOrWhiteSpace(x.PlcAddress)))
                    {
                        var entity = IoMappingEntity.Create(
                            device.Id,
                            mapping.SignalKey,
                            mapping.PlcAddress,
                            mapping.AddressCount,
                            mapping.DataType,
                            mapping.Direction);
                        entity.UpdateSortOrder(mapping.SortOrder);
                        ioRepo.Add(entity);
                    }
                }
                else
                {
                    ioRepo.Add(CreateIoMapping(device.Id, $"Signal-{index + 1}", $"DB1.DBW{index * 2}", 1, "Int16", "Read", index + 1));
                }

                await ioRepo.SaveChangesAsync().ConfigureAwait(false);
            }
        }
    }

    private sealed class SpyPlcConnectionManager : IPlcConnectionManager
    {
        public sealed record RuntimeFault(int NetworkDeviceId, string DeviceName, string Error);

        public Dictionary<string, Func<IPlcBuffer, ProductionContext, List<IPlcTask>>> RegisteredFactories { get; }
            = new(StringComparer.OrdinalIgnoreCase);

        public List<RuntimeFault> RuntimeFaults { get; } = [];

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

        public void MarkRuntimeFault(int networkDeviceId, string deviceName, string error)
            => RuntimeFaults.Add(new RuntimeFault(networkDeviceId, deviceName, error));

        public PlcConnectionRuntimeSnapshot? GetRuntimeStatus(int networkDeviceId) => null;

        public IReadOnlyCollection<PlcConnectionRuntimeSnapshot> GetRuntimeStatuses()
            => Array.Empty<PlcConnectionRuntimeSnapshot>();

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
            => GetOrCreate(deviceName, moduleId: null);

        public ProductionContext GetOrCreate(string deviceName, string? moduleId)
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

    private sealed class TestNavigationView : System.Windows.Controls.ContentControl
    {
    }

    private sealed class DefaultNavigationViewModel : ViewModelBase
    {
        public override string ViewId => "Plugin.Default";

        public override string ViewTitle => "榛樿椤甸潰";
    }

    private sealed class FactoryNavigationViewModel : ViewModelBase
    {
        public override string ViewId => "Plugin.Factory";

        public override string ViewTitle => "宸ュ巶椤甸潰";
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
