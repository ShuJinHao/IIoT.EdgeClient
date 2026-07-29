using IIoT.Edge.Module.Contracts.Diagnostics;
using IIoT.Edge.Application.Modules.Hardware;
using IIoT.Edge.Module.Contracts.Hardware;
using IIoT.Edge.Module.Sdk.Hardware;
using IIoT.Edge.Module.Contracts.Runtime;
using IIoT.Edge.Module.Contracts.Context;
using IIoT.Edge.Module.Contracts.Config;
using IIoT.Edge.Module.Contracts.DataPipeline;
using IIoT.Edge.Module.Contracts.Logging;
using IIoT.Edge.Module.Contracts.Modules;
using IIoT.Edge.Module.Contracts.Plc;
using IIoT.Edge.Module.Contracts.Plc.Store;
using IIoT.Edge.Module.Contracts.Recipe;
using IIoT.Edge.Module.Contracts.Tasks;
using IIoT.Edge.Module.Contracts.Time;
using IIoT.Edge.Application.Common.Tasks;
using IIoT.Edge.Application.Common.Plc;
using IIoT.Edge.Application.Features.Config.CloudApi;
using IIoT.Edge.Application.Features.Config.ModuleParameters;
using IIoT.Edge.Application.Features.Config.SchemaReconciliation;
using IIoT.Edge.Application.Features.Hardware.PlcTaskBindings;
using IIoT.Edge.Module.Contracts.UI;
using IIoT.Edge.Domain.Hardware.Aggregates;
using IIoT.Edge.Infrastructure.DeviceComm.Plc;
using IIoT.Edge.Infrastructure.DeviceComm.Plc.Store;
using IIoT.Edge.Infrastructure.Persistence.Dapper;
using IIoT.Edge.Infrastructure.Persistence.Dapper.Connection;
using IIoT.Edge.Infrastructure.Persistence.Dapper.Repository;
using IIoT.Edge.Infrastructure.Persistence.EfCore;
using IIoT.Edge.Host.Bootstrap;
using IIoT.Edge.Host.Bootstrap.Modules;
using IIoT.Edge.Presentation.Navigation;
using IIoT.Edge.Module.Contracts.DataPipeline.Capacity;
using IIoT.Edge.Module.Contracts.DataPipeline.CellData;
using IIoT.Edge.Module.Contracts.DataPipeline.Recipe;
using IIoT.Edge.SharedKernel.Repository;
using IIoT.Edge.SharedKernel.Configuration;
using IIoT.Edge.Module.Sdk.Signals;
using IIoT.Edge.Shell.Core;
using IIoT.Edge.Shell.Modules;
using IIoT.Edge.UI.Shared.PluginSystem;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using System.Reflection;
using Xunit;

namespace IIoT.Edge.Startup.IntegrationTests;

public sealed class ModuleRuntimeRegistrationTests
{
    [Fact]
    public void StartupModuleRegistrationValidator_WhenModuleIsMesOnly_ShouldNotRequireCloudUploader()
    {
        var module = new DiagnosticProcessModule("MesOnlyModule", "MesOnlyProcess", requiresCloud: false, requiresMes: true);
        var (validator, context) = CreateRegistrationValidatorContext(module, registerMesUploader: true);
        var issues = new List<StartupDiagnosticIssue>();

        validator.Validate(context, issues);

        Assert.DoesNotContain(issues, issue => issue.Code == "CLOUD_UPLOADER_MISSING");
        Assert.DoesNotContain(issues, issue => issue.Code == "MES_UPLOADER_MISSING");
    }

    [Fact]
    public void StartupModuleRegistrationValidator_WhenModuleRequiresMesUploader_ShouldReportMissingMesUploader()
    {
        var module = new DiagnosticProcessModule("MesOnlyModule", "MesOnlyProcess", requiresCloud: false, requiresMes: true);
        var (validator, context) = CreateRegistrationValidatorContext(module, registerMesUploader: false);
        var issues = new List<StartupDiagnosticIssue>();

        validator.Validate(context, issues);

        Assert.DoesNotContain(issues, issue => issue.Code == "CLOUD_UPLOADER_MISSING");
        Assert.Contains(issues, issue => issue.Code == "MES_UPLOADER_MISSING");
    }

    [Fact]
    public void ConfiguredCatalog_WhenNoModulesSectionExists_ShouldNotEnableDiscoveredModules()
    {
        var pluginRoot = CreatePluginRuntimeRootFor("TestPlugin");
        try
        {
            var discovery = DiscoverTestPlugins(pluginRoot);
            var activation = CreateShellModuleCatalog().CreateEnabledModules(CreateConfiguration(), discovery.Modules);

            Assert.Empty(discovery.Issues);
            Assert.Empty(activation.Modules);
            Assert.Contains(activation.Issues, issue => issue.Code == "PLUGIN_ENABLED_EMPTY");
            Assert.Contains(activation.Issues, issue => issue.Code == "PLUGIN_NONE_ENABLED");
        }
        finally
        {
            DeleteDirectory(pluginRoot);
        }
    }

    private static (StartupModuleRegistrationValidator Validator, StartupValidationContext Context)
        CreateRegistrationValidatorContext(
            DiagnosticProcessModule module,
            bool registerMesUploader)
    {
        var cellDataRegistry = new CellDataRegistry(new CellDataTypeRegistry());
        cellDataRegistry.Register<DiagnosticCellData>(module.ProcessType);
        var runtimeRegistry = new StationRuntimeRegistry();
        runtimeRegistry.Register(new DiagnosticRuntimeFactory(module.ModuleId));
        var integrationRegistry = new ProcessIntegrationRegistry();
        if (registerMesUploader)
        {
            integrationRegistry.RegisterMesUploader(module.ProcessType, ProcessUploadMode.Single);
        }

        var context = new StartupValidationContext
        {
            ConfigurationProfile = new ConfigurationProfileSnapshot(
                "Test",
                "TestProfile",
                "appsettings.machine.Test.json",
                IsMachineProfileLoaded: true,
                RuntimeDataRoot: "/tmp/test"),
            SystemCloudEnabled = false,
            PlcDevices = [],
            ModulesById = new Dictionary<string, IEdgeProcessModule>(StringComparer.OrdinalIgnoreCase)
            {
                [module.ModuleId] = module
            },
            DiscoveredModulesById = new Dictionary<string, ModulePluginDescriptor>(StringComparer.OrdinalIgnoreCase),
            HardwareProfilesByModuleId = new Dictionary<string, IModuleHardwareProfileProvider>(StringComparer.OrdinalIgnoreCase)
        };
        return (new StartupModuleRegistrationValidator(cellDataRegistry, runtimeRegistry, integrationRegistry), context);
    }

    [Fact]
    public void DiscoverDirectoryPlugins_ShouldFindTestPluginFixture()
    {
        var pluginRoot = CreatePluginRuntimeRootFor("TestPlugin");

        try
        {
            AssertStagedModuleLayout(
                pluginRoot,
                "TestPlugin",
                "test-plugin.module.json",
                "IIoT.Edge.TestPlugin.dll");

            var discovery = DiscoverTestPlugins(pluginRoot);

            Assert.Empty(discovery.Issues);
            Assert.Equal(["TestPlugin"], discovery.Modules.Select(x => x.ModuleId).ToArray());
        }
        finally
        {
            DeleteDirectory(pluginRoot);
        }
    }

    private static void AssertStagedModuleLayout(
        string pluginRoot,
        string moduleId,
        string configFileName,
        string entryAssemblyName,
        bool hasLanguageResources = false)
    {
        var runtimeDirectory = Path.Combine(pluginRoot, moduleId);

        Assert.True(Directory.Exists(runtimeDirectory));
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
    public void ConfiguredCatalog_WhenTestPluginIsEnabled_ShouldLoadModule()
    {
        var pluginRoot = CreatePluginRuntimeRootFor("TestPlugin");
        try
        {
            var discovery = DiscoverTestPlugins(pluginRoot);
            var activation = CreateShellModuleCatalog().CreateEnabledModules(
                CreateConfiguration(["TestPlugin"]),
                discovery.Modules);

            Assert.Empty(activation.Issues);
            Assert.Single(activation.Modules);
            Assert.Equal(["TestPlugin"], activation.Modules.Select(module => module.ModuleId).ToArray());
        }
        finally
        {
            DeleteDirectory(pluginRoot);
        }
    }

    [Fact]
    public void ConfiguredCatalog_WhenOneStagedPluginHasApprovedAssemblyFailure_ShouldLoadIndependentPlugin()
    {
        var pluginRoot = CreatePluginRuntimeRootFor("TestPlugin");
        try
        {
            var brokenPluginDirectory = Path.Combine(pluginRoot, "BrokenPlugin");
            Directory.CreateDirectory(brokenPluginDirectory);
            File.WriteAllText(
                Path.Combine(brokenPluginDirectory, "plugin.json"),
                """
                {
                  "moduleId": "BrokenPlugin",
                  "supportedProcessType": "BrokenProcess",
                  "displayName": "Broken Plugin",
                  "version": "1.0.0",
                  "hostApiVersion": "2.0.0",
                  "minHostVersion": "2.0.0",
                  "maxHostVersion": "99.0.0",
                  "dependencies": [],
                  "entryAssembly": "BrokenPlugin.dll",
                  "entryType": "BrokenPlugin.DependencyInjection"
                }
                """);
            File.WriteAllText(
                Path.Combine(brokenPluginDirectory, "BrokenPlugin.dll"),
                "not a managed assembly");

            var catalog = CreateShellModuleCatalog();
            var discovery = catalog.DiscoverModules(pluginRoot);
            var activation = catalog.CreateEnabledModules(
                CreateConfiguration(["BrokenPlugin", "TestPlugin"]),
                discovery.Modules);

            Assert.Empty(discovery.Issues);
            Assert.Equal(["TestPlugin"], activation.Modules.Select(module => module.ModuleId).ToArray());
            var issue = Assert.Single(activation.Issues);
            Assert.Equal("PLUGIN_LOAD_FAILED", issue.Code);
            Assert.Equal("BrokenPlugin", issue.ModuleId);
        }
        finally
        {
            DeleteDirectory(pluginRoot);
        }
    }

    [Fact]
    public void ConfiguredCatalog_WhenUnknownModuleIsConfigured_ShouldReportActivationIssue()
    {
        var pluginRoot = CreatePluginRuntimeRootFor("TestPlugin");
        try
        {
            var discovery = DiscoverTestPlugins(pluginRoot);
            var activation = CreateShellModuleCatalog().CreateEnabledModules(
                CreateConfiguration(["TestPlugin", "UnknownModule"]),
                discovery.Modules);

            Assert.Single(activation.Modules);
            Assert.Equal("TestPlugin", activation.Modules[0].ModuleId);
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
    public async Task AppLifecycleManager_WhenOnlyTestPluginIsEnabled_ShouldRunNeutralPluginLifecycleExactlyOnce()
    {
        var harness = await AppLifecycleHarness.CreateAsync(
            enabledModules: ["TestPlugin"],
            deviceModuleIds: ["TestPlugin"]);
        var lifecycleProbe = harness.GetTestPluginLifecycleProbe();

        try
        {
            var result = await harness.Manager.StartAsync(TestContext.Current.CancellationToken);

            Assert.True(result.Success, result.Message);

            var report = harness.StartupDiagnosticsStore.Current;
            Assert.Equal(["TestPlugin"], report.EnabledModules);
            Assert.Equal(["TestPlugin"], report.ActivatedModules);

            var testPluginState = Assert.Single(
                report.PluginStates,
                x => string.Equals(x.ModuleId, "TestPlugin", StringComparison.OrdinalIgnoreCase));
            Assert.Equal(PluginLifecycleState.Activated, testPluginState.State);
            Assert.Equal(1, lifecycleProbe.StartCount);
            Assert.Equal(0, lifecycleProbe.StopCount);
            Assert.Equal(0, lifecycleProbe.DisposeCount);

            await harness.Manager.StopAsync(TestContext.Current.CancellationToken);

            Assert.Equal(1, lifecycleProbe.StartCount);
            Assert.Equal(1, lifecycleProbe.StopCount);
            Assert.Equal(0, lifecycleProbe.DisposeCount);
        }
        finally
        {
            await harness.DisposeAsync();
        }

        Assert.Equal(1, lifecycleProbe.StartCount);
        Assert.Equal(1, lifecycleProbe.StopCount);
        Assert.Equal(1, lifecycleProbe.DisposeCount);
    }

    [Fact]
    public async Task AppLifecycleManager_WhenCallerCancelsStartup_ShouldPropagateCancellationWithoutStartingBackgroundServices()
    {
        await using var harness = await AppLifecycleHarness.CreateAsync(
            enabledModules: ["TestPlugin"],
            deviceModuleIds: ["TestPlugin"]);
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            harness.Manager.StartAsync(cancellation.Token));

        Assert.Equal(0, harness.BackgroundCoordinator.StartCallCount);
        Assert.Contains(
            harness.Logger.Entries,
            entry => entry.Level == "Warn"
                     && entry.Message.Contains("应用启动已取消", StringComparison.Ordinal));
    }

    [Fact]
    public async Task AppLifecycleManager_WhenStartsOverlap_ShouldRunLifecycleOnlyOnce()
    {
        await using var harness = await AppLifecycleHarness.CreateAsync(
            enabledModules: ["TestPlugin"],
            deviceModuleIds: ["TestPlugin"]);

        var starts = Enumerable.Range(0, 4)
            .Select(_ => harness.Manager.StartAsync(TestContext.Current.CancellationToken))
            .ToArray();
        var results = await Task.WhenAll(starts);

        Assert.All(results, static result => Assert.True(result.Success, result.Message));
        Assert.Equal(1, harness.BackgroundCoordinator.StartCallCount);

        await harness.Manager.StopAsync(TestContext.Current.CancellationToken);
        await harness.Manager.StopAsync(TestContext.Current.CancellationToken);
        Assert.Equal(1, harness.BackgroundCoordinator.StopCallCount);
    }

    [Fact]
    public async Task AppLifecycleManager_WhenStoppedBeforeStart_ShouldRemainNoOp()
    {
        await using var harness = await AppLifecycleHarness.CreateAsync(
            enabledModules: ["TestPlugin"],
            deviceModuleIds: ["TestPlugin"]);

        await harness.Manager.StopAsync(TestContext.Current.CancellationToken);

        Assert.Equal(0, harness.BackgroundCoordinator.StartCallCount);
        Assert.Equal(0, harness.BackgroundCoordinator.StopCallCount);
    }

    [Fact]
    public async Task AppLifecycleManager_WhenContextSaveFails_ShouldStillSaveRecipeAndStopBackgroundServices()
    {
        var saveFailure = new IOException("context disk full");
        await using var harness = await AppLifecycleHarness.CreateAsync(
            enabledModules: ["TestPlugin"],
            deviceModuleIds: ["TestPlugin"],
            contextSaveException: saveFailure);
        var start = await harness.Manager.StartAsync(TestContext.Current.CancellationToken);
        Assert.True(start.Success, start.Message);

        var actual = await Assert.ThrowsAsync<IOException>(
            () => harness.Manager.StopAsync(TestContext.Current.CancellationToken));

        Assert.Same(saveFailure, actual);
        Assert.Equal(1, harness.ContextStore.SaveCallCount);
        Assert.Equal(1, harness.RecipeService.SaveCallCount);
        Assert.Equal(1, harness.BackgroundCoordinator.StopCallCount);
        Assert.DoesNotContain(
            harness.Logger.Entries,
            entry => entry.Message.Contains("关闭前运行时状态已保存", StringComparison.Ordinal));
    }

    [Fact]
    public async Task BackgroundServiceCoordinator_WhenCallerCancelsMidBatch_ShouldStopStartedServicesInReverseAndRethrowSameCancellation()
    {
        using var cancellation = new CancellationTokenSource();
        var cancellationException = new OperationCanceledException(
            "caller canceled during background startup",
            innerException: null,
            cancellation.Token);
        var events = new List<string>();
        var first = new RecordingManagedBackgroundService("First", events);
        var second = new RecordingManagedBackgroundService("Second", events);
        var canceling = new RecordingManagedBackgroundService(
            "Canceling",
            events,
            _ =>
            {
                cancellation.Cancel();
                return Task.FromException(cancellationException);
            });
        var neverStarted = new RecordingManagedBackgroundService("NeverStarted", events);
        var coordinator = new BackgroundServiceCoordinator(
            [first, second, canceling, neverStarted],
            new SpyLogService());

        var actual = await Assert.ThrowsAsync<OperationCanceledException>(
            () => coordinator.StartAsync(cancellation.Token));

        Assert.Same(cancellationException, actual);
        Assert.Equal(
            ["start:First", "start:Second", "start:Canceling", "stop:Second", "stop:First"],
            events);
        Assert.Equal(1, first.StopCallCount);
        Assert.Equal(1, second.StopCallCount);
        Assert.Equal(0, canceling.StopCallCount);
        Assert.Equal(0, neverStarted.StartCallCount);
    }

    [Fact]
    public async Task AppLifecycleManager_WhenBootstrapSecretIsMissing_ShouldStartWithDiagnosticIssue()
    {
        await using var harness = await AppLifecycleHarness.CreateAsync(
            enabledModules: ["TestPlugin"],
            deviceModuleIds: ["TestPlugin"],
            bootstrapSecret: null);

        var result = await harness.Manager.StartAsync(TestContext.Current.CancellationToken);

        Assert.True(result.Success, result.Message);
        Assert.Contains(
            harness.StartupDiagnosticsStore.Current.Issues,
            issue => string.Equals(issue.Code, "CONFIG_INVALID", StringComparison.OrdinalIgnoreCase)
                     && issue.Message.Contains("CloudApi:BootstrapSecret", StringComparison.Ordinal));
    }

    [Fact]
    public async Task AppLifecycleManager_WhenClientCodeIsMissing_ShouldStartWithDiagnosticIssue()
    {
        await using var harness = await AppLifecycleHarness.CreateAsync(
            enabledModules: ["TestPlugin"],
            deviceModuleIds: ["TestPlugin"],
            clientCode: null);

        var result = await harness.Manager.StartAsync(TestContext.Current.CancellationToken);

        Assert.True(result.Success, result.Message);
        Assert.Contains(
            harness.StartupDiagnosticsStore.Current.Issues,
            issue => string.Equals(issue.Code, "CONFIG_INVALID", StringComparison.OrdinalIgnoreCase)
                     && issue.Message.Contains("CloudApi:ClientCode", StringComparison.Ordinal));
    }

    [Fact]
    public async Task AppLifecycleManager_WhenIoMappingAddressIsEmpty_ShouldStartWithDiagnosticIssue()
    {
        await using var harness = await AppLifecycleHarness.CreateAsync(
            enabledModules: ["TestPlugin"],
            deviceModuleIds: ["TestPlugin"]);
        var device = Assert.Single(await harness.GetNetworkDevicesAsync());
        var mappings = await harness.GetIoMappingsAsync(device.Id);
        var sourceMapping = mappings.OrderBy(static x => x.SortOrder).First();
        var emptyAddressMapping = IoMappingEntity.Create(
            device.Id,
            sourceMapping.SignalKey,
            string.Empty,
            sourceMapping.AddressCount,
            sourceMapping.DataType,
            sourceMapping.Direction,
            sourceMapping.Category,
            sourceMapping.BusinessGroup);
        emptyAddressMapping.UpdateSortOrder(sourceMapping.SortOrder);

        await harness.ReplaceIoMappingsAsync(
            device.Id,
            mappings
                .Where(x => x.Id != sourceMapping.Id)
                .Append(emptyAddressMapping)
                .ToArray());

        var result = await harness.Manager.StartAsync(TestContext.Current.CancellationToken);

        Assert.True(result.Success, result.Message);
        Assert.Contains(
            harness.StartupDiagnosticsStore.Current.Issues,
            issue => string.Equals(issue.Code, "DEVICE_MODULE_MISMATCH", StringComparison.OrdinalIgnoreCase)
                     && issue.Message.Contains("PlcAddress 为空", StringComparison.Ordinal));
        Assert.Contains(
            harness.Logger.Entries,
            entry => string.Equals(entry.Level, "Warn", StringComparison.OrdinalIgnoreCase)
                     && entry.Message.Contains("非阻断", StringComparison.Ordinal));
    }

    [Fact]
    public async Task AppLifecycleManager_WhenCloudApiPathIsMissing_ShouldStartWithDiagnosticIssue()
    {
        await using var harness = await AppLifecycleHarness.CreateAsync(
            enabledModules: ["TestPlugin"],
            deviceModuleIds: ["TestPlugin"],
            omittedCloudPathKey: "CloudApi:Paths:DeviceInstance");

        var result = await harness.Manager.StartAsync(TestContext.Current.CancellationToken);

        Assert.True(result.Success, result.Message);
        Assert.Contains(
            harness.StartupDiagnosticsStore.Current.Issues,
            issue => string.Equals(issue.Code, "CONFIG_INVALID", StringComparison.OrdinalIgnoreCase)
                     && issue.Message.Contains("CloudApi:Paths:DeviceInstance", StringComparison.Ordinal));
    }

    [Fact]
    public async Task AppLifecycleManager_WhenCloudApiPathStartsWithSlash_ShouldPassStartupValidation()
    {
        await using var harness = await AppLifecycleHarness.CreateAsync(
            enabledModules: ["TestPlugin"],
            deviceModuleIds: ["TestPlugin"],
            deviceInstancePath: "/api/v1/bootstrap/device-instance");

        var result = await harness.Manager.StartAsync(TestContext.Current.CancellationToken);

        Assert.True(result.Success, result.Message);
        Assert.DoesNotContain(
            harness.StartupDiagnosticsStore.Current.Issues,
            issue => issue.Message.Contains("CloudApi:Paths:DeviceInstance", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("https://cloud.test/api/v1/bootstrap/device-instance")]
    [InlineData("http://cloud.test/api/v1/bootstrap/device-instance")]
    [InlineData("file://api/v1/bootstrap/device-instance")]
    public async Task AppLifecycleManager_WhenCloudApiPathHasExplicitScheme_ShouldStartWithDiagnosticIssue(string deviceInstancePath)
    {
        await using var harness = await AppLifecycleHarness.CreateAsync(
            enabledModules: ["TestPlugin"],
            deviceModuleIds: ["TestPlugin"],
            deviceInstancePath: deviceInstancePath);

        var result = await harness.Manager.StartAsync(TestContext.Current.CancellationToken);

        Assert.True(result.Success, result.Message);
        Assert.Contains(
            harness.StartupDiagnosticsStore.Current.Issues,
            issue => string.Equals(issue.Code, "CONFIG_INVALID", StringComparison.OrdinalIgnoreCase)
                     && issue.Message.Contains("CloudApi:Paths:DeviceInstance", StringComparison.Ordinal)
                     && issue.Message.Contains("相对 API 路径", StringComparison.Ordinal));
    }

    [Fact]
    public async Task AppLifecycleManager_WhenCloudApiPathStartsWithDoubleSlash_ShouldStartWithDiagnosticIssue()
    {
        await using var harness = await AppLifecycleHarness.CreateAsync(
            enabledModules: ["TestPlugin"],
            deviceModuleIds: ["TestPlugin"],
            deviceInstancePath: "//api/v1/bootstrap/device-instance");

        var result = await harness.Manager.StartAsync(TestContext.Current.CancellationToken);

        Assert.True(result.Success, result.Message);
        Assert.Contains(
            harness.StartupDiagnosticsStore.Current.Issues,
            issue => string.Equals(issue.Code, "CONFIG_INVALID", StringComparison.OrdinalIgnoreCase)
                     && issue.Message.Contains("CloudApi:Paths:DeviceInstance", StringComparison.Ordinal)
                     && issue.Message.Contains("单个 /", StringComparison.Ordinal));
    }

    [Fact]
    public async Task AppLifecycleManager_WhenRecipePathMissingDeviceIdPlaceholder_ShouldStartWithDiagnosticIssue()
    {
        await using var harness = await AppLifecycleHarness.CreateAsync(
            enabledModules: ["TestPlugin"],
            deviceModuleIds: ["TestPlugin"],
            recipeByDeviceTemplate: "/api/v1/edge/recipes/device");

        var result = await harness.Manager.StartAsync(TestContext.Current.CancellationToken);

        Assert.True(result.Success, result.Message);
        Assert.Contains(
            harness.StartupDiagnosticsStore.Current.Issues,
            issue => string.Equals(issue.Code, "CONFIG_INVALID", StringComparison.OrdinalIgnoreCase)
                     && issue.Message.Contains("CloudApi:Paths:RecipeByDeviceTemplate", StringComparison.Ordinal));
    }

    [Fact]
    public async Task AppLifecycleManager_WhenBackgroundStartupFails_ShouldStillStartShell()
    {
        await using var harness = await AppLifecycleHarness.CreateAsync(
            enabledModules: ["TestPlugin"],
            deviceModuleIds: ["TestPlugin"],
            backgroundStartException: new InvalidOperationException("cloud task unavailable"));

        var result = await harness.Manager.StartAsync(TestContext.Current.CancellationToken);

        Assert.True(result.Success, result.Message);
        Assert.Equal(1, harness.BackgroundCoordinator.StartCallCount);
        Assert.Contains(
            harness.Logger.Entries,
            entry => string.Equals(entry.Level, "Warn", StringComparison.OrdinalIgnoreCase)
                     && entry.Message.Contains("后台服务启动失败", StringComparison.Ordinal)
                     && entry.Message.Contains("非阻断", StringComparison.Ordinal));
        Assert.Contains(
            harness.StartupDiagnosticsStore.Current.Issues,
            issue => string.Equals(issue.Code, "STARTUP_BACKGROUND_SERVICE_FAILED", StringComparison.Ordinal));
        Assert.DoesNotContain(harness.Logger.Entries, entry => entry.Level == "Fatal");
    }

    [Theory]
    [InlineData("PLC.Runtime", "STARTUP_PLC_UNREACHABLE")]
    [InlineData("MES.Heartbeat", "STARTUP_MES_UNREACHABLE")]
    [InlineData("Cloud.RuntimeHeartbeat", "STARTUP_CLOUD_UNREACHABLE")]
    public async Task AppLifecycleManager_WhenConnectivityServiceIsUnreachable_ShouldPublishDiagnosticAndStillStartShell(
        string serviceName,
        string expectedCode)
    {
        await using var harness = await AppLifecycleHarness.CreateAsync(
            enabledModules: ["TestPlugin"],
            deviceModuleIds: ["TestPlugin"],
            unreachableServiceName: serviceName);
        var lifecycleProbe = harness.GetTestPluginLifecycleProbe();

        var result = await harness.Manager.StartAsync(TestContext.Current.CancellationToken);

        Assert.True(result.Success, result.Message);
        Assert.Equal(1, harness.BackgroundCoordinator.StartCallCount);
        Assert.Equal(1, lifecycleProbe.StartCount);
        Assert.NotEqual(DateTime.MinValue, harness.StartupDiagnosticsStore.Current.GeneratedAt);
        Assert.Contains(
            harness.StartupDiagnosticsStore.Current.Issues,
            issue => string.Equals(issue.Code, expectedCode, StringComparison.Ordinal));
        Assert.DoesNotContain(harness.Logger.Entries, entry => entry.Level == "Fatal");
    }

    [Fact]
    public async Task AppLifecycleManager_WhenDeviceHasNoIoMappings_ShouldPublishMismatchAndStillStartShell()
    {
        await using var harness = await AppLifecycleHarness.CreateAsync(
            enabledModules: ["TestPlugin"],
            deviceModuleIds: ["TestPlugin"]);
        var device = Assert.Single(await harness.GetNetworkDevicesAsync());
        await harness.ReplaceIoMappingsAsync(device.Id, []);

        var result = await harness.Manager.StartAsync(TestContext.Current.CancellationToken);

        Assert.True(result.Success, result.Message);
        Assert.NotEqual(DateTime.MinValue, harness.StartupDiagnosticsStore.Current.GeneratedAt);
        Assert.Contains(
            harness.StartupDiagnosticsStore.Current.Issues,
            issue => string.Equals(issue.Code, "DEVICE_MODULE_MISMATCH", StringComparison.Ordinal)
                     && issue.Message.Contains("没有配置 IO 映射", StringComparison.Ordinal));
        Assert.DoesNotContain(harness.Logger.Entries, entry => entry.Level == "Fatal");
    }

    [Fact]
    public async Task AppLifecycleManager_WhenModuleHardwareProfileDiagnosticThrows_ShouldPublishFailureAndStillStartShell()
    {
        await using var harness = await AppLifecycleHarness.CreateAsync(
            enabledModules: ["TestPlugin"],
            deviceModuleIds: ["TestPlugin"],
            hardwareProfileDiagnosticThrows: true);

        var result = await harness.Manager.StartAsync(TestContext.Current.CancellationToken);

        Assert.True(result.Success, result.Message);
        Assert.NotEqual(DateTime.MinValue, harness.StartupDiagnosticsStore.Current.GeneratedAt);
        Assert.Contains(
            harness.StartupDiagnosticsStore.Current.Issues,
            issue => string.Equals(issue.Code, "STARTUP_DIAGNOSTIC_VALIDATOR_FAILED", StringComparison.Ordinal)
                     && issue.Message.Contains(nameof(StartupPlcConfigurationValidator), StringComparison.Ordinal));
        Assert.DoesNotContain(harness.Logger.Entries, entry => entry.Level == "Fatal");
    }

    [Fact]
    public async Task AppLifecycleManager_WhenStartupDiagnosticValidatorFails_ShouldStillPublishConfigurationProfile()
    {
        await using var harness = await AppLifecycleHarness.CreateAsync(
            enabledModules: ["TestPlugin"],
            deviceModuleIds: ["TestPlugin"],
            startupDiagnosticException: new InvalidOperationException("legacy signal property missing"));

        var result = await harness.Manager.StartAsync(TestContext.Current.CancellationToken);

        Assert.True(result.Success, result.Message);
        var report = harness.StartupDiagnosticsStore.Current;
        Assert.NotEqual(DateTime.MinValue, report.GeneratedAt);
        Assert.Equal("Production", report.ConfigurationProfile.EnvironmentName);
        Assert.False(string.IsNullOrWhiteSpace(report.ConfigurationProfile.RuntimeDataRoot));
        Assert.Contains(
            report.Issues,
            issue => string.Equals(issue.Code, "STARTUP_DIAGNOSTIC_VALIDATOR_FAILED", StringComparison.OrdinalIgnoreCase)
                     && issue.Message.Contains(nameof(ThrowingStartupAsyncDiagnosticValidator), StringComparison.Ordinal));
    }

    [Fact]
    public async Task AppLifecycleManager_WhenDapperTableInitializerFails_ShouldContinueFollowingTableAndPublishDiagnostic()
    {
        var failing = new ProbeTableInitializer("startup_probe", "FailingTable", throws: true);
        var following = new ProbeTableInitializer("startup_probe", "FollowingTable", throws: false);
        await using var harness = await AppLifecycleHarness.CreateAsync(
            enabledModules: ["TestPlugin"],
            deviceModuleIds: ["TestPlugin"],
            additionalDapperTableInitializers: [failing, following]);

        var result = await harness.Manager.StartAsync(TestContext.Current.CancellationToken);

        Assert.True(result.Success, result.Message);
        Assert.Equal(1, failing.CallCount);
        Assert.Equal(1, following.CallCount);
        Assert.Contains(
            harness.StartupDiagnosticsStore.Current.Issues,
            issue => issue.Code == "STARTUP_DAPPER_TABLE_INITIALIZATION_FAILED" &&
                     issue.Message.Contains("DbName=startup_probe", StringComparison.Ordinal) &&
                     issue.Message.Contains(nameof(ProbeTableInitializer), StringComparison.Ordinal) &&
                     issue.Message.Contains("FailingTable initialization failed", StringComparison.Ordinal));
    }

    [Fact]
    public void EnabledCatalog_ShouldRegisterTestPluginWithoutConflicts()
    {
        var pluginRoot = CreatePluginRuntimeRootFor("TestPlugin");
        try
        {
            var discovery = DiscoverTestPlugins(pluginRoot);
            var activation = CreateShellModuleCatalog().CreateEnabledModules(
                CreateConfiguration(["TestPlugin"]),
                discovery.Modules);
            var modules = activation.Modules;
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
                    CreateConfiguration(["TestPlugin"]),
                    new ModuleViewRegistry(viewRegistry, module.ModuleId),
                    cellDataRegistry,
                    runtimeRegistry,
                    integrationRegistry,
                    moduleParamRegistry));
            }

            Assert.Empty(activation.Issues);
            Assert.Single(modules);
            Assert.Single(cellDataRegistry.GetRegistrations());
            Assert.Single(runtimeRegistry.GetRegistrations());
            Assert.Empty(integrationRegistry.GetCloudUploaders());
            Assert.Empty(integrationRegistry.GetMesUploaders());
            Assert.Empty(moduleParamRegistry.GetRegistrations());
            Assert.NotNull(viewRegistry.GetViewRegistration("TestPlugin.DataView"));
        }
        finally
        {
            DeleteDirectory(pluginRoot);
        }
    }

    [Fact]
    public void HardwareProfileRegistration_WhenProviderModuleIdGetterThrows_ShouldResolveGuardAndDeferFailureToDiagnosticUse()
    {
        var services = new ServiceCollection();
        var builder = new EdgeProcessModuleBuilder(
            "GuardedPlugin",
            "GuardedProcess",
            services,
            CreateConfiguration(),
            new ModuleViewRegistry(new ViewRegistry(), "GuardedPlugin"),
            new CellDataRegistry(new CellDataTypeRegistry()),
            new StationRuntimeRegistry(),
            new ProcessIntegrationRegistry(),
            new ModuleParamRegistry());

        builder.RegisterHardwareProfile<ThrowingIdentityHardwareProfileProvider>();

        using var provider = services.BuildServiceProvider();
        var profile = provider.GetRequiredService<IModuleHardwareProfileProvider>();
        Assert.Equal("GuardedPlugin", profile.ModuleId);
        var exception = Assert.Throws<InvalidOperationException>(() => profile.GetDefaultPlcSettings());
        Assert.Contains("身份无效", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void StartupDiagnosticsHardwareProfileIndex_WhenProfilesDuplicateOrIdentityThrows_ShouldReportAndContinue()
    {
        var issues = new List<StartupDiagnosticIssue>();

        var result = StartupDiagnosticsReportBuilder.BuildHardwareProfileIndex(
            [
                new IdentityOnlyHardwareProfileProvider("DuplicatePlugin"),
                new IdentityOnlyHardwareProfileProvider("DuplicatePlugin"),
                new ThrowingIdentityHardwareProfileProvider()
            ],
            issues);

        Assert.Empty(result);
        Assert.Contains(issues, issue => issue.Code == "HARDWARE_PROFILE_DUPLICATE");
        Assert.Contains(issues, issue => issue.Code == "HARDWARE_PROFILE_IDENTITY_FAILED");
    }

    [Fact]
    public void ModuleHardwareProfileResolver_WhenIdentityGetterThrows_ShouldNotFailConstruction()
    {
        var profile = new ThrowingIdentityHardwareProfileProvider();

        var resolver = new ModuleHardwareProfileResolver([profile]);

        Assert.Same(profile, resolver.Resolve());
    }

    [Fact]
    public void ModuleViewRegistry_ShouldRejectCorePrefixedRoutes()
    {
        var registry = new ModuleViewRegistry(new ViewRegistry(), "TestPlugin");

        var ex = Assert.Throws<InvalidOperationException>(() =>
            registry.RegisterRoute("Core.BadRoute", typeof(object), typeof(object)));

        Assert.Contains("TestPlugin.", ex.Message, StringComparison.Ordinal);
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
    public void ViewRegistry_WhenViewModelFactoryIsRegistered_ShouldKeepFactory()
    {
        var services = new ServiceCollection().BuildServiceProvider();
        var registry = new ViewRegistry();
        registry.RegisterRoute(
            "Plugin.Factory",
            typeof(TestNavigationView),
            typeof(DefaultNavigationViewModel),
            _ => new FactoryNavigationViewModel(),
            cacheView: false);

        var registration = registry.GetViewRegistration("Plugin.Factory");

        Assert.NotNull(registration);
        Assert.Equal(typeof(TestNavigationView), registration.ViewType);
        Assert.Equal(typeof(DefaultNavigationViewModel), registration.ViewModelType);
        Assert.IsType<FactoryNavigationViewModel>(registration.ViewModelFactory!(services));
        Assert.False(registration.CacheView);
    }

    [Fact]
    public void ViewRegistry_WhenViewModelFactoryIsMissing_ShouldKeepViewModelType()
    {
        var registry = new ViewRegistry();
        registry.RegisterRoute(
            "Plugin.Default",
            typeof(TestNavigationView),
            typeof(DefaultNavigationViewModel),
            cacheView: false);

        var registration = registry.GetViewRegistration("Plugin.Default");

        Assert.NotNull(registration);
        Assert.Equal(typeof(TestNavigationView), registration.ViewType);
        Assert.Equal(typeof(DefaultNavigationViewModel), registration.ViewModelType);
        Assert.Null(registration.ViewModelFactory);
        Assert.False(registration.CacheView);
    }

    [Fact]
    public void HostBootstrap_ShouldRegisterDiagnosticsCoreView()
    {
        var services = new ServiceCollection();
        var viewRegistry = new ViewRegistry();
        var configuration = CreateConfiguration();
        var hostRoot = Path.Combine(Path.GetTempPath(), "edge-host-bootstrap-" + Guid.NewGuid().ToString("N"));
        var pluginRoot = CreatePluginRuntimeRootFor("TestPlugin");

        try
        {
            EdgeEnvironmentTestScope.WithDataRootOverride(
                Path.Combine(hostRoot, "data-root"),
                () =>
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
                    var transactionRegistration = services.Last(
                        descriptor => descriptor.ServiceType
                            == typeof(IPlcTaskBindingTransactionService));
                    Assert.Equal(
                        typeof(PlcTaskBindingTransactionService),
                        transactionRegistration.ImplementationType);
                    var mutationGateRegistration = Assert.Single(
                        services,
                        descriptor => descriptor.ServiceType
                            == typeof(IPlcRuntimeConfigurationMutationGate));
                    Assert.Equal(
                        typeof(PlcRuntimeConfigurationMutationGate),
                        mutationGateRegistration.ImplementationType);
                    Assert.Equal(ServiceLifetime.Singleton, mutationGateRegistration.Lifetime);
                });
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

    [Fact]
    public void HostBootstrap_WhenPluginConfigureFails_ShouldDiscardEveryRegistrationAndContinueOtherPlugins()
    {
        var services = new ServiceCollection();
        var viewRegistry = new ViewRegistry();
        var configuration = CreateConfiguration(["TestPlugin"]);
        var hostRoot = Path.Combine(Path.GetTempPath(), "edge-host-plugin-transaction-" + Guid.NewGuid().ToString("N"));
        var pluginRoot = CreatePluginRuntimeRootFor("TestPlugin");

        try
        {
            EdgeEnvironmentTestScope.WithDataRootOverride(
                Path.Combine(hostRoot, "data-root"),
                () =>
                {
                    var runtimePaths = CreateRuntimePaths(hostRoot, configuration);
                    var discovery = DiscoverTestPlugins(pluginRoot);
                    var activation = CreateShellModuleCatalog().CreateEnabledModules(configuration, discovery.Modules);
                    Assert.Empty(activation.Issues);
                    var healthyModule = Assert.Single(activation.Modules);
                    services.AddEdgeHostBootstrap(
                        viewRegistry,
                        configuration,
                        runtimePaths,
                        "Production",
                        discovery.Modules,
                        discovery.Issues,
                        ["BrokenPlugin", "TestPlugin"],
                        [new ThrowingRegistrationModule(), healthyModule]);

                    using var provider = services.BuildServiceProvider();
                    Assert.Null(provider.GetService<FailedPluginServiceMarker>());
                    Assert.DoesNotContain(
                        provider.GetServices<IEdgeProcessModule>(),
                        module => module.ModuleId == "BrokenPlugin");
                    Assert.Contains(
                        provider.GetServices<IEdgeProcessModule>(),
                        module => module.ModuleId == "TestPlugin");

                    var cellData = provider.GetRequiredService<ICellDataRegistry>();
                    var runtimes = provider.GetRequiredService<IStationRuntimeRegistry>();
                    var integrations = provider.GetRequiredService<IProcessIntegrationRegistry>();
                    var parameters = provider.GetRequiredService<IModuleParamRegistry>();
                    Assert.False(cellData.IsRegistered("BrokenProcess"));
                    Assert.False(runtimes.HasFactory("BrokenPlugin"));
                    Assert.False(integrations.HasCloudUploader("BrokenProcess"));
                    Assert.False(integrations.HasMesUploader("BrokenProcess"));
                    Assert.DoesNotContain(parameters.GetRegistrations(), item => item.ModuleId == "BrokenPlugin");
                    Assert.Null(viewRegistry.GetViewRegistration("BrokenPlugin.Main"));
                    Assert.DoesNotContain(viewRegistry.GetAllMenus(), menu => menu.ViewId == "BrokenPlugin.Main");

                    Assert.True(cellData.IsRegistered("TestPlugin"));
                    Assert.True(runtimes.HasFactory("TestPlugin"));
                    Assert.NotNull(viewRegistry.GetViewRegistration("TestPlugin.DataView"));
                    Assert.Contains(
                        provider.GetRequiredService<IReadOnlyCollection<ModuleCatalogIssue>>(),
                        issue => issue.Code == "PLUGIN_CONFIGURE_FAILED"
                                 && issue.ModuleId == "BrokenPlugin"
                                 && issue.Message.Contains("configure exploded", StringComparison.Ordinal));
                });
        }
        finally
        {
            if (Directory.Exists(hostRoot))
                Directory.Delete(hostRoot, recursive: true);

            DeleteDirectory(pluginRoot);
        }
    }

    [Fact]
    public void HostBootstrap_WhenProductionTimeZoneIsInvalid_ShouldUseSafeDefaultAndPublishDiagnostic()
    {
        var hostRoot = Path.Combine(Path.GetTempPath(), "edge-host-time-zone-" + Guid.NewGuid().ToString("N"));
        var configuration = new ConfigurationBuilder()
            .AddConfiguration(CreateConfiguration())
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ProductionTime:TimeZoneId"] = "Invalid/Factory-Time-Zone"
            })
            .Build();
        try
        {
            EdgeEnvironmentTestScope.WithDataRootOverride(
                Path.Combine(hostRoot, "data-root"),
                () =>
                {
                    var services = new ServiceCollection();
                    services.AddEdgeHostBootstrap(
                        new ViewRegistry(),
                        configuration,
                        CreateRuntimePaths(hostRoot, configuration),
                        "Production",
                        [],
                        [],
                        [],
                        []);

                    using var provider = services.BuildServiceProvider();
                    Assert.Equal(
                        "Asia/Shanghai",
                        provider.GetRequiredService<ProductionTimeOptions>().TimeZoneId);
                    Assert.Contains(
                        provider.GetRequiredService<IProductionTimeProvider>().BusinessTimeZone.Id,
                        new[] { "Asia/Shanghai", "China Standard Time" });
                    Assert.Contains(
                        provider.GetRequiredService<IReadOnlyCollection<StartupDiagnosticIssue>>(),
                        issue => issue.Code == "PRODUCTION_TIME_ZONE_INVALID");
                });
        }
        finally
        {
            if (Directory.Exists(hostRoot))
                Directory.Delete(hostRoot, recursive: true);
        }
    }

    [Fact]
    public async Task AppStartupInitializer_WhenEfPragmaOrMigrationFails_ShouldContinueAndPublishDiagnostic()
    {
        var tempDirectory = Path.Combine(Path.GetTempPath(), "edge-startup-ef-failure-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDirectory);
        try
        {
            var blockedDirectory = Path.Combine(tempDirectory, "blocked-db-directory");
            File.WriteAllText(blockedDirectory, "blocks Directory.CreateDirectory");
            var services = new ServiceCollection();
            services.AddEfCorePersistenceInfrastructure(Path.Combine(blockedDirectory, "edge.db"));
            services.AddSingleton(new SqliteConnectionFactory(Path.Combine(tempDirectory, "dapper")));
            using var provider = services.BuildServiceProvider();
            var logger = new SpyLogService();
            var initializer = new AppStartupInitializer(
                provider,
                new NoopDevelopmentSampleInitializer(),
                new NoopCloudSystemSwitchMigration(),
                new NoopConfigSchemaReconciler(),
                logger);

            var issues = await initializer.InitializeAsync(TestContext.Current.CancellationToken);

            Assert.Contains(issues, issue => issue.Code == "STARTUP_EF_MIGRATION_FAILED");
            Assert.Contains(
                logger.Entries,
                entry => entry.Level == "Warn"
                         && entry.Message.Contains("SQLite", StringComparison.Ordinal));
            Assert.Contains(
                logger.Entries,
                entry => entry.Level == "Info"
                         && entry.Message.Contains("Dapper 表初始化完成", StringComparison.Ordinal));
            Assert.DoesNotContain(logger.Entries, entry => entry.Level == "Fatal");
        }
        finally
        {
            if (Directory.Exists(tempDirectory))
                Directory.Delete(tempDirectory, recursive: true);
        }
    }

    [Fact]
    public async Task PlcRuntimeApplyService_ShouldRejectBindingBypassAndReloadOnlyHardwareChanges()
    {
        await using var harness = await AppLifecycleHarness.CreateAsync(
            enabledModules: ["TestPlugin"],
            deviceModuleIds: ["TestPlugin"]);
        var networkDevices =
            harness.GetRequiredService<IReadRepository<NetworkDeviceEntity>>();
        var device = Assert.Single(
            await networkDevices.GetListAsync(
                x => x.DeviceType == DeviceType.PLC,
                TestContext.Current.CancellationToken));
        var binder = new SpyPlcRuntimeTaskBinder();
        var service = new PlcRuntimeApplyService(
            networkDevices,
            binder,
            harness.PlcManager,
            harness.Logger);

        var bypass = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.ApplyDeviceRuntimeAsync(
                device.Id,
                PlcRuntimeApplyReasons.TaskBindingSave,
                TestContext.Current.CancellationToken));

        Assert.Contains("一体化事务命令", bypass.Message, StringComparison.Ordinal);
        Assert.Empty(binder.DeviceCalls);
        Assert.Empty(harness.PlcManager.ReloadedDeviceNames);

        await service.ApplyDeviceRuntimeAsync(
            device.Id,
            PlcRuntimeApplyReasons.HardwareOrIoMappingSave,
            TestContext.Current.CancellationToken);

        Assert.Collection(
            binder.DeviceCalls,
            call =>
            {
                Assert.Equal(device.Id, call.NetworkDeviceId);
                Assert.False(call.ApplyToRunningDevice);
            });
        Assert.Equal([device.DeviceName], harness.PlcManager.ReloadedDeviceNames);
    }

    private static IConfiguration CreateConfiguration(
        string[]? enabledModules = null,
        string environmentName = "Production",
        bool developmentSamplesEnabled = false,
        string? clientCode = "CLIENT-01",
        string? bootstrapSecret = "bootstrap-secret",
        string? omittedCloudPathKey = null,
        string? recipeByDeviceTemplate = null,
        string? deviceInstancePath = null)
    {
        var settings = new Dictionary<string, string?>
        {
            ["CloudApi:BaseUrl"] = "https://cloud.test",
            ["CloudApi:Paths:DeviceInstance"] = deviceInstancePath ?? "/api/v1/bootstrap/device-instance",
            ["CloudApi:Paths:BootstrapRefresh"] = "/api/v1/bootstrap/edge-refresh",
            ["CloudApi:Paths:IdentityDeviceLogin"] = "/api/v1/bootstrap/edge-login",
            ["CloudApi:Paths:HumanIdentityRefresh"] = "/api/v1/human/identity/refresh",
            ["CloudApi:Paths:HumanSessionValidation"] = "/api/v1/human/devices/select",
            ["CloudApi:Paths:DeviceLog"] = "/api/v1/edge/device-logs",
            ["CloudApi:Paths:PassStationBatchTemplate"] = "/api/v1/edge/pass-stations/{typeKey}/batch",
            ["CloudApi:Paths:CapacityHourly"] = "/api/v1/edge/capacity/hourly",
            ["CloudApi:Paths:CapacitySummary"] = "/api/v1/edge/capacity/summary",
            ["CloudApi:Paths:CapacitySummaryRange"] = "/api/v1/edge/capacity/summary/range",
            ["CloudApi:Paths:RecipeByDeviceTemplate"] = recipeByDeviceTemplate ?? "/api/v1/edge/recipes/device/{deviceId}",
            ["CloudApi:Paths:ClientReleaseCatalogTemplate"] = "/api/v1/edge/client-releases/device/{deviceId}/catalog",
            ["CloudApi:Paths:ClientVersionReport"] = "/api/v1/edge/client-releases/version-reports",
            ["CloudApi:Paths:RuntimeHeartbeat"] = "/api/v1/edge/runtime-heartbeats",
            ["CloudApi:Paths:EdgeHostPlcRuntimeStates"] = "/api/v1/edge/edge-hosts/plc-runtime-states",
            ["Shell:Environment"] = environmentName,
            ["DevelopmentSamples:Enabled"] = developmentSamplesEnabled.ToString(),
            ["DevelopmentSamples:SampleBarcode"] = "ST-DEV-0001",
            ["DevelopmentSamples:SampleLayerCount"] = "12"
        };

        if (clientCode is not null)
        {
            settings["CloudApi:ClientCode"] = clientCode;
        }

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
        => new DirectoryModuleCatalog(
            new ModulePluginLoader(new ModulePluginAssemblyResolver()),
            new ModulePluginCompatibilityPolicy());

    private sealed class TestHostEnvironment(string environmentName) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = environmentName;

        public string ApplicationName { get; set; } = "IIoT.Edge.Startup.IntegrationTests";

        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;

        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }

    private static ModuleCatalogDiscoveryResult DiscoverTestPlugins(string pluginRoot)
    {
        return CreateShellModuleCatalog().DiscoverModules(pluginRoot);
    }

    private static string CreatePluginRuntimeRootFor(params string[] moduleIds)
        => CreatePluginRuntimeRoot(targetRoot: null, moduleIds);

    private static string CreatePluginRuntimeRoot(string? targetRoot = null, string[]? moduleIds = null)
    {
        var pluginRoot = targetRoot ?? Path.Combine(Path.GetTempPath(), "edge-shell-plugin-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(pluginRoot);

        var runtimeModulesRoot = CreateShellModuleCatalog().GetPluginRootPath(AppContext.BaseDirectory);
        foreach (var moduleId in moduleIds ?? ["TestPlugin"])
        {
            var sourceModuleDirectory = Path.Combine(runtimeModulesRoot, moduleId);
            if (!Directory.Exists(sourceModuleDirectory))
            {
                throw new DirectoryNotFoundException(
                    $"Staged shell-test plugin directory was not found: '{sourceModuleDirectory}'. " +
                    "Build IIoT.Edge.Startup.IntegrationTests for the current configuration before running tests.");
            }

            var targetModuleDirectory = Path.Combine(pluginRoot, moduleId);
            CopyDirectory(sourceModuleDirectory, targetModuleDirectory);
        }

        return pluginRoot;
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

    private sealed class TestPluginLifecycleProbeView(object instance)
    {
        public int StartCount => ReadCount(nameof(StartCount));
        public int StopCount => ReadCount(nameof(StopCount));
        public int DisposeCount => ReadCount(nameof(DisposeCount));

        private int ReadCount(string propertyName)
            => (int)(instance.GetType().GetProperty(propertyName)?.GetValue(instance)
                ?? throw new InvalidOperationException(
                    $"Staged TestPlugin probe property '{propertyName}' is missing."));
    }

    private sealed class AppLifecycleHarness : IAsyncDisposable
    {
        private readonly ServiceProvider _serviceProvider;
        private readonly string _tempDirectory;
        private int _disposed;

        private AppLifecycleHarness(
            ServiceProvider serviceProvider,
            string tempDirectory,
            AppLifecycleManager manager,
            SpyPlcConnectionManager plcManager,
            SpyProductionContextStore contextStore,
            SpyRecipeService recipeService,
            SpyBackgroundServiceCoordinator backgroundCoordinator,
            SpyLogService logger,
            IStartupDiagnosticsStore startupDiagnosticsStore)
        {
            _serviceProvider = serviceProvider;
            _tempDirectory = tempDirectory;
            Manager = manager;
            PlcManager = plcManager;
            ContextStore = contextStore;
            RecipeService = recipeService;
            BackgroundCoordinator = backgroundCoordinator;
            Logger = logger;
            StartupDiagnosticsStore = startupDiagnosticsStore;
        }

        public AppLifecycleManager Manager { get; }

        public SpyPlcConnectionManager PlcManager { get; }

        public SpyProductionContextStore ContextStore { get; }

        public SpyRecipeService RecipeService { get; }

        public SpyBackgroundServiceCoordinator BackgroundCoordinator { get; }

        public SpyLogService Logger { get; }

        public IStartupDiagnosticsStore StartupDiagnosticsStore { get; }

        public T GetRequiredService<T>()
            where T : notnull
            => _serviceProvider.GetRequiredService<T>();

        public TestPluginLifecycleProbeView GetTestPluginLifecycleProbe()
        {
            const string probeTypeName = "IIoT.Edge.TestPlugin.TestPluginLifecycleProbe";
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                var probeType = assembly.GetType(probeTypeName, throwOnError: false);
                if (probeType is null || _serviceProvider.GetService(probeType) is not { } probe)
                {
                    continue;
                }

                return new TestPluginLifecycleProbeView(probe);
            }

            throw new InvalidOperationException($"Staged TestPlugin lifecycle probe '{probeTypeName}' was not registered.");
        }

        public async Task<List<NetworkDeviceEntity>> GetNetworkDevicesAsync()
            => await _serviceProvider
                .GetRequiredService<IReadRepository<NetworkDeviceEntity>>()
                .GetListAsync(x => x.DeviceType == DeviceType.PLC, includes: null, cancellationToken: default)
                .ConfigureAwait(false);

        public async Task<List<IoMappingEntity>> GetIoMappingsAsync(int networkDeviceId)
            => await _serviceProvider
                .GetRequiredService<IReadRepository<IoMappingEntity>>()
                .GetListAsync(x => x.NetworkDeviceId == networkDeviceId, includes: null, cancellationToken: default)
                .ConfigureAwait(false);

        public async Task ReplaceIoMappingsAsync(int networkDeviceId, IReadOnlyCollection<IoMappingEntity> mappings)
        {
            await using var unitOfWork = await _serviceProvider
                .GetRequiredService<IEdgeUnitOfWorkFactory>()
                .BeginAsync()
                .ConfigureAwait(false);
            var repo = unitOfWork.Repository<IoMappingEntity>();
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

            await unitOfWork.CommitAsync().ConfigureAwait(false);
        }

        public async Task SaveTaskBindingsAsync(
            int networkDeviceId,
            string moduleId,
            IReadOnlyDictionary<string, bool> taskStates)
        {
            var persistence = _serviceProvider
                .GetRequiredService<IPlcTaskBindingPersistenceTransaction>();
            var preparation = await persistence
                .PrepareAsync(networkDeviceId, moduleId, taskStates)
                .ConfigureAwait(false);
            await persistence.CommitAsync(preparation).ConfigureAwait(false);
        }

        public static async Task<AppLifecycleHarness> CreateAsync(
            string[] enabledModules,
            string[] deviceModuleIds,
            string environmentName = "Production",
            bool developmentSamplesEnabled = false,
            string? clientCode = "CLIENT-01",
            string? bootstrapSecret = "bootstrap-secret",
            string? omittedCloudPathKey = null,
            string? recipeByDeviceTemplate = null,
            string? deviceInstancePath = null,
            bool systemCloudEnabled = true,
            Exception? backgroundStartException = null,
            Exception? startupDiagnosticException = null,
            string? unreachableServiceName = null,
            bool hardwareProfileDiagnosticThrows = false,
            IReadOnlyCollection<ITableInitializer>? additionalDapperTableInitializers = null,
            Exception? contextSaveException = null)
        {
            var tempDirectory = Path.Combine(Path.GetTempPath(), "edge-shell-tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDirectory);

            var configuration = CreateConfiguration(
                enabledModules: enabledModules,
                environmentName: environmentName,
                developmentSamplesEnabled: developmentSamplesEnabled,
                clientCode: clientCode,
                bootstrapSecret: bootstrapSecret,
                omittedCloudPathKey: omittedCloudPathKey,
                recipeByDeviceTemplate: recipeByDeviceTemplate,
                deviceInstancePath: deviceInstancePath);
            EdgeRuntimePaths? runtimePaths = null;
            EdgeEnvironmentTestScope.WithDataRootOverride(
                Path.Combine(tempDirectory, "data-root"),
                () => runtimePaths = CreateRuntimePaths(tempDirectory, configuration));

            var services = new ServiceCollection();
            services.AddSingleton(runtimePaths!);
            services.AddEfCorePersistenceInfrastructure(Path.Combine(runtimePaths!.DatabaseDirectory, "edge.db"));
            services.AddDapperPersistenceInfrastructure(runtimePaths.DatabaseDirectory);
            foreach (var initializer in additionalDapperTableInitializers ?? [])
                services.AddSingleton(initializer);
            var pluginRoot = CreatePluginRuntimeRoot(Path.Combine(tempDirectory, "Modules"), enabledModules);

            var shiftConfig = new ShiftConfig
            {
                DayStart = "08:00",
                DayEnd = "20:00"
            };

            var plcManager = new SpyPlcConnectionManager();
            var plcRuntimeTaskController = new PlcRuntimeTaskController(new PlcRuntimeRegistry());
            var contextStore = new SpyProductionContextStore(contextSaveException);
            var backgroundCoordinator = new SpyBackgroundServiceCoordinator(backgroundStartException);
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
            services.AddTransient<IPlcTaskBindingPersistenceTransaction, PlcTaskBindingService>();
            if (!string.IsNullOrWhiteSpace(unreachableServiceName))
            {
                services.AddSingleton<IManagedBackgroundService>(new DelegatingBackgroundService(
                    unreachableServiceName,
                    _ => throw new IOException($"{unreachableServiceName} endpoint unreachable")));
            }

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
            backgroundCoordinator.Attach(
                serviceProvider.GetServices<IManagedBackgroundService>(),
                logger);
            serviceProvider.ApplyMigrations();

            await SeedDevicesAsync(serviceProvider, deviceModuleIds).ConfigureAwait(false);

            var diagnosticsStore = serviceProvider.GetRequiredService<IStartupDiagnosticsStore>();
            var developmentSampleInitializer = serviceProvider.GetRequiredService<IDevelopmentSampleInitializer>();
            var networkDevices = serviceProvider.GetRequiredService<IReadRepository<NetworkDeviceEntity>>();
            var ioMappings = serviceProvider.GetRequiredService<IReadRepository<IoMappingEntity>>();
            var configurationProfileBuilder = new StartupConfigurationProfileBuilder(configuration, runtimePaths);
            var syncValidators = new IStartupDiagnosticValidator[]
            {
                new StartupAppSettingsValidator(configuration, shiftConfig),
                new StartupModuleRegistrationValidator(cellDataRegistry, runtimeRegistry, integrationRegistry)
            };
            IStartupAsyncDiagnosticValidator[] asyncValidators = startupDiagnosticException is null
                ? [new StartupPlcConfigurationValidator(ioMappings, cellDataRegistry, runtimeRegistry)]
                :
                [
                    new StartupPlcConfigurationValidator(ioMappings, cellDataRegistry, runtimeRegistry),
                    new ThrowingStartupAsyncDiagnosticValidator(startupDiagnosticException)
                ];
            var hardwareProfiles = serviceProvider.GetServices<IModuleHardwareProfileProvider>().ToArray();
            IEnumerable<IModuleHardwareProfileProvider> diagnosticHardwareProfiles = hardwareProfileDiagnosticThrows
                ? hardwareProfiles.Select(static profile => (IModuleHardwareProfileProvider)new ThrowingHardwareProfileProvider(profile))
                : hardwareProfiles;
            var diagnosticsReportBuilder = new StartupDiagnosticsReportBuilder(
                networkDevices,
                new StartupPluginLifecycleSnapshotBuilder(),
                discovery.Modules,
                [.. discovery.Issues, .. activation.Issues],
                [],
                activation.EnabledModuleIds,
                activation.Modules,
                diagnosticHardwareProfiles,
                syncValidators,
                asyncValidators,
                configurationProfileBuilder,
                new StartupModuleRegistrationSnapshotBuilder(cellDataRegistry, runtimeRegistry, integrationRegistry),
                new FixedRuntimeConfigService(systemCloudEnabled));
            var manager = new AppLifecycleManager(
                new AppStartupInitializer(
                    serviceProvider,
                    developmentSampleInitializer,
                    new NoopCloudSystemSwitchMigration(),
                    new NoopConfigSchemaReconciler(),
                    logger),
                diagnosticsReportBuilder,
                diagnosticsStore,
                new PlcRuntimeTaskBinder(
                    serviceProvider,
                    networkDevices,
                    ioMappings,
                    runtimeRegistry,
                    serviceProvider.GetRequiredService<IPlcTaskBindingService>(),
                    serviceProvider.GetRequiredService<IProductionContextSignalBindingStore>(),
                    plcRuntimeTaskController,
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
                recipeService,
                backgroundCoordinator,
                logger,
                diagnosticsStore);
        }

        public async ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
            {
                return;
            }

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
            var unitOfWorkFactory = serviceProvider.GetRequiredService<IEdgeUnitOfWorkFactory>();
            var hardwareProfiles = serviceProvider.GetServices<IModuleHardwareProfileProvider>()
                .ToDictionary(x => x.ModuleId, StringComparer.OrdinalIgnoreCase);

            for (var index = 0; index < moduleIds.Count; index++)
            {
                await using var unitOfWork = await unitOfWorkFactory.BeginAsync().ConfigureAwait(false);
                var networkRepo = unitOfWork.Repository<NetworkDeviceEntity>();
                var ioRepo = unitOfWork.Repository<IoMappingEntity>();
                var deviceName = $"PLC-{(char)('A' + index)}";
                var device = NetworkDeviceEntity.Create(deviceName, DeviceType.PLC, "127.0.0.1", 102 + index);
                device.UpdateDeviceModel(PlcType.S7.ToString());
                device.UpdateEndpoint("127.0.0.1", 102 + index, null, 3000);
                device.Enable();

                networkRepo.Add(device);
                await unitOfWork.FlushAsync().ConfigureAwait(false);

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

                await unitOfWork.CommitAsync().ConfigureAwait(false);
            }
        }
    }

    private sealed class SpyPlcConnectionManager : IPlcConnectionManager
    {
        public sealed record RuntimeFault(int NetworkDeviceId, string DeviceName, string Error);

        public Dictionary<string, Func<IPlcBuffer, ProductionContext, List<IPlcTask>>> RegisteredFactories { get; }
            = new(StringComparer.OrdinalIgnoreCase);

        public List<RuntimeFault> RuntimeFaults { get; } = [];

        public List<string> ReloadedDeviceNames { get; } = [];

        public Task InitializeAsync(CancellationToken ct = default) => Task.CompletedTask;

        public Task StopAsync(CancellationToken ct = default) => Task.CompletedTask;

        public Task ReloadAsync(string deviceName, CancellationToken ct = default)
        {
            ReloadedDeviceNames.Add(deviceName);
            return Task.CompletedTask;
        }

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

    private sealed class SpyPlcRuntimeTaskBinder : IPlcRuntimeTaskBinder
    {
        public sealed record DeviceCall(
            int NetworkDeviceId,
            bool ApplyToRunningDevice);

        public List<DeviceCall> DeviceCalls { get; } = [];

        public Task BindAsync(CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task<PlcRuntimeTaskApplyResult> BindDeviceAsync(
            int networkDeviceId,
            bool applyToRunningDevice,
            CancellationToken cancellationToken = default)
        {
            DeviceCalls.Add(new DeviceCall(networkDeviceId, applyToRunningDevice));
            return Task.FromResult(
                new PlcRuntimeTaskApplyResult(
                    applyToRunningDevice
                        ? PlcRuntimeTaskApplyState.Applied
                        : PlcRuntimeTaskApplyState.WaitingForRuntime,
                    ["Task.MG1", "Task.MG2"]));
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

    private sealed class SpyProductionContextStore(Exception? saveException = null) : IProductionContextStore
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

        public void SaveToFile()
        {
            SaveCallCount++;
            if (saveException is not null)
                throw saveException;
        }

        public Task StartAutoSaveAsync(CancellationToken ct, int intervalSeconds = 30) => Task.CompletedTask;
    }

    private sealed class SpyBackgroundServiceCoordinator(Exception? startException = null) : IBackgroundServiceCoordinator
    {
        private IBackgroundServiceCoordinator? _inner;

        public int StartCallCount { get; private set; }

        public int StopCallCount { get; private set; }

        public void Attach(IEnumerable<IManagedBackgroundService> services, ILogService logger)
            => _inner = new BackgroundServiceCoordinator(services, logger);

        public async Task StartAsync(CancellationToken cancellationToken = default)
        {
            StartCallCount++;
            if (startException is not null)
            {
                throw startException;
            }

            await (_inner ?? throw new InvalidOperationException("Background service coordinator is not attached."))
                .StartAsync(cancellationToken);
        }

        public async Task StopAsync(CancellationToken cancellationToken = default)
        {
            StopCallCount++;
            if (_inner is not null)
            {
                await _inner.StopAsync(cancellationToken);
            }
        }
    }

    private sealed class RecordingManagedBackgroundService(
        string serviceName,
        ICollection<string> events,
        Func<CancellationToken, Task>? start = null) : IManagedBackgroundService
    {
        public string ServiceName => serviceName;

        public int StartCallCount { get; private set; }

        public int StopCallCount { get; private set; }

        public async Task StartAsync(CancellationToken cancellationToken)
        {
            StartCallCount++;
            events.Add($"start:{ServiceName}");
            if (start is not null)
            {
                await start(cancellationToken);
            }
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            StopCallCount++;
            events.Add($"stop:{ServiceName}");
            return Task.CompletedTask;
        }
    }

    private sealed class SpyRecipeService : IRecipeService
    {
        public int SaveCallCount { get; private set; }

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

        public Task<bool> PullFromCloudAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(false);

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
            SaveCallCount++;
        }
    }

    private sealed class TestNavigationView
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

    private sealed class NoopConfigSchemaReconciler : IConfigSchemaReconciler
    {
        public Task ReconcileAsync(CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }

    private sealed class NoopDevelopmentSampleInitializer : IDevelopmentSampleInitializer
    {
        public Task EnsureConfigurationSamplesAsync(CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task EnsureRuntimeSamplesAsync(CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }

    private sealed class NoopCloudSystemSwitchMigration : ICloudSystemSwitchMigration
    {
        public Task<bool> MigrateAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(true);
    }

    private sealed class ProbeTableInitializer(string dbName, string tableName, bool throws)
        : ITableInitializer
    {
        public string DbName { get; } = dbName;

        public int CallCount { get; private set; }

        public Task InitializeTableAsync(System.Data.IDbConnection connection)
        {
            CallCount++;
            if (throws)
                throw new IOException($"{tableName} initialization failed");

            return Task.CompletedTask;
        }
    }

    private sealed class FixedRuntimeConfigService(bool systemCloudEnabled) : ILocalSystemRuntimeConfigService
    {
        public SystemRuntimeConfigSnapshot Current { get; } = SystemRuntimeConfigSnapshot.Default with
        {
            SystemCloudEnabled = systemCloudEnabled
        };

        public Task EnsureInitializedAsync(CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }

    private sealed class DiagnosticProcessModule(
        string moduleId,
        string processType,
        bool requiresCloud,
        bool requiresMes) : IEdgeProcessModule
    {
        public string ModuleId { get; } = moduleId;

        public string ProcessType { get; } = processType;

        public string DisplayName => ModuleId;

        public bool RequiresCloudUploader { get; } = requiresCloud;

        public bool RequiresMesUploader { get; } = requiresMes;

        public void Configure(IEdgeProcessModuleBuilder builder)
        {
        }
    }

    private sealed class ThrowingRegistrationModule : IEdgeProcessModule
    {
        public string ModuleId => "BrokenPlugin";

        public string ProcessType => "BrokenProcess";

        public string DisplayName => "Broken Plugin";

        public void Configure(IEdgeProcessModuleBuilder builder)
        {
            builder.Services.AddSingleton<FailedPluginServiceMarker>();
            builder.RegisterCellData(typeof(FailedPluginCellData));
            builder.RegisterRuntimeFactory(new DiagnosticRuntimeFactory(ModuleId));
            builder.RegisterCloudUploader(ProcessUploadMode.Single);
            builder.RegisterMesUploader(ProcessUploadMode.Batch);
            builder.RegisterParameters<FailedPluginParam, FailedPluginParam, FailedPluginParam>();
            builder.RegisterRoute("BrokenPlugin.Main", typeof(object), typeof(object));
            builder.RegisterMenu(new ModuleMenuDescriptor
            {
                Title = "Broken Plugin",
                ViewId = "BrokenPlugin.Main"
            });
            throw new InvalidOperationException("configure exploded");
        }
    }

    private sealed class FailedPluginServiceMarker;

    private enum FailedPluginParam
    {
        Value
    }

    private sealed class FailedPluginCellData : CellDataBase
    {
        public override string ProcessType => "BrokenProcess";
    }

    private sealed class DiagnosticRuntimeFactory(string moduleId) : IStationRuntimeFactory
    {
        public string ModuleId { get; } = moduleId;

        public IReadOnlyCollection<TaskCandidate> GetTaskCandidates() => [];

        public List<IPlcTask> CreateTasks(
            IServiceProvider serviceProvider,
            IPlcBuffer buffer,
            ProductionContext context,
            IReadOnlySet<string> enabledTaskKeys)
            => [];
    }

    private sealed class DiagnosticCellData : CellDataBase
    {
        public override string ProcessType => "Diagnostic";
    }

    private sealed class ThrowingStartupAsyncDiagnosticValidator(Exception exception) : IStartupAsyncDiagnosticValidator
    {
        public Task ValidateAsync(
            StartupValidationContext context,
            List<StartupDiagnosticIssue> issues,
            CancellationToken cancellationToken)
            => throw exception;
    }

    private sealed class ThrowingHardwareProfileProvider(IModuleHardwareProfileProvider inner)
        : IModuleHardwareProfileProvider
    {
        public string ModuleId => inner.ModuleId;

        public ModulePlcDefaults GetDefaultPlcSettings() => inner.GetDefaultPlcSettings();

        public PlcIoRuntimePolicy GetIoRuntimePolicy() => inner.GetIoRuntimePolicy();

        public IReadOnlyList<ModuleIoTemplateEntry> GetDefaultIoTemplate() => inner.GetDefaultIoTemplate();

        public IReadOnlyList<ModuleIoTemplateEntry> GetIoMappingCandidates() => inner.GetIoMappingCandidates();

        public ModuleIoTemplateEntry ResolveIoTemplateForDevice(
            string deviceName,
            ModuleIoTemplateEntry template)
            => inner.ResolveIoTemplateForDevice(deviceName, template);

        public ModuleHardwareValidationResult ValidatePlcConfiguration(
            string deviceName,
            string? deviceModel,
            IReadOnlyCollection<ModuleIoSnapshot> mappings)
            => throw new InvalidOperationException("module hardware profile diagnostic failed");
    }

    private sealed class IdentityOnlyHardwareProfileProvider(string moduleId)
        : IModuleHardwareProfileProvider
    {
        public string ModuleId => moduleId;

        public ModulePlcDefaults GetDefaultPlcSettings() => throw new NotSupportedException();

        public PlcIoRuntimePolicy GetIoRuntimePolicy() => throw new NotSupportedException();

        public IReadOnlyList<ModuleIoTemplateEntry> GetDefaultIoTemplate() => throw new NotSupportedException();

        public IReadOnlyList<ModuleIoTemplateEntry> GetIoMappingCandidates() => throw new NotSupportedException();

        public ModuleIoTemplateEntry ResolveIoTemplateForDevice(
            string deviceName,
            ModuleIoTemplateEntry template) => throw new NotSupportedException();

        public ModuleHardwareValidationResult ValidatePlcConfiguration(
            string deviceName,
            string? deviceModel,
            IReadOnlyCollection<ModuleIoSnapshot> mappings) => throw new NotSupportedException();
    }

    private sealed class ThrowingIdentityHardwareProfileProvider : IModuleHardwareProfileProvider
    {
        public string ModuleId => throw new InvalidOperationException("profile identity getter failed");

        public ModulePlcDefaults GetDefaultPlcSettings() => throw new NotSupportedException();

        public PlcIoRuntimePolicy GetIoRuntimePolicy() => throw new NotSupportedException();

        public IReadOnlyList<ModuleIoTemplateEntry> GetDefaultIoTemplate() => throw new NotSupportedException();

        public IReadOnlyList<ModuleIoTemplateEntry> GetIoMappingCandidates() => throw new NotSupportedException();

        public ModuleIoTemplateEntry ResolveIoTemplateForDevice(
            string deviceName,
            ModuleIoTemplateEntry template) => throw new NotSupportedException();

        public ModuleHardwareValidationResult ValidatePlcConfiguration(
            string deviceName,
            string? deviceModel,
            IReadOnlyCollection<ModuleIoSnapshot> mappings) => throw new NotSupportedException();
    }
}
