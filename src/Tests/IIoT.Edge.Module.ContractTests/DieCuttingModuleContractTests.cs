using IIoT.Edge.Application.Abstractions.Cloud;
using IIoT.Edge.Application.Abstractions.Config;
using IIoT.Edge.Application.Abstractions.DataPipeline;
using IIoT.Edge.Application.Abstractions.Device;
using IIoT.Edge.Application.Abstractions.Logging;
using IIoT.Edge.Application.Abstractions.Mes;
using IIoT.Edge.Application.Abstractions.Modules;
using IIoT.Edge.Application.Abstractions.Plc;
using IIoT.Edge.Application.Abstractions.Plc.Store;
using IIoT.Edge.Application.Abstractions.Shared;
using IIoT.Edge.Application.Abstractions.Time;
using IIoT.Edge.Application.Features.Hardware.PlcTaskBindings;
using IIoT.Edge.Application.Features.Production.Planning;
using IIoT.Edge.Application.Modules;
using IIoT.Edge.Application.Modules.Hardware;
using IIoT.Edge.Domain.Hardware.Aggregates;
using IIoT.Edge.Infrastructure.DeviceComm.Plc.Store;
using IIoT.Edge.Module.DieCutting;
using IIoT.Edge.Module.DieCutting.Config;
using IIoT.Edge.Module.DieCutting.Config.Io;
using IIoT.Edge.Module.DieCutting.Config.Parameters;
using IIoT.Edge.Module.DieCutting.Mes;
using IIoT.Edge.Module.DieCutting.Payload;
using IIoT.Edge.Module.DieCutting.Presentation;
using IIoT.Edge.Module.DieCutting.Presentation.Views;
using IIoT.Edge.Module.DieCutting.Production;
using IIoT.Edge.Module.DieCutting.Samples;
using IIoT.Edge.SharedKernel.Context;
using IIoT.Edge.SharedKernel.DataPipeline;
using IIoT.Edge.SharedKernel.DataPipeline.CellData;
using IIoT.Edge.SharedKernel.Domain;
using IIoT.Edge.SharedKernel.Enums;
using IIoT.Edge.SharedKernel.Repository;
using IIoT.Edge.SharedKernel.Specification;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using System.Linq.Expressions;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AnodeModule = IIoT.Edge.Module.DieCuttingAnode.DependencyInjection;
using CathodeModule = IIoT.Edge.Module.DieCuttingCathode.DependencyInjection;

namespace IIoT.Edge.Module.ContractTests;

public sealed class DieCuttingAnodeModuleContractTests : DieCuttingModuleContractTestsBase<AnodeModule>
{
    protected override string ExpectedModuleId => AnodeModule.ModuleKey;
    protected override string ExpectedDisplayName => "负极模切";
    protected override string ExpectedConfigFileName => "diecutting-anode.module.json";
    protected override string ExpectedEntryType => "IIoT.Edge.Module.DieCuttingAnode.DependencyInjection";
    protected override string ExpectedMachineProfileFileName => "appsettings.machine.DieCuttingAnodeLine.json";
    protected override string ExpectedFirstDevice => "P1-AP01";
    protected override string ExpectedLastDevice => "P1-AP12";
    protected override string ExpectedFirstIpAddress => "plc-ap-01.local";
    protected override string ExpectedLastIpAddress => "plc-ap-12.local";
    protected override string ExpectedUpperComputerNo => "P1-APUC";
    protected override string ExpectedOperationCode => "AP";
    protected override string ExpectedMesBaseUrl => string.Empty;
}

public sealed class DieCuttingCathodeModuleContractTests : DieCuttingModuleContractTestsBase<CathodeModule>
{
    protected override string ExpectedModuleId => CathodeModule.ModuleKey;
    protected override string ExpectedDisplayName => "正极模切";
    protected override string ExpectedConfigFileName => "diecutting-cathode.module.json";
    protected override string ExpectedEntryType => "IIoT.Edge.Module.DieCuttingCathode.DependencyInjection";
    protected override string ExpectedMachineProfileFileName => "appsettings.machine.DieCuttingCathodeLine.json";
    protected override string ExpectedFirstDevice => "P2-CP01";
    protected override string ExpectedLastDevice => "P2-CP12";
    protected override string ExpectedFirstIpAddress => "plc-cp-01.local";
    protected override string ExpectedLastIpAddress => "plc-cp-12.local";
    protected override string ExpectedUpperComputerNo => "P2-CPUC";
    protected override string ExpectedOperationCode => "CP";
    protected override string ExpectedMesBaseUrl => string.Empty;
}

public abstract class DieCuttingModuleContractTestsBase<TModule> : ModuleContractTestBase<TModule>
    where TModule : IEdgeProcessModule, new()
{
    private const string ExpectedMesSignSecret = "test-mes-hmac-secret";
    private const string TestMesBaseUrl = "http://mes.example.test:8080";

    protected abstract string ExpectedModuleId { get; }
    protected abstract string ExpectedDisplayName { get; }
    protected abstract string ExpectedConfigFileName { get; }
    protected abstract string ExpectedEntryType { get; }
    protected abstract string ExpectedMachineProfileFileName { get; }
    protected abstract string ExpectedFirstDevice { get; }
    protected abstract string ExpectedLastDevice { get; }
    protected abstract string ExpectedFirstIpAddress { get; }
    protected abstract string ExpectedLastIpAddress { get; }
    protected abstract string ExpectedUpperComputerNo { get; }
    protected abstract string ExpectedOperationCode { get; }
    protected abstract string ExpectedMesBaseUrl { get; }

    protected override bool RequiresHardwareProfile => true;
    protected override bool RequiresMesUploader => true;
    protected override int ExpectedRuntimeTaskCount => 2;
    protected override int MinimumRouteCount => 6;

    protected override ProductionContext CreateRuntimeContext()
        => new DieCuttingContext { DeviceName = ExpectedFirstDevice };

    protected override void ConfigureRuntimeServices(IServiceCollection services)
    {
        AddDefaultRuntimeServices(services);
        services.AddSingleton<IMesUploadDiagnosticsStore, ContractMesUploadDiagnosticsStore>();
        services.AddSingleton<ICloudUploadDiagnosticsStore, ContractCloudUploadDiagnosticsStore>();
        services.AddSingleton<IMesHttpClient, CapturingMesHttpClient>();
        services.AddSingleton<IMesEndpointProvider, ContractMesEndpointProvider>();
        services.AddSingleton<IPlcConnectionManager, ContractPlcConnectionManager>();
        services.AddSingleton<IModuleParamRoleProvider>(new ContractModuleParamRoleProvider(ExpectedFirstDevice));
        services.AddSingleton<MesRequestExecutor>();
        services.AddSingleton<IDieCuttingMesScenarioChannel>(new ContractDieCuttingMesChannel(ExpectedModuleId));
        var parameters = new ContractDieCuttingModuleParamProvider(
            ExpectedModuleId,
            ExpectedUpperComputerNo,
            ExpectedOperationCode);
        services.AddSingleton<IModuleParamProvider<DieCuttingParams.Mes, DieCuttingParams.Cloud, DieCuttingParams.Business>>(parameters);
        services.AddSingleton<ICloudExecutionPolicy>(parameters);
        services.AddSingleton(Options.Create(new DieCuttingModuleOptions()));
    }

    [Fact]
    public void RegisterServices_ShouldRegisterDevelopmentSampleContributor()
    {
        var result = new ModuleContractFixture().RegisterModule(new TModule());

        Assert.Contains(
            result.Services,
            descriptor => descriptor.ServiceType == typeof(IDevelopmentSampleContributor)
                          && descriptor.ImplementationType == typeof(DieCuttingDevelopmentSampleContributor));
    }

    [Fact]
    public async Task DevelopmentSampleContributor_WhenExistingSecondDeviceHasLegacyMappings_ShouldPatchBatchAddressAndSeedMissingRows()
    {
        var configuration = CreateEnabledSeedConfiguration(resetBeforeImport: false);
        var result = new ModuleContractFixture().RegisterModule(new TModule(), configuration);
        var networkDevices = new InMemoryRepository<NetworkDeviceEntity>();
        var ioMappings = new InMemoryRepository<IoMappingEntity>();
        var taskBindings = new InMemoryRepository<PlcTaskBindingEntity>();
        var existingDevice = NetworkDeviceEntity.Create(
            ExpectedDeviceName(2),
            DeviceType.PLC,
            ExpectedIpAddress(2),
            65530);
        existingDevice.UpdateDeviceModel("Mc");
        existingDevice.SetEnabled(false);
        existingDevice.UpdateRemark(ExpectedDisplayName);
        networkDevices.Add(existingDevice);
        ioMappings.Add(CreateIoMapping(
            existingDevice.Id,
            "DieCutting.BatchNumber",
            "R9660",
            addressCount: 8,
            dataType: "Ascii"));
        ioMappings.Add(CreateIoMapping(
            existingDevice.Id,
            "DieCutting.PunchingQuantity",
            "R2450",
            addressCount: 2,
            dataType: "Int32"));
        result.Services.AddSingleton(configuration);
        result.Services.AddSingleton<ILogService, ContractLogService>();
        result.Services.AddSingleton<IRepository<NetworkDeviceEntity>>(networkDevices);
        result.Services.AddSingleton<IRepository<IoMappingEntity>>(ioMappings);
        result.Services.AddSingleton<IRepository<PlcTaskBindingEntity>>(taskBindings);
        await using var serviceProvider = result.Services.BuildServiceProvider();
        var contributor = serviceProvider
            .GetServices<IDevelopmentSampleContributor>()
            .OfType<DieCuttingDevelopmentSampleContributor>()
            .Single();

        await contributor.EnsureConfigurationSamplesAsync(TestContext.Current.CancellationToken);
        await contributor.EnsureConfigurationSamplesAsync(TestContext.Current.CancellationToken);

        var hardwareProfile = serviceProvider.GetRequiredService<IModuleHardwareProfileProvider>();
        var cp02Mappings = ioMappings.Items
            .Where(mapping => mapping.NetworkDeviceId == existingDevice.Id)
            .ToArray();
        Assert.Equal(12, networkDevices.Items.Count);
        Assert.All(networkDevices.Items, static device => Assert.False(device.IsEnabled));
        Assert.Equal(SeedableTemplateCount(hardwareProfile), cp02Mappings.Length);
        Assert.Contains(cp02Mappings, static mapping =>
            mapping.SignalKey == "DieCutting.BatchNumber"
            && mapping.Direction == "Read"
            && mapping.PlcAddress == "R9600");
        Assert.Contains(cp02Mappings, static mapping =>
            mapping.SignalKey == "DieCutting.DeviceStatus"
            && mapping.Direction == "Read"
            && mapping.PlcAddress == "R100");
        Assert.Equal(65531, existingDevice.Port1);
        var definition = serviceProvider.GetRequiredService<DieCuttingModuleDefinition>();
        Assert.Equal(networkDevices.Items.Count * 2, taskBindings.Items.Count(static binding => binding.Enabled));
        Assert.Equal(
            networkDevices.Items.Count,
            taskBindings.Items.Count(binding => binding.TaskKey == definition.RealtimeSampleUploadTaskKey && binding.Enabled));
        Assert.Equal(
            networkDevices.Items.Count,
            taskBindings.Items.Count(binding => binding.TaskKey == definition.DeviceStatusUploadTaskKey && binding.Enabled));
    }

    [Fact]
    public async Task DevelopmentSampleContributor_WhenExistingSeedDeviceUsesCustomPort_ShouldKeepCustomPort()
    {
        var configuration = CreateEnabledSeedConfiguration(resetBeforeImport: false);
        var result = new ModuleContractFixture().RegisterModule(new TModule(), configuration);
        var networkDevices = new InMemoryRepository<NetworkDeviceEntity>();
        var ioMappings = new InMemoryRepository<IoMappingEntity>();
        var taskBindings = new InMemoryRepository<PlcTaskBindingEntity>();
        var existingDevice = NetworkDeviceEntity.Create(
            ExpectedDeviceName(2),
            DeviceType.PLC,
            ExpectedIpAddress(2),
            65000);
        existingDevice.UpdateDeviceModel("Mc");
        existingDevice.SetEnabled(false);
        existingDevice.UpdateRemark(ExpectedDisplayName);
        networkDevices.Add(existingDevice);
        result.Services.AddSingleton(configuration);
        result.Services.AddSingleton<ILogService, ContractLogService>();
        result.Services.AddSingleton<IRepository<NetworkDeviceEntity>>(networkDevices);
        result.Services.AddSingleton<IRepository<IoMappingEntity>>(ioMappings);
        result.Services.AddSingleton<IRepository<PlcTaskBindingEntity>>(taskBindings);
        await using var serviceProvider = result.Services.BuildServiceProvider();
        var contributor = serviceProvider
            .GetServices<IDevelopmentSampleContributor>()
            .OfType<DieCuttingDevelopmentSampleContributor>()
            .Single();

        await contributor.EnsureConfigurationSamplesAsync(TestContext.Current.CancellationToken);

        Assert.Equal(65000, existingDevice.Port1);
    }

    [Fact]
    public void PluginManifest_ShouldMatchModuleEntry()
    {
        var manifestPath = Path.Combine(
            ContractTestPathHelper.GetModuleSourceDirectory(ExpectedModuleId),
            "plugin.json");

        using var document = JsonDocument.Parse(File.ReadAllText(manifestPath));
        var root = document.RootElement;

        Assert.Equal(ExpectedModuleId, root.GetProperty("moduleId").GetString());
        Assert.Equal(ExpectedDisplayName, root.GetProperty("displayName").GetString());
        Assert.Equal(ExpectedModuleId, root.GetProperty("supportedProcessType").GetString());
        Assert.Equal(ExpectedEntryType, root.GetProperty("entryType").GetString());
    }

    [Fact]
    public void SharedDieCuttingLibrary_ShouldNotDeclarePluginManifest()
    {
        var sharedManifestPath = Path.Combine(
            ContractTestPathHelper.FindRepoRoot(),
            "src",
            "Modules",
            "IIoT.Edge.Module.DieCutting.Shared",
            "plugin.json");

        Assert.False(
            File.Exists(sharedManifestPath),
            "共享模切库不能声明 plugin.json，避免打包扫描时把抽象共享库误当成可加载插件。");
    }

    [Fact]
    public void RegisterServices_ShouldRegisterMesScenarioChannelAsProcessUploader()
    {
        var result = new ModuleContractFixture().RegisterModule(new TModule());

        Assert.Contains(
            result.Services,
            descriptor => descriptor.ServiceType == typeof(IDieCuttingMesScenarioChannel)
                          && descriptor.ImplementationFactory is not null);
        Assert.Contains(
            result.Services,
            descriptor => descriptor.ServiceType == typeof(IProcessMesUploader)
                          && descriptor.ImplementationFactory is not null);
    }

    [Fact]
    public void RegisterViews_ShouldUseDieCuttingPluginDataView()
    {
        var result = new ModuleContractFixture().RegisterModule(new TModule());
        var registration = result.ViewRegistry.GetViewRegistration($"{ExpectedModuleId}.DataView");

        Assert.NotNull(registration);
        Assert.Equal(typeof(DieCuttingDataPage), registration.ViewType);
        Assert.Equal(typeof(DieCuttingDataViewModel), registration.ViewModelType);
    }

    [Fact]
    public void RegisterServices_ShouldRegisterDieCuttingProductionStore()
    {
        var result = new ModuleContractFixture().RegisterModule(new TModule());

        Assert.Contains(
            result.Services,
            descriptor => descriptor.ServiceType == typeof(IDieCuttingProductionRecordStore)
                          && descriptor.ImplementationFactory is not null);
    }

    [Fact]
    public async Task ProductionRecordStore_ShouldPersistRealRowsAndFilterBySelectedDevice()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"iiot-edge-diecutting-production-{Guid.NewGuid():N}");
        try
        {
            var store = new DieCuttingProductionRecordStore(tempDir, new ContractLogService());
            var completedAt = DateTime.UtcNow;

            await store.AddAsync(
                new DieCuttingProductionRecord
                {
                    ModuleId = ExpectedModuleId,
                    DeviceName = ExpectedFirstDevice,
                    BatchNo = "TRACE-REAL-001",
                    Quantity = 12,
                    WindowStartAt = completedAt.AddMinutes(-1),
                    WindowCompleteAt = completedAt,
                    PunchingSpeed = 60.5m,
                    PlateLengthMm = 125.4m,
                    PlateWidthMm = 75.2m,
                    CreatedAtUtc = completedAt
                },
                TestContext.Current.CancellationToken);
            await store.AddAsync(
                new DieCuttingProductionRecord
                {
                    ModuleId = ExpectedModuleId,
                    DeviceName = ExpectedLastDevice,
                    BatchNo = "TRACE-REAL-002",
                    Quantity = 5,
                    WindowStartAt = completedAt.AddMinutes(-2),
                    WindowCompleteAt = completedAt.AddSeconds(-30),
                    PunchingSpeed = 55m,
                    CreatedAtUtc = completedAt
                },
                TestContext.Current.CancellationToken);

            var allRows = await store.QueryAsync(
                ExpectedModuleId,
                "__all__",
                cancellationToken: TestContext.Current.CancellationToken);
            var selectedRows = await store.QueryAsync(
                ExpectedModuleId,
                ExpectedFirstDevice,
                cancellationToken: TestContext.Current.CancellationToken);

            Assert.Equal(2, allRows.Count);
            var row = Assert.Single(selectedRows);
            Assert.Equal(ExpectedFirstDevice, row.DeviceName);
            Assert.Equal("TRACE-REAL-001", row.BatchNo);
            Assert.Equal(12, row.Quantity);
            Assert.Equal(60.5m, row.PunchingSpeed);
            Assert.Equal(125.4m, row.PlateLengthMm);
            Assert.Equal(75.2m, row.PlateWidthMm);
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, recursive: true);
            }
        }
    }

    [Fact]
    public void ModuleDefinition_ShouldSeedOnlyOneLineOfTwelvePlcs()
    {
        var result = new ModuleContractFixture().RegisterModule(new TModule());
        using var provider = result.Services.BuildServiceProvider();
        var definition = provider.GetRequiredService<DieCuttingModuleDefinition>();

        Assert.Equal(ExpectedModuleId, definition.ModuleId);
        Assert.Equal(ExpectedDisplayName, definition.DisplayName);
        Assert.Equal(ExpectedOperationCode, definition.OperationCode);
        Assert.Equal(12, definition.DefaultDevices.Count);
        AssertDefaultDevice(definition.DefaultDevices[0], ExpectedFirstDevice, ExpectedFirstIpAddress);
        AssertDefaultDevice(definition.DefaultDevices[^1], ExpectedLastDevice, ExpectedLastIpAddress);
        Assert.All(
            definition.DefaultDevices,
            device =>
            {
                Assert.Equal(ExpectedUpperComputerNo, device.UpperComputerNo);
                Assert.False(device.IsEnabled);
            });
    }

    [Fact]
    public void RuntimeFactory_TaskCandidates_ShouldSeparateRealtimeDataAndDeviceStatus()
    {
        var result = new ModuleContractFixture().RegisterModule(new TModule());
        Assert.True(result.RuntimeRegistry.TryGetFactory(ExpectedModuleId, out var factory));

        using var provider = result.Services.BuildServiceProvider();
        var definition = provider.GetRequiredService<DieCuttingModuleDefinition>();
        var candidates = factory.GetTaskCandidates();
        var realtimeCandidate = Assert.Single(candidates, candidate => candidate.Key == definition.RealtimeSampleUploadTaskKey);
        var statusCandidate = Assert.Single(candidates, candidate => candidate.Key == definition.DeviceStatusUploadTaskKey);
        Assert.True(realtimeCandidate.DefaultEnabled);
        Assert.True(statusCandidate.DefaultEnabled);

        var realtimeRequiredSignals = realtimeCandidate.RequiredSignals
            .Select(static signal => signal.SignalKey)
            .ToArray();

        Assert.DoesNotContain("DieCutting.DeviceStatus", realtimeRequiredSignals);
        Assert.Contains("DieCutting.PunchingQuantity", realtimeRequiredSignals);
        Assert.Contains("DieCutting.PunchingSpeed", realtimeRequiredSignals);
        Assert.Contains("DieCutting.BatchNumber", realtimeRequiredSignals);
        Assert.Contains("DieCutting.ClipNo.Mg1", realtimeRequiredSignals);
        Assert.Contains("DieCutting.ClipNo.Mg2", realtimeRequiredSignals);
        Assert.Contains("DieCutting.OperatorCode", realtimeRequiredSignals);
        Assert.Contains("DieCutting.MoldCode", realtimeRequiredSignals);
        Assert.Contains("DieCutting.CutterCode", realtimeRequiredSignals);

        var statusRequiredSignal = Assert.Single(statusCandidate.RequiredSignals);
        Assert.Equal("DieCutting.DeviceStatus", statusRequiredSignal.SignalKey);
        Assert.Equal("Read", statusRequiredSignal.Direction);
    }

    [Fact]
    public async Task TaskBindingService_WhenStandardIoExists_ShouldDefaultEnableRealtimeAndDeviceStatus()
    {
        var result = new ModuleContractFixture().RegisterModule(new TModule());
        Assert.True(result.RuntimeRegistry.TryGetFactory(ExpectedModuleId, out var factory));
        using var provider = result.Services.BuildServiceProvider();
        var definition = provider.GetRequiredService<DieCuttingModuleDefinition>();
        var hardwareProfile = provider.GetRequiredService<IModuleHardwareProfileProvider>();
        var service = new PlcTaskBindingService(
            new ConfigurationBuilder().Build(),
            result.RuntimeRegistry,
            new InMemoryRepository<NetworkDeviceEntity>(),
            new InMemoryRepository<IoMappingEntity>(),
            new InMemoryRepository<PlcTaskBindingEntity>(),
            new ContractLogService());

        var enabledKeys = await service.GetEnabledTaskKeysAsync(
            networkDeviceId: 1,
            factory.GetTaskCandidates(),
            CreateStandardIoSnapshots(hardwareProfile),
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(
            [definition.DeviceStatusUploadTaskKey, definition.RealtimeSampleUploadTaskKey],
            enabledKeys.OrderBy(static key => key, StringComparer.OrdinalIgnoreCase));
    }

    [Fact]
    public void RuntimeFactory_WhenAllDieCuttingTasksDisabled_ShouldCreateNoTasks()
    {
        var result = new ModuleContractFixture().RegisterModule(new TModule());
        Assert.True(result.RuntimeRegistry.TryGetFactory(ExpectedModuleId, out var factory));

        using var provider = new ServiceCollection().BuildServiceProvider();
        var tasks = factory.CreateTasks(
            provider,
            new PlcBuffer(16, 16),
            new DieCuttingContext { DeviceName = ExpectedFirstDevice },
            new HashSet<string>(StringComparer.OrdinalIgnoreCase));

        Assert.Empty(tasks);
    }

    [Fact]
    public async Task RealtimeSampleUploadTask_WhenPlcDisconnected_ShouldWriteChineseDiagnosticsLog()
    {
        var logEntries = new List<LogEntry>();
        var logService = new ContractLogService();
        logService.EntryAdded += entry => logEntries.Add(entry);

        var runtime = CreateRealtimeSampleTask(
            logService,
            new DisconnectedPlcConnectionManager(),
            new ContractDieCuttingModuleParamProvider(ExpectedModuleId, ExpectedUpperComputerNo, ExpectedOperationCode));
        using var provider = runtime.Provider;

        await InvokeTaskCoreOnceAsync(runtime.Task);

        Assert.Contains(logEntries, entry =>
            entry.Message.Contains("[模切采样] 任务配置", StringComparison.Ordinal)
            && entry.Message.Contains($"MES地址={TestMesBaseUrl}", StringComparison.Ordinal)
            && entry.Message.Contains("采集处理周期=1000ms", StringComparison.Ordinal));
        Assert.Contains(logEntries, entry =>
            entry.Message.Contains("PLC 未连接，模切采样上传暂停", StringComparison.Ordinal));
        Assert.DoesNotContain(logEntries, ContainsSensitiveMesCredentialText);
    }

    [Fact]
    public async Task RealtimeSampleUploadTask_WhenMesBaseUrlIsCustom_ShouldNotWarnLegacyOrCredentialText()
    {
        var logEntries = new List<LogEntry>();
        var logService = new ContractLogService();
        logService.EntryAdded += entry => logEntries.Add(entry);

        var runtime = CreateRealtimeSampleTask(
            logService,
            new DisconnectedPlcConnectionManager(),
            new ContractDieCuttingModuleParamProvider(
                ExpectedModuleId,
                ExpectedUpperComputerNo,
                ExpectedOperationCode,
                mesBaseUrl: "http://legacy-mes.example.test:8081"));
        using var provider = runtime.Provider;

        await InvokeTaskCoreOnceAsync(runtime.Task);

        Assert.DoesNotContain(logEntries, entry =>
            entry.Message.Contains("历史默认值", StringComparison.Ordinal));
        Assert.DoesNotContain(logEntries, ContainsSensitiveMesCredentialText);
    }

    [Fact]
    public async Task RealtimeSampleUploadTask_WhenMesDisabled_ShouldStillCaptureLocalProductionData()
    {
        var logService = new ContractLogService();
        var recordStore = new InMemoryDieCuttingProductionRecordStore();
        var mesChannel = new CountingDieCuttingMesChannel(ExpectedModuleId);
        var runtime = CreateRealtimeSampleRuntime(
            logService,
            new ContractPlcConnectionManager(),
            new ContractDieCuttingModuleParamProvider(
                ExpectedModuleId,
                ExpectedUpperComputerNo,
                ExpectedOperationCode,
                mesEnabled: false),
            recordStore,
            mesChannel);
        using var provider = runtime.Provider;
        SeedRealtimeSignals(runtime.Buffer);

        await InvokeTaskCoreOnceAsync(runtime.Task);
        await InvokeTaskCoreOnceAsync(runtime.Task);

        var records = await recordStore.QueryAsync(
            ExpectedModuleId,
            ExpectedFirstDevice,
            cancellationToken: TestContext.Current.CancellationToken);
        var record = Assert.Single(records);
        Assert.Equal("BATCH-1", record.BatchNo);
        Assert.Equal(100, record.Quantity);
        Assert.Equal(12.5m, record.PunchingSpeed);
        Assert.Equal(3456, runtime.Context.LastRealtimeSnapshot?.UnwindingLength);
        Assert.Contains("MES/Cloud 上传已关闭", runtime.Context.LastRealtimeResult);
        Assert.Equal(0, mesChannel.RealtimeUploadCount);
        Assert.Equal(0, mesChannel.DeviceStatusUploadCount);
    }

    [Fact]
    public async Task RealtimeSampleUploadTask_WhenMesEnabledWithoutMainPlan_ShouldNotStoreLocalRecordOrEnqueueUpload()
    {
        var logService = new ContractLogService();
        var recordStore = new InMemoryDieCuttingProductionRecordStore();
        var pipeline = new CapturingDataPipelineService();
        var runtime = CreateRealtimeSampleRuntime(
            logService,
            new ContractPlcConnectionManager(),
            new ContractDieCuttingModuleParamProvider(ExpectedModuleId, ExpectedUpperComputerNo, ExpectedOperationCode),
            recordStore,
            mesChannel: null,
            pipeline);
        using var provider = runtime.Provider;
        runtime.Context.PlanSessionId = "STALE-SESSION";
        runtime.Context.SelectedProductionPlan = CreatePlanOption("STALE-MP");
        runtime.Context.TraceBatchNumber = "STALE-TRACE";
        SeedRealtimeSignals(runtime.Buffer);

        await InvokeTaskCoreOnceAsync(runtime.Task);

        var records = await recordStore.QueryAsync(
            ExpectedModuleId,
            ExpectedFirstDevice,
            cancellationToken: TestContext.Current.CancellationToken);
        Assert.Empty(records);
        Assert.Empty(pipeline.Records);
        Assert.Contains("请先选择主批计划", runtime.Context.LastRealtimeResult, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RealtimeSampleUploadTask_WhenMainPlanSelected_ShouldEnqueueUploadWithPlcAndPlanContext()
    {
        var logService = new ContractLogService();
        var recordStore = new InMemoryDieCuttingProductionRecordStore();
        var pipeline = new CapturingDataPipelineService();
        var httpClient = new CapturingMesHttpClient
        {
            PostResponse = """{"code":200,"msg":"OK","data":{"batchNumber":"TRACE-PLAN-001"}}"""
        };
        var runtime = CreateRealtimeSampleRuntime(
            logService,
            new ContractPlcConnectionManager(),
            new ContractDieCuttingModuleParamProvider(ExpectedModuleId, ExpectedUpperComputerNo, ExpectedOperationCode),
            recordStore,
            mesChannel: null,
            pipeline,
            httpClient);
        using var provider = runtime.Provider;
        var planService = provider.GetRequiredService<DieCuttingProductionPlanService>();
        await planService.SelectPlanAsync(CreatePlanOption("MP-001"), TestContext.Current.CancellationToken);
        SeedRealtimeSignals(runtime.Buffer);

        await InvokeTaskCoreOnceAsync(runtime.Task);

        var outbound = Assert.Single(
            pipeline.Records,
            record => Assert.IsType<DieCuttingCellData>(record.CellData).RecordKind == DieCuttingCellData.RecordKinds.RealtimeOutbound);
        var cellData = Assert.IsType<DieCuttingCellData>(outbound.CellData);
        var definition = provider.GetRequiredService<DieCuttingModuleDefinition>();
        Assert.Equal(1, outbound.NetworkDeviceId);
        Assert.Equal(ExpectedFirstDevice, outbound.DeviceName);
        Assert.Equal(ExpectedModuleId, outbound.ModuleId);
        Assert.Equal(definition.RealtimeSampleUploadTaskKey, outbound.TaskKey);
        Assert.False(string.IsNullOrWhiteSpace(outbound.PlanSessionId));
        Assert.Equal("MP-001", outbound.MainPlanCode);
        Assert.Equal("TRACE-PLAN-001", outbound.TraceBatchNumber);
        Assert.Equal(outbound.NetworkDeviceId, cellData.PlcDeviceId);
        Assert.Equal(DieCuttingCellData.RecordKinds.RealtimeOutbound, cellData.RecordKind);
        Assert.Equal(DataPipelineUploadTargets.Mes, cellData.UploadTargets);
        Assert.Contains("MES 上传队列", runtime.Context.LastRealtimeResult, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RealtimeSampleUploadTask_WhenMesDisabledAndCloudEnabled_ShouldEnqueueCloudOnlyWithoutMainPlan()
    {
        var logService = new ContractLogService();
        var recordStore = new InMemoryDieCuttingProductionRecordStore();
        var pipeline = new CapturingDataPipelineService();
        var runtime = CreateRealtimeSampleRuntime(
            logService,
            new ContractPlcConnectionManager(),
            new ContractDieCuttingModuleParamProvider(
                ExpectedModuleId,
                ExpectedUpperComputerNo,
                ExpectedOperationCode,
                mesEnabled: false,
                cloudEnabled: true),
            recordStore,
            mesChannel: null,
            pipeline);
        using var provider = runtime.Provider;
        runtime.Context.PlanSessionId = "STALE-SESSION";
        runtime.Context.SelectedProductionPlan = CreatePlanOption("STALE-MP");
        runtime.Context.TraceBatchNumber = "STALE-TRACE";
        SeedRealtimeSignals(runtime.Buffer);

        await InvokeTaskCoreOnceAsync(runtime.Task);

        var outbound = Assert.Single(
            pipeline.Records,
            record => Assert.IsType<DieCuttingCellData>(record.CellData).RecordKind == DieCuttingCellData.RecordKinds.RealtimeOutbound);
        var cellData = Assert.IsType<DieCuttingCellData>(outbound.CellData);
        var definition = provider.GetRequiredService<DieCuttingModuleDefinition>();
        Assert.Equal(ExpectedModuleId, outbound.ModuleId);
        Assert.Equal(definition.RealtimeSampleUploadTaskKey, outbound.TaskKey);
        Assert.Equal(string.Empty, outbound.PlanSessionId);
        Assert.Equal(string.Empty, outbound.MainPlanCode);
        Assert.Equal(string.Empty, outbound.TraceBatchNumber);
        Assert.Equal(DataPipelineUploadTargets.Cloud, cellData.UploadTargets);
        Assert.Equal(outbound.NetworkDeviceId, cellData.PlcDeviceId);
        Assert.Contains("Cloud 上传队列", runtime.Context.LastRealtimeResult, StringComparison.Ordinal);

        var records = await recordStore.QueryAsync(
            ExpectedModuleId,
            ExpectedFirstDevice,
            cancellationToken: TestContext.Current.CancellationToken);
        Assert.Single(records);
    }

    [Fact]
    public async Task RealtimeSampleUploadTask_WhenCloudOnlyDataPipelineThrows_ShouldRecordCloudFailure()
    {
        var logService = new ContractLogService();
        var recordStore = new InMemoryDieCuttingProductionRecordStore();
        var pipeline = new CapturingDataPipelineService
        {
            ExceptionToThrow = new InvalidOperationException("本地队列异常")
        };
        var runtime = CreateRealtimeSampleRuntime(
            logService,
            new ContractPlcConnectionManager(),
            new ContractDieCuttingModuleParamProvider(
                ExpectedModuleId,
                ExpectedUpperComputerNo,
                ExpectedOperationCode,
                mesEnabled: false,
                cloudEnabled: true),
            recordStore,
            mesChannel: null,
            pipeline);
        using var provider = runtime.Provider;
        SeedRealtimeSignals(runtime.Buffer);

        await InvokeTaskCoreOnceAsync(runtime.Task);

        var definition = provider.GetRequiredService<DieCuttingModuleDefinition>();
        var diagnostics = provider.GetRequiredService<ICloudUploadDiagnosticsStore>().Snapshot;
        Assert.Empty(pipeline.Records);
        Assert.Contains("本地队列异常", runtime.Context.LastRealtimeResult, StringComparison.Ordinal);
        Assert.Null(runtime.Context.LastOutboundFingerprint);
        Assert.Equal(CloudCallOutcome.Exception, diagnostics.LastOutcome);
        Assert.Equal("plc_realtime_enqueue_failed", diagnostics.LastReasonCode);
        Assert.Equal(definition.ProcessType, diagnostics.LastProcessType);
        Assert.Equal(ExpectedModuleId, diagnostics.LastModuleId);
        Assert.Equal(definition.RealtimeSampleUploadTaskKey, diagnostics.LastTaskKey);
        Assert.Equal("生产上传", diagnostics.LastScenario);
    }

    [Fact]
    public async Task RealtimeSampleUploadTask_WhenMesAndCloudEnabledWithMainPlan_ShouldEnqueueAllTargets()
    {
        var logService = new ContractLogService();
        var recordStore = new InMemoryDieCuttingProductionRecordStore();
        var pipeline = new CapturingDataPipelineService();
        var httpClient = new CapturingMesHttpClient
        {
            PostResponse = """{"code":200,"msg":"OK","data":{"batchNumber":"TRACE-PLAN-001"}}"""
        };
        var runtime = CreateRealtimeSampleRuntime(
            logService,
            new ContractPlcConnectionManager(),
            new ContractDieCuttingModuleParamProvider(
                ExpectedModuleId,
                ExpectedUpperComputerNo,
                ExpectedOperationCode,
                cloudEnabled: true),
            recordStore,
            mesChannel: null,
            pipeline,
            httpClient);
        using var provider = runtime.Provider;
        var planService = provider.GetRequiredService<DieCuttingProductionPlanService>();
        await planService.SelectPlanAsync(CreatePlanOption("MP-001"), TestContext.Current.CancellationToken);
        SeedRealtimeSignals(runtime.Buffer);

        await InvokeTaskCoreOnceAsync(runtime.Task);

        var outbound = Assert.Single(
            pipeline.Records,
            record => Assert.IsType<DieCuttingCellData>(record.CellData).RecordKind == DieCuttingCellData.RecordKinds.RealtimeOutbound);
        var cellData = Assert.IsType<DieCuttingCellData>(outbound.CellData);
        Assert.Equal(DataPipelineUploadTargets.All, cellData.UploadTargets);
        Assert.False(string.IsNullOrWhiteSpace(outbound.PlanSessionId));
        Assert.Equal("MP-001", outbound.MainPlanCode);
        Assert.Equal("TRACE-PLAN-001", outbound.TraceBatchNumber);
        Assert.Contains("MES/Cloud 上传队列", runtime.Context.LastRealtimeResult, StringComparison.Ordinal);
    }

    [Fact]
    public async Task DeviceStatusUploadTask_WhenMesEnabledWithoutMainPlan_ShouldEnqueueStatusWithPlcContext()
    {
        var logService = new ContractLogService();
        var pipeline = new CapturingDataPipelineService();
        var runtime = CreateDeviceStatusRuntime(
            logService,
            new ContractPlcConnectionManager(),
            new ContractDieCuttingModuleParamProvider(ExpectedModuleId, ExpectedUpperComputerNo, ExpectedOperationCode),
            pipeline);
        using var provider = runtime.Provider;
        SeedRealtimeSignals(runtime.Buffer);

        await InvokeTaskCoreOnceAsync(runtime.Task);

        var record = Assert.Single(pipeline.Records);
        var cellData = Assert.IsType<DieCuttingCellData>(record.CellData);
        var definition = provider.GetRequiredService<DieCuttingModuleDefinition>();
        Assert.Equal(1, record.NetworkDeviceId);
        Assert.Equal(ExpectedFirstDevice, record.DeviceName);
        Assert.Equal(ExpectedModuleId, record.ModuleId);
        Assert.Equal(definition.DeviceStatusUploadTaskKey, record.TaskKey);
        Assert.Equal(string.Empty, record.PlanSessionId);
        Assert.Equal(string.Empty, record.MainPlanCode);
        Assert.Equal(string.Empty, record.TraceBatchNumber);
        Assert.Equal(DataPipelineUploadTargets.Mes, cellData.UploadTargets);
        Assert.Equal(DieCuttingCellData.RecordKinds.DeviceStatus, cellData.RecordKind);
        Assert.Equal((short)1, cellData.StatusCode);
        Assert.Contains("MES 上传队列", runtime.Context.LastDeviceStatusResult, StringComparison.Ordinal);
    }

    [Fact]
    public async Task DeviceStatusUploadTask_WhenMesDisabledAndCloudEnabled_ShouldEnqueueCloudOnlyWithoutMainPlan()
    {
        var logService = new ContractLogService();
        var pipeline = new CapturingDataPipelineService();
        var runtime = CreateDeviceStatusRuntime(
            logService,
            new ContractPlcConnectionManager(),
            new ContractDieCuttingModuleParamProvider(
                ExpectedModuleId,
                ExpectedUpperComputerNo,
                ExpectedOperationCode,
                mesEnabled: false,
                cloudEnabled: true),
            pipeline);
        using var provider = runtime.Provider;
        SeedRealtimeSignals(runtime.Buffer);

        await InvokeTaskCoreOnceAsync(runtime.Task);

        var record = Assert.Single(pipeline.Records);
        var cellData = Assert.IsType<DieCuttingCellData>(record.CellData);
        var definition = provider.GetRequiredService<DieCuttingModuleDefinition>();
        Assert.Equal(1, record.NetworkDeviceId);
        Assert.Equal(ExpectedFirstDevice, record.DeviceName);
        Assert.Equal(ExpectedModuleId, record.ModuleId);
        Assert.Equal(definition.DeviceStatusUploadTaskKey, record.TaskKey);
        Assert.Equal(string.Empty, record.PlanSessionId);
        Assert.Equal(string.Empty, record.MainPlanCode);
        Assert.Equal(string.Empty, record.TraceBatchNumber);
        Assert.Equal(DataPipelineUploadTargets.Cloud, cellData.UploadTargets);
        Assert.Equal(DieCuttingCellData.RecordKinds.DeviceStatus, cellData.RecordKind);
        Assert.Contains("Cloud 上传队列", runtime.Context.LastDeviceStatusResult, StringComparison.Ordinal);
    }

    [Fact]
    public async Task DeviceStatusUploadTask_WhenCloudOnlyDataPipelineThrows_ShouldRecordCloudFailure()
    {
        var logService = new ContractLogService();
        var pipeline = new CapturingDataPipelineService
        {
            ExceptionToThrow = new InvalidOperationException("本地队列异常")
        };
        var runtime = CreateDeviceStatusRuntime(
            logService,
            new ContractPlcConnectionManager(),
            new ContractDieCuttingModuleParamProvider(
                ExpectedModuleId,
                ExpectedUpperComputerNo,
                ExpectedOperationCode,
                mesEnabled: false,
                cloudEnabled: true),
            pipeline);
        using var provider = runtime.Provider;
        SeedRealtimeSignals(runtime.Buffer);

        await InvokeTaskCoreOnceAsync(runtime.Task);

        var definition = provider.GetRequiredService<DieCuttingModuleDefinition>();
        var diagnostics = provider.GetRequiredService<ICloudUploadDiagnosticsStore>().Snapshot;
        Assert.Empty(pipeline.Records);
        Assert.Contains("本地队列异常", runtime.Context.LastDeviceStatusResult, StringComparison.Ordinal);
        Assert.Null(runtime.Context.LastDeviceStatusFingerprint);
        Assert.Equal(CloudCallOutcome.Exception, diagnostics.LastOutcome);
        Assert.Equal("plc_device_status_enqueue_failed", diagnostics.LastReasonCode);
        Assert.Equal(definition.ProcessType, diagnostics.LastProcessType);
        Assert.Equal(ExpectedModuleId, diagnostics.LastModuleId);
        Assert.Equal(definition.DeviceStatusUploadTaskKey, diagnostics.LastTaskKey);
        Assert.Equal("设备状态上传", diagnostics.LastScenario);
    }

    [Fact]
    public async Task DeviceStatusUploadTask_WhenMesAndCloudEnabled_ShouldEnqueueAllTargetsWithoutMainPlan()
    {
        var logService = new ContractLogService();
        var pipeline = new CapturingDataPipelineService();
        var runtime = CreateDeviceStatusRuntime(
            logService,
            new ContractPlcConnectionManager(),
            new ContractDieCuttingModuleParamProvider(
                ExpectedModuleId,
                ExpectedUpperComputerNo,
                ExpectedOperationCode,
                cloudEnabled: true),
            pipeline);
        using var provider = runtime.Provider;
        SeedRealtimeSignals(runtime.Buffer);

        await InvokeTaskCoreOnceAsync(runtime.Task);

        var record = Assert.Single(pipeline.Records);
        var cellData = Assert.IsType<DieCuttingCellData>(record.CellData);
        Assert.Equal(DataPipelineUploadTargets.All, cellData.UploadTargets);
        Assert.Equal(string.Empty, record.PlanSessionId);
        Assert.Equal(string.Empty, record.MainPlanCode);
        Assert.Equal(string.Empty, record.TraceBatchNumber);
        Assert.Contains("MES/Cloud 上传队列", runtime.Context.LastDeviceStatusResult, StringComparison.Ordinal);
    }

    [Fact]
    public async Task DeviceStatusUploadTask_WhenMesAndCloudDisabled_ShouldSkipQueueWithoutMainPlan()
    {
        var logService = new ContractLogService();
        var pipeline = new CapturingDataPipelineService();
        var runtime = CreateDeviceStatusRuntime(
            logService,
            new ContractPlcConnectionManager(),
            new ContractDieCuttingModuleParamProvider(
                ExpectedModuleId,
                ExpectedUpperComputerNo,
                ExpectedOperationCode,
                mesEnabled: false,
                cloudEnabled: false),
            pipeline);
        using var provider = runtime.Provider;
        SeedRealtimeSignals(runtime.Buffer);

        await InvokeTaskCoreOnceAsync(runtime.Task);

        Assert.Empty(pipeline.Records);
        Assert.NotNull(runtime.Context.LastDeviceStatusAt);
        Assert.Null(runtime.Context.LastDeviceStatusFingerprint);
        Assert.Contains("MES/Cloud 上传已关闭", runtime.Context.LastDeviceStatusResult, StringComparison.Ordinal);
        Assert.Equal("Disabled", runtime.Context.Get<string>($"Runtime.Tasks.{provider.GetRequiredService<DieCuttingModuleDefinition>().DeviceStatusUploadTaskKey}.LastUploadOutcome"));
    }

    [Fact]
    public async Task DeviceStatusUploadTask_WhenStatusCodeUnknown_ShouldSkipQueueAndKeepFingerprintUnset()
    {
        var logService = new ContractLogService();
        var pipeline = new CapturingDataPipelineService();
        var runtime = CreateDeviceStatusRuntime(
            logService,
            new ContractPlcConnectionManager(),
            new ContractDieCuttingModuleParamProvider(ExpectedModuleId, ExpectedUpperComputerNo, ExpectedOperationCode),
            pipeline);
        using var provider = runtime.Provider;
        SeedRealtimeSignals(runtime.Buffer);
        SetReadInt16(runtime.Buffer, DieCuttingPlcSignals.SingleRead.设备状态, 99);

        await InvokeTaskCoreOnceAsync(runtime.Task);

        var definition = provider.GetRequiredService<DieCuttingModuleDefinition>();
        Assert.Empty(pipeline.Records);
        Assert.NotNull(runtime.Context.LastDeviceStatusAt);
        Assert.Null(runtime.Context.LastDeviceStatusFingerprint);
        Assert.Contains("设备状态码未知", runtime.Context.LastDeviceStatusResult, StringComparison.Ordinal);
        Assert.Equal("InvalidContext", runtime.Context.Get<string>($"Runtime.Tasks.{definition.DeviceStatusUploadTaskKey}.LastUploadOutcome"));
    }

    [Fact]
    public void DependencyInjection_Configure_BindsLocalOptionsAndParameterRoles()
    {
        var configuration = new ConfigurationBuilder()
            .AddJsonFile(
                Path.Combine(
                    ContractTestPathHelper.GetModuleSourceDirectory(ExpectedModuleId),
                    "Config",
                    ExpectedConfigFileName),
                optional: false,
                reloadOnChange: false)
            .Build();

        var result = new ModuleContractFixture().RegisterModule(new TModule(), configuration);
        using var provider = result.Services.BuildServiceProvider();

        Assert.Equal(1000, provider.GetRequiredService<IOptions<DieCuttingModuleOptions>>().Value.Runtime.DataReadLoopIntervalMs);
        Assert.Contains(
            result.ModuleParamRegistry.GetDescriptors(ExpectedModuleId, ModuleParamCategory.Business),
            descriptor => descriptor.Role == ModuleParamRole.DataReadLoopIntervalMs
                          && descriptor.Name == nameof(DieCuttingParams.Business.采集频率毫秒));
        Assert.Contains(
            result.ModuleParamRegistry.GetDescriptors(ExpectedModuleId, ModuleParamCategory.Mes),
            descriptor => descriptor.Role == ModuleParamRole.MesSignToken
                          && descriptor.Name == nameof(DieCuttingParams.Mes.签名令牌)
                          && string.IsNullOrEmpty(descriptor.DefaultValue));
        Assert.Contains(
            result.ModuleParamRegistry.GetDescriptors(ExpectedModuleId, ModuleParamCategory.Mes),
            descriptor => descriptor.Role == ModuleParamRole.MesBaseUrl
                          && descriptor.Name == nameof(DieCuttingParams.Mes.服务地址)
                          && descriptor.DefaultValue == ExpectedMesBaseUrl
                          && descriptor.LegacyDefaultValues is not null
                          && descriptor.LegacyDefaultValues.Count == 0);
        Assert.Contains(
            result.ModuleParamRegistry.GetDescriptors(ExpectedModuleId, ModuleParamCategory.Mes),
            descriptor => descriptor.Role == ModuleParamRole.MesUpperComputerNo
                          && descriptor.Name == nameof(DieCuttingParams.Mes.UpperComputerNo)
                          && descriptor.DefaultValue == ExpectedUpperComputerNo);
        Assert.Contains(
            result.ModuleParamRegistry.GetDescriptors(ExpectedModuleId, ModuleParamCategory.Mes),
            descriptor => descriptor.Role == ModuleParamRole.MesOperationCode
                          && descriptor.Name == nameof(DieCuttingParams.Mes.OperationCode)
                          && descriptor.DefaultValue == ExpectedOperationCode);
        Assert.Contains(
            result.ModuleParamRegistry.GetDescriptors(ExpectedModuleId, ModuleParamCategory.Mes),
            descriptor => descriptor.Name == nameof(DieCuttingParams.Mes.OrderPath)
                          && descriptor.DefaultValue == "/dev/dev/get/order");
        Assert.Contains(
            result.ModuleParamRegistry.GetDescriptors(ExpectedModuleId, ModuleParamCategory.Mes),
            descriptor => descriptor.Name == nameof(DieCuttingParams.Mes.BatchNumberPath)
                          && descriptor.DefaultValue == "/dev/dev/get/batchNumber");
        Assert.Contains(
            result.ModuleParamRegistry.GetDescriptors(ExpectedModuleId, ModuleParamCategory.Mes),
            descriptor => descriptor.Name == nameof(DieCuttingParams.Mes.InboundPath)
                          && descriptor.DefaultValue == "/dev/dev/electrode/getIn/check");
        Assert.Contains(
            result.ModuleParamRegistry.GetDescriptors(ExpectedModuleId, ModuleParamCategory.Mes),
            descriptor => descriptor.Name == nameof(DieCuttingParams.Mes.EquipmentStatusPath)
                          && descriptor.DefaultValue == "/dev/dev/realTime/status");
        Assert.Empty(result.ModuleParamRegistry.GetDescriptors(ExpectedModuleId, ModuleParamCategory.Cloud));
        Assert.Contains(
            result.Services,
            descriptor => descriptor.ServiceType == typeof(IProductionPlanSelectionService)
                          && descriptor.ImplementationFactory is not null);
    }

    [Fact]
    public void MesIdentity_WhenCodeIsMissing_ShouldReserveEmptyDeviceCodeByDefault()
    {
        var identity = new DieCuttingMesIdentityOptions().Resolve(ExpectedFirstDevice);

        Assert.Equal(string.Empty, identity.DeviceCode);
        Assert.Equal(ExpectedFirstDevice, identity.DeviceName);
        Assert.Equal(ExpectedFirstDevice, identity.UpperComputerNo);
    }

    [Fact]
    public void RealtimeSnapshotFingerprint_WhenSpeedOrUnwindingLengthChanges_ShouldChange()
    {
        var baseline = CreateFingerprintSnapshot();
        var speedChanged = CreateFingerprintSnapshot();
        speedChanged.PunchingSpeed += 1;
        var unwindingChanged = CreateFingerprintSnapshot();
        unwindingChanged.UnwindingLength += 1;

        Assert.NotEqual(baseline.CreateOutboundFingerprint(), speedChanged.CreateOutboundFingerprint());
        Assert.NotEqual(baseline.CreateOutboundFingerprint(), unwindingChanged.CreateOutboundFingerprint());
    }

    [Fact]
    public void HardwareProfile_ShouldOverrideBatchAddressByDevice()
    {
        var result = new ModuleContractFixture().RegisterModule(new TModule());
        using var provider = result.Services.BuildServiceProvider();
        var hardwareProfile = provider.GetRequiredService<IModuleHardwareProfileProvider>();
        var batchTemplate = hardwareProfile.GetDefaultIoTemplate()
            .Single(static x => x.SignalKey == "DieCutting.BatchNumber");

        Assert.Equal("R9660", hardwareProfile.ResolveIoTemplateForDevice(ExpectedFirstDevice, batchTemplate).PlcAddress);
        Assert.Equal("R9600", hardwareProfile.ResolveIoTemplateForDevice(ExpectedLastDevice, batchTemplate).PlcAddress);
    }

    [Fact]
    public void MachineProfile_ShouldSeedTwelveMesDeviceCodesFromMesDocument()
    {
        var machineProfilePath = Path.Combine(
            ContractTestPathHelper.FindRepoRoot(),
            "src",
            "Edge",
            "IIoT.Edge.Shell",
            ExpectedMachineProfileFileName);

        using var document = JsonDocument.Parse(File.ReadAllText(machineProfilePath));
        var modules = document.RootElement.GetProperty("Modules");
        var enabledModules = modules.GetProperty("Enabled").EnumerateArray()
            .Select(static x => x.GetString() ?? string.Empty)
            .ToArray();
        var mesIdentity = modules
            .GetProperty(ExpectedModuleId)
            .GetProperty("Module")
            .GetProperty("MesIdentity");
        var devices = mesIdentity.GetProperty("Devices");

        Assert.Equal([ExpectedModuleId], enabledModules);
        Assert.False(mesIdentity.GetProperty("UseDeviceNameWhenCodeMissing").GetBoolean());
        Assert.Equal(12, devices.EnumerateObject().Count());
        AssertSeededMesIdentity(devices, ExpectedFirstDevice, ExpectedUpperComputerNo);
        AssertSeededMesIdentity(devices, ExpectedLastDevice, ExpectedUpperComputerNo);
    }

    [Fact]
    public async Task MesChannel_UploadRealtime_ShouldPostTraceOutboundPayload()
    {
        var result = new ModuleContractFixture().RegisterModule(new TModule());
        using var provider = result.Services.BuildServiceProvider();
        var definition = provider.GetRequiredService<DieCuttingModuleDefinition>();
        var httpClient = new CapturingMesHttpClient();
        var roleProvider = new ContractModuleParamRoleProvider(ExpectedFirstDevice);
        var channel = new DieCuttingMesChannel(
            definition,
            new MesRequestExecutor(
                httpClient,
                new ContractMesEndpointProvider(),
                roleProvider,
                new ContractLogService()),
            roleProvider,
            new ContractDieCuttingModuleParamProvider(
                ExpectedModuleId,
                ExpectedUpperComputerNo,
                ExpectedOperationCode),
            new ContractLogService(),
            new ContractProductionTimeProvider());
        var snapshot = new DieCuttingRealtimeSnapshot
        {
            CapturedAt = new DateTime(2026, 6, 24, 10, 1, 2),
            WindowStartAt = new DateTime(2026, 6, 24, 10, 0, 0),
            WindowCompleteAt = new DateTime(2026, 6, 24, 10, 1, 0),
            ClipNo = "MG-01",
            PunchingDeviceCode = ExpectedFirstDevice,
            PunchingDeviceName = ExpectedFirstDevice,
            PunchingQuantity = 123,
            PunchingSpeed = 45.6m,
            PunchingLotNumber = "TRACE-AP-001"
        };

        var uploadResult = await channel.UploadRealtimeAsync(
            new DeviceSession
            {
                DeviceId = Guid.NewGuid(),
                ProcessId = Guid.NewGuid(),
                DeviceName = ExpectedFirstDevice,
                ClientCode = ExpectedUpperComputerNo
            },
            snapshot,
            TestContext.Current.CancellationToken);

        Assert.True(uploadResult.IsSuccess, uploadResult.Message);
        Assert.Equal("/dev/dev/electrode/exit/push", httpClient.LastUrl);

        using var payloadJson = JsonDocument.Parse(JsonSerializer.Serialize(httpClient.LastPayload));
        var root = payloadJson.RootElement;
        Assert.Equal(ExpectedUpperComputerNo, root.GetProperty("upperComputerNo").GetString());
        var timestamp = root.GetProperty("timestamp").GetString()!;
        Assert.Equal(
            BuildExpectedSign(ExpectedUpperComputerNo, timestamp, ExpectedMesSignSecret),
            root.GetProperty("sign").GetString());
        Assert.Equal(ExpectedFirstDevice, root.GetProperty("stationNo").GetString());
        Assert.Equal(ExpectedOperationCode, root.GetProperty("operationCode").GetString());
        Assert.Equal("TRACE-AP-001", root.GetProperty("batchNumber").GetString());
        var produce = root.GetProperty("data").GetProperty("produce").EnumerateArray().ToArray();
        Assert.Contains(produce, item =>
            item.GetProperty("code").GetString() == "punchingLotNumber"
            && item.GetProperty("val").GetString() == "TRACE-AP-001");
        Assert.Contains(produce, item =>
            item.GetProperty("code").GetString() == "punchingDeviceCode"
            && item.GetProperty("val").GetString() == ExpectedFirstDevice);
        Assert.Contains(produce, item =>
            item.GetProperty("code").GetString() == "punchingQuantity"
            && item.GetProperty("val").GetString() == "123");
        Assert.Contains(produce, item =>
            item.GetProperty("code").GetString() == "polePieceLength"
            && item.GetProperty("val").GetString() == string.Empty);
    }

    [Fact]
    public async Task MesChannel_WhenSignSecretMissing_ShouldReturnInvalidContextWithoutHttp()
    {
        var result = new ModuleContractFixture().RegisterModule(new TModule());
        using var provider = result.Services.BuildServiceProvider();
        var definition = provider.GetRequiredService<DieCuttingModuleDefinition>();
        var httpClient = new CapturingMesHttpClient();
        var roleProvider = new ContractModuleParamRoleProvider(ExpectedFirstDevice, mesSignSecret: null);
        var channel = new DieCuttingMesChannel(
            definition,
            new MesRequestExecutor(
                httpClient,
                new ContractMesEndpointProvider(),
                roleProvider,
                new ContractLogService()),
            roleProvider,
            new ContractDieCuttingModuleParamProvider(
                ExpectedModuleId,
                ExpectedUpperComputerNo,
                ExpectedOperationCode),
            new ContractLogService(),
            new ContractProductionTimeProvider());

        var uploadResult = await channel.UploadRealtimeAsync(
            new DeviceSession
            {
                DeviceId = Guid.NewGuid(),
                ProcessId = Guid.NewGuid(),
                DeviceName = ExpectedFirstDevice,
                ClientCode = ExpectedUpperComputerNo
            },
            new DieCuttingRealtimeSnapshot
            {
                CapturedAt = new DateTime(2026, 6, 24, 10, 1, 2),
                WindowStartAt = new DateTime(2026, 6, 24, 10, 0, 0),
                WindowCompleteAt = new DateTime(2026, 6, 24, 10, 1, 0),
                ClipNo = "MG-01",
                PunchingDeviceCode = ExpectedFirstDevice,
                PunchingDeviceName = ExpectedFirstDevice,
                PunchingQuantity = 123,
                PunchingSpeed = 45.6m,
                PunchingLotNumber = "TRACE-AP-001"
            },
            TestContext.Current.CancellationToken);

        Assert.Equal(MesCallOutcome.InvalidContext, uploadResult.Outcome);
        Assert.Contains("未配置 MES 签名密钥", uploadResult.Message, StringComparison.Ordinal);
        Assert.Null(httpClient.LastUrl);
    }

    [Fact]
    public async Task MesChannel_UploadEquipmentStatus_ShouldPostRealtimeStatusPayload()
    {
        var result = new ModuleContractFixture().RegisterModule(new TModule());
        using var provider = result.Services.BuildServiceProvider();
        var definition = provider.GetRequiredService<DieCuttingModuleDefinition>();
        var httpClient = new CapturingMesHttpClient();
        var roleProvider = new ContractModuleParamRoleProvider(ExpectedFirstDevice);
        var channel = new DieCuttingMesChannel(
            definition,
            new MesRequestExecutor(
                httpClient,
                new ContractMesEndpointProvider(),
                roleProvider,
                new ContractLogService()),
            roleProvider,
            new ContractDieCuttingModuleParamProvider(
                ExpectedModuleId,
                ExpectedUpperComputerNo,
                ExpectedOperationCode),
            new ContractLogService(),
            new ContractProductionTimeProvider());

        var uploadResult = await channel.UploadEquipmentStatusAsync(
            new DeviceSession
            {
                DeviceId = Guid.NewGuid(),
                ProcessId = Guid.NewGuid(),
                DeviceName = ExpectedFirstDevice,
                ClientCode = ExpectedUpperComputerNo
            },
            new DieCuttingDeviceStatusSnapshot
            {
                CapturedAt = new DateTime(2026, 6, 24, 10, 1, 2),
                StatusCode = 0,
                Messages = []
            },
            TestContext.Current.CancellationToken);

        Assert.True(uploadResult.IsSuccess, uploadResult.Message);
        Assert.Equal("/dev/dev/realTime/status", httpClient.LastUrl);
        using var payloadJson = JsonDocument.Parse(JsonSerializer.Serialize(httpClient.LastPayload));
        var device = payloadJson.RootElement
            .GetProperty("data")
            .GetProperty("devices")
            .EnumerateArray()
            .Single();
        Assert.Equal(ExpectedFirstDevice, device.GetProperty("stationNo").GetString());
        Assert.Equal(0, device.GetProperty("status").GetInt32());
    }

    [Fact]
    public async Task MesChannel_GetMainPlan_ShouldUseOrderPathAndUpperComputerNo()
    {
        var result = new ModuleContractFixture().RegisterModule(new TModule());
        using var provider = result.Services.BuildServiceProvider();
        var definition = provider.GetRequiredService<DieCuttingModuleDefinition>();
        var httpClient = new CapturingMesHttpClient
        {
            GetResponse = """{"code":200,"msg":"OK","data":{"orders":[[{"code":"orderNo","name":"主批次号","val":"MP-001"}]]}}"""
        };
        var roleProvider = new ContractModuleParamRoleProvider(ExpectedFirstDevice);
        var channel = new DieCuttingMesChannel(
            definition,
            new MesRequestExecutor(
                httpClient,
                new ContractMesEndpointProvider(),
                roleProvider,
                new ContractLogService()),
            roleProvider,
            new ContractDieCuttingModuleParamProvider(
                ExpectedModuleId,
                ExpectedUpperComputerNo,
                ExpectedOperationCode),
            new ContractLogService(),
            new ContractProductionTimeProvider());

        var plans = await channel.GetMainPlanAsync(
            new DieCuttingMainPlanRequest(ExpectedUpperComputerNo, new DateTime(2026, 6, 24, 10, 0, 0)),
            TestContext.Current.CancellationToken);

        Assert.True(plans.IsSuccess, plans.Message);
        Assert.StartsWith("/dev/dev/get/order?", httpClient.LastUrl, StringComparison.Ordinal);
        Assert.Contains($"upperComputerNo={ExpectedUpperComputerNo}", httpClient.LastUrl, StringComparison.Ordinal);
        Assert.Equal("MP-001", plans.Data!.Orders.Single().Single().Value);
    }

    [Fact]
    public async Task MesChannel_GenerateTraceBatchNumber_ShouldUseBatchNumberPathAndPayload()
    {
        var result = new ModuleContractFixture().RegisterModule(new TModule());
        using var provider = result.Services.BuildServiceProvider();
        var definition = provider.GetRequiredService<DieCuttingModuleDefinition>();
        var httpClient = new CapturingMesHttpClient
        {
            PostResponse = """{"code":200,"msg":"OK","data":{"batchNumber":"TRACE-001"}}"""
        };
        var roleProvider = new ContractModuleParamRoleProvider(ExpectedFirstDevice);
        var channel = new DieCuttingMesChannel(
            definition,
            new MesRequestExecutor(
                httpClient,
                new ContractMesEndpointProvider(),
                roleProvider,
                new ContractLogService()),
            roleProvider,
            new ContractDieCuttingModuleParamProvider(
                ExpectedModuleId,
                ExpectedUpperComputerNo,
                ExpectedOperationCode),
            new ContractLogService(),
            new ContractProductionTimeProvider());

        var batch = await channel.GenerateTraceBatchNumberAsync(
            new DieCuttingTraceBatchRequest("MP-001", ExpectedOperationCode),
            TestContext.Current.CancellationToken);

        Assert.True(batch.IsSuccess, batch.Message);
        Assert.Equal("/dev/dev/get/batchNumber", httpClient.LastUrl);
        Assert.Equal("TRACE-001", batch.Data!.BatchNumber);
        using var payloadJson = JsonDocument.Parse(JsonSerializer.Serialize(httpClient.LastPayload));
        Assert.Equal("MP-001", payloadJson.RootElement.GetProperty("masterPlanCode").GetString());
        Assert.Equal(ExpectedOperationCode, payloadJson.RootElement.GetProperty("operationCode").GetString());
    }

    [Fact]
    public async Task MesChannel_UploadRealtime_WhenOutboundPathMissing_ShouldFailWithoutPosting()
    {
        var result = new ModuleContractFixture().RegisterModule(new TModule());
        using var provider = result.Services.BuildServiceProvider();
        var definition = provider.GetRequiredService<DieCuttingModuleDefinition>();
        var httpClient = new CapturingMesHttpClient();
        var roleProvider = new ContractModuleParamRoleProvider(ExpectedFirstDevice);
        var channel = new DieCuttingMesChannel(
            definition,
            new MesRequestExecutor(
                httpClient,
                new ContractMesEndpointProvider(),
                roleProvider,
                new ContractLogService()),
            roleProvider,
            new ContractDieCuttingModuleParamProvider(
                ExpectedModuleId,
                ExpectedUpperComputerNo,
                ExpectedOperationCode,
                outboundPath: string.Empty),
            new ContractLogService(),
            new ContractProductionTimeProvider());

        var uploadResult = await channel.UploadRealtimeAsync(
            new DeviceSession
            {
                DeviceId = Guid.NewGuid(),
                ProcessId = Guid.NewGuid(),
                DeviceName = ExpectedFirstDevice,
                ClientCode = ExpectedUpperComputerNo
            },
            new DieCuttingRealtimeSnapshot
            {
                CapturedAt = new DateTime(2026, 6, 24, 10, 1, 2),
                WindowStartAt = new DateTime(2026, 6, 24, 10, 0, 0),
                WindowCompleteAt = new DateTime(2026, 6, 24, 10, 1, 0),
                ClipNo = "MG-01",
                PunchingDeviceCode = ExpectedFirstDevice,
                PunchingDeviceName = ExpectedFirstDevice,
                PunchingQuantity = 123,
                PunchingSpeed = 45.6m,
                PunchingLotNumber = "TRACE-001"
            },
            TestContext.Current.CancellationToken);

        Assert.Equal(MesCallOutcome.InvalidContext, uploadResult.Outcome);
        Assert.Null(httpClient.LastUrl);
    }

    private static void AssertSeededMesIdentity(JsonElement devices, string deviceCode, string upperComputerNo)
    {
        var identity = devices.GetProperty(deviceCode);
        Assert.Equal(deviceCode, identity.GetProperty("DeviceCode").GetString());
        Assert.Equal(deviceCode, identity.GetProperty("DeviceName").GetString());
        Assert.Equal(upperComputerNo, identity.GetProperty("UpperComputerNo").GetString());
    }

    private static string BuildExpectedSign(string upperComputerNo, string timestamp, string signToken)
    {
        var key = Encoding.UTF8.GetBytes(signToken);
        var bytes = Encoding.UTF8.GetBytes($"{upperComputerNo}{timestamp}");
        var hash = HMACSHA256.HashData(key, bytes);
        var builder = new StringBuilder(hash.Length * 2);

        foreach (var value in hash)
        {
            builder.Append(value.ToString("X2"));
        }

        return builder.ToString();
    }

    private static void AssertDefaultDevice(DieCuttingDeviceSeed device, string deviceName, string ipAddress)
    {
        Assert.Equal(deviceName, device.DeviceName);
        Assert.Equal(deviceName, device.DeviceCode);
        Assert.Equal(deviceName, device.DeviceDisplayName);
        Assert.Equal(ipAddress, device.IpAddress);
        Assert.Equal(65531, device.Port1);
    }

    private static DieCuttingRealtimeSnapshot CreateFingerprintSnapshot()
        => new()
        {
            PunchingQuantity = 100,
            PunchingSpeed = 12.5m,
            UnwindingLength = 3456,
            BatchNumber = "BATCH-1",
            ClipNoMg1 = "MG1",
            ClipNoMg2 = "MG2",
            Mg1ReceivingActual = 10,
            Mg2ReceivingActual = 20,
            OkSheetQuantity = 30,
            OperatorCode = "OP",
            MoldCode = "MOLD",
            CutterCode = "CUTTER"
        };

    private (IPlcTask Task, ServiceProvider Provider) CreateRealtimeSampleTask(
        ILogService logService,
        IPlcConnectionManager plcConnectionManager,
        IModuleParamProvider<DieCuttingParams.Mes, DieCuttingParams.Cloud, DieCuttingParams.Business> parameters)
    {
        var runtime = CreateRealtimeSampleRuntime(
            logService,
            plcConnectionManager,
            parameters,
            recordStore: null,
            mesChannel: null);
        return (runtime.Task, runtime.Provider);
    }

    private RealtimeSampleRuntime CreateRealtimeSampleRuntime(
        ILogService logService,
        IPlcConnectionManager plcConnectionManager,
        IModuleParamProvider<DieCuttingParams.Mes, DieCuttingParams.Cloud, DieCuttingParams.Business> parameters,
        IDieCuttingProductionRecordStore? recordStore,
        IDieCuttingMesScenarioChannel? mesChannel,
        IDataPipelineService? dataPipelineService = null,
        IMesHttpClient? mesHttpClient = null)
        => CreateTaskRuntime(
            static definition => definition.RealtimeSampleUploadTaskKey,
            logService,
            plcConnectionManager,
            parameters,
            recordStore,
            mesChannel,
            dataPipelineService,
            mesHttpClient);

    private RealtimeSampleRuntime CreateDeviceStatusRuntime(
        ILogService logService,
        IPlcConnectionManager plcConnectionManager,
        IModuleParamProvider<DieCuttingParams.Mes, DieCuttingParams.Cloud, DieCuttingParams.Business> parameters,
        IDataPipelineService dataPipelineService)
        => CreateTaskRuntime(
            static definition => definition.DeviceStatusUploadTaskKey,
            logService,
            plcConnectionManager,
            parameters,
            recordStore: null,
            mesChannel: null,
            dataPipelineService: dataPipelineService);

    private RealtimeSampleRuntime CreateTaskRuntime(
        Func<DieCuttingModuleDefinition, string> resolveTaskKey,
        ILogService logService,
        IPlcConnectionManager plcConnectionManager,
        IModuleParamProvider<DieCuttingParams.Mes, DieCuttingParams.Cloud, DieCuttingParams.Business> parameters,
        IDieCuttingProductionRecordStore? recordStore,
        IDieCuttingMesScenarioChannel? mesChannel,
        IDataPipelineService? dataPipelineService = null,
        IMesHttpClient? mesHttpClient = null)
    {
        var result = new ModuleContractFixture().RegisterModule(new TModule());
        Assert.True(result.RuntimeRegistry.TryGetFactory(ExpectedModuleId, out var factory));

        var services = new ServiceCollection();
        foreach (var descriptor in result.Services)
        {
            ((ICollection<ServiceDescriptor>)services).Add(descriptor);
        }

        ConfigureRuntimeServices(services);
        services.AddSingleton(logService);
        services.AddSingleton(plcConnectionManager);
        services.AddSingleton(parameters);
        services.AddSingleton<ICloudExecutionPolicy>(Assert.IsAssignableFrom<ICloudExecutionPolicy>(parameters));
        if (recordStore is not null)
        {
            services.AddSingleton(recordStore);
        }

        if (mesChannel is not null)
        {
            services.AddSingleton(mesChannel);
        }

        if (dataPipelineService is not null)
        {
            services.AddSingleton(dataPipelineService);
        }

        if (mesHttpClient is not null)
        {
            services.AddSingleton(mesHttpClient);
        }

        var provider = services.BuildServiceProvider();
        var buffer = new PlcBuffer(128, 16);
        var context = new DieCuttingContext
        {
            DeviceName = ExpectedFirstDevice,
            NetworkDeviceId = 1
        };
        var tasks = factory.CreateTasks(
            provider,
            buffer,
            context,
            factory.GetTaskCandidates().Select(static x => x.Key).ToHashSet(StringComparer.OrdinalIgnoreCase));
        var definition = provider.GetRequiredService<DieCuttingModuleDefinition>();
        var taskKey = resolveTaskKey(definition);
        var task = Assert.Single(tasks, task => string.Equals(task.TaskName, taskKey, StringComparison.OrdinalIgnoreCase));

        return new RealtimeSampleRuntime(task, provider, buffer, context);
    }

    private static void SeedRealtimeSignals(PlcBuffer buffer)
    {
        foreach (var signalKey in DieCuttingSignalCodec.RequiredSignalKeys)
        {
            buffer.UpdateReadSignal(signalKey, [0]);
        }

        SetReadInt16(buffer, DieCuttingPlcSignals.SingleRead.设备状态, 1);
        SetReadInt32(buffer, DieCuttingPlcSignals.SingleRead.实际产量, 100);
        SetReadInt32(buffer, DieCuttingPlcSignals.SingleRead.冲切速度, 1_250_000);
        SetReadInt32(buffer, DieCuttingPlcSignals.SingleRead.放卷长度, 3456);
        SetReadUInt16(buffer, DieCuttingPlcSignals.SingleRead.收料片数MG1实际, 10);
        SetReadUInt16(buffer, DieCuttingPlcSignals.SingleRead.收料片数MG2实际, 20);
        SetReadInt32(buffer, DieCuttingPlcSignals.SingleRead.弹夹OK级片数量, 30);
        SetReadAscii(buffer, DieCuttingPlcSignals.ContinuousRead.批次号, "BATCH-1", 8);
        SetReadAscii(buffer, DieCuttingPlcSignals.ContinuousRead.弹夹号MG1, "MG1", 11);
        SetReadAscii(buffer, DieCuttingPlcSignals.ContinuousRead.操作员工号, "OP", 5);
        SetReadAscii(buffer, DieCuttingPlcSignals.ContinuousRead.模具编号, "MOLD", 5);
        SetReadAscii(buffer, DieCuttingPlcSignals.ContinuousRead.切刀编号, "CUTTER", 5);
    }

    private static void SetReadUInt16(
        PlcBuffer buffer,
        DieCuttingPlcSignals.SingleRead key,
        ushort value)
        => buffer.UpdateReadSignal(EnumPlcSignalMetadata.GetRead(key).SignalKey, [value]);

    private static void SetReadInt16(
        PlcBuffer buffer,
        DieCuttingPlcSignals.SingleRead key,
        short value)
        => SetReadUInt16(buffer, key, unchecked((ushort)value));

    private static void SetReadInt32(
        PlcBuffer buffer,
        DieCuttingPlcSignals.SingleRead key,
        int value)
        => buffer.UpdateReadSignal(
            EnumPlcSignalMetadata.GetRead(key).SignalKey,
            [unchecked((ushort)value), unchecked((ushort)(value >> 16))]);

    private static void SetReadAscii(
        PlcBuffer buffer,
        DieCuttingPlcSignals.ContinuousRead key,
        string value,
        int wordCount)
    {
        var bytes = Encoding.ASCII.GetBytes(value);
        var words = new ushort[wordCount];
        for (var index = 0; index < bytes.Length && index / 2 < words.Length; index++)
        {
            var wordIndex = index / 2;
            if (index % 2 == 0)
            {
                words[wordIndex] = bytes[index];
            }
            else
            {
                words[wordIndex] |= (ushort)(bytes[index] << 8);
            }
        }

        buffer.UpdateReadSignal(EnumPlcSignalMetadata.GetRead(key).SignalKey, words);
    }

    private static async Task InvokeTaskCoreOnceAsync(IPlcTask task)
    {
        var method = task.GetType().GetMethod("DoCoreAsync", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);
        await Assert.IsAssignableFrom<Task>(method!.Invoke(task, []));
    }

    private static bool ContainsSensitiveMesCredentialText(LogEntry entry)
        => entry.Message.Contains("token", StringComparison.OrdinalIgnoreCase)
           || entry.Message.Contains("sign=", StringComparison.OrdinalIgnoreCase)
           || entry.Message.Contains("密钥", StringComparison.OrdinalIgnoreCase);

    private static ProductionPlanOption CreatePlanOption(string mainPlanCode)
        => new(
            Id: mainPlanCode,
            MainPlanCode: mainPlanCode,
            WorkOrderCode: string.Empty,
            ErpOrderCode: string.Empty,
            ProductCode: "PRODUCT-1",
            ProductName: "测试产品",
            PlanStatus: "RUNNING",
            ProcessCode: string.Empty,
            ProcessName: string.Empty,
            LineCode: string.Empty,
            LineName: string.Empty,
            PlannedQuantity: string.Empty,
            CompletedQuantity: string.Empty,
            Unit: string.Empty,
            ProductModel: string.Empty,
            StartTime: string.Empty,
            EndTime: string.Empty,
            Fields: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["orderNo"] = mainPlanCode
            });

    private sealed record RealtimeSampleRuntime(
        IPlcTask Task,
        ServiceProvider Provider,
        PlcBuffer Buffer,
        DieCuttingContext Context);

    private string ExpectedDeviceName(int index)
        => $"{ExpectedFirstDevice[..^2]}{index:D2}";

    private string ExpectedIpAddress(int index)
    {
        var firstDeviceSuffix = "01.local";
        return ExpectedFirstIpAddress.EndsWith(firstDeviceSuffix, StringComparison.Ordinal)
            ? $"{ExpectedFirstIpAddress[..^firstDeviceSuffix.Length]}{index:D2}.local"
            : ExpectedFirstIpAddress;
    }

    private IConfiguration CreateEnabledSeedConfiguration(bool resetBeforeImport)
        => new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [$"Modules:{ExpectedModuleId}:DeviceSeed:Enabled"] = "true",
                [$"Modules:{ExpectedModuleId}:DeviceSeed:ResetBeforeImport"] = resetBeforeImport.ToString()
            })
            .Build();

    private static IoMappingEntity CreateIoMapping(
        int deviceId,
        string signalKey,
        string address,
        int addressCount,
        string dataType)
        => IoMappingEntity.Create(
            deviceId,
            signalKey,
            address,
            addressCount,
            dataType,
            "Read",
            "连续读数据",
            "MES采样");

    private static int SeedableTemplateCount(IModuleHardwareProfileProvider hardwareProfile)
        => hardwareProfile
            .GetDefaultIoTemplate()
            .Count(static template => !string.IsNullOrWhiteSpace(template.PlcAddress));

    private static IReadOnlyCollection<ModuleIoSnapshot> CreateStandardIoSnapshots(
        IModuleHardwareProfileProvider hardwareProfile)
        => hardwareProfile
            .GetDefaultIoTemplate()
            .Where(static template => !string.IsNullOrWhiteSpace(template.PlcAddress))
            .Select(static template => new ModuleIoSnapshot(
                template.SignalKey,
                template.PlcAddress,
                template.AddressCount,
                template.DataType,
                template.Direction,
                template.SortOrder,
                template.Category,
                template.BusinessGroup))
            .ToArray();

    private sealed class InMemoryRepository<T> : IRepository<T>
        where T : class, IEntity<int>, IAggregateRoot
    {
        private int _nextId = 1;

        public List<T> Items { get; } = [];

        public IQueryable<T> GetQueryable() => Items.AsQueryable();

        public T Add(T entity)
        {
            if (entity.Id == 0)
            {
                SetId(entity, _nextId++);
            }

            Items.Add(entity);
            return entity;
        }

        public void Update(T entity)
        {
        }

        public void Delete(T entity)
            => Items.Remove(entity);

        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(1);

        public Task<int> ExecuteDeleteAsync(
            Expression<Func<T, bool>> predicate,
            CancellationToken cancellationToken = default)
        {
            var compiled = predicate.Compile();
            var deleted = Items.RemoveAll(item => compiled(item));
            return Task.FromResult(deleted);
        }

        public Task<T?> GetByIdAsync<TKey>(TKey id, CancellationToken cancellationToken = default)
            where TKey : notnull
            => Task.FromResult(Items.FirstOrDefault(item => EqualityComparer<object>.Default.Equals(item.Id, id)));

        public Task<T?> GetAsync(
            Expression<Func<T, bool>> expression,
            Expression<Func<T, object>>[]? includes = null,
            CancellationToken cancellationToken = default)
            => Task.FromResult(Items.FirstOrDefault(expression.Compile()));

        public Task<List<T>> GetListAsync(
            Expression<Func<T, bool>> expression,
            CancellationToken cancellationToken = default)
            => Task.FromResult(Items.Where(expression.Compile()).ToList());

        public Task<List<T>> GetListAsync(
            Expression<Func<T, bool>> expression,
            Expression<Func<T, object>>[]? includes = null,
            CancellationToken cancellationToken = default)
            => Task.FromResult(Items.Where(expression.Compile()).ToList());

        public Task<List<T>> GetListAsync(
            ISpecification<T>? specification = null,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<T?> GetSingleOrDefaultAsync(
            ISpecification<T>? specification = null,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<int> GetCountAsync(
            Expression<Func<T, bool>> expression,
            CancellationToken cancellationToken = default)
            => Task.FromResult(Items.Count(expression.Compile()));

        public Task<int> CountAsync(
            ISpecification<T>? specification = null,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<bool> AnyAsync(
            ISpecification<T>? specification = null,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        private static void SetId(T entity, int id)
        {
            var property = typeof(BaseEntity<int>).GetProperty(nameof(BaseEntity<int>.Id))
                ?? throw new InvalidOperationException("测试内存仓储无法定位实体 Id 属性。");
            property.SetValue(entity, id);
        }
    }

    private sealed class ContractMesUploadDiagnosticsStore : IMesUploadDiagnosticsStore
    {
        public IReadOnlyList<MesChannelDiagnostics> GetAll() => [];
        public MesChannelDiagnostics? Get(string processType) => null;
        public void RecordSuccess(string processType, MesUploadDiagnosticsContext? context = null) { }
        public void RecordFailure(string processType, string failureReason, MesUploadDiagnosticsContext? context = null) { }
        public void RecordBlocked(string processType, string blockedReason, MesUploadDiagnosticsContext? context = null) { }
    }

    private sealed class ContractCloudUploadDiagnosticsStore : ICloudUploadDiagnosticsStore
    {
        public CloudUploadDiagnosticsSnapshot Snapshot { get; private set; } = new(
            LastAttemptAt: null,
            LastSuccessAt: null,
            LastFailureAt: null,
            LastBlockedAt: null,
            LastOutcome: CloudCallOutcome.Success,
            LastReasonCode: "none",
            LastBlockedReason: null,
            LastProcessType: null,
            RuntimeState: CloudRetryRuntimeState.Idle,
            IsCapacityBlocked: false,
            BlockedChannel: null,
            BlockedReason: "none",
            LastCapacityBlockAt: null);

        public void RecordResult(
            string? processType,
            CloudCallResult result,
            CloudUploadDiagnosticsContext? context = null)
        {
            Snapshot = Snapshot with
            {
                LastAttemptAt = DateTime.UtcNow,
                LastProcessType = processType,
                LastOutcome = result.Outcome,
                LastReasonCode = result.ReasonCode,
                LastDeviceName = context?.DeviceName,
                LastModuleId = context?.ModuleId,
                LastTaskKey = context?.TaskKey,
                LastScenario = context?.Scenario
            };
        }

        public void RecordBlocked(
            string? processType,
            string reasonCode,
            string? blockedReason = null,
            CloudUploadDiagnosticsContext? context = null)
        {
            Snapshot = Snapshot with
            {
                LastAttemptAt = DateTime.UtcNow,
                LastBlockedAt = DateTime.UtcNow,
                LastProcessType = processType,
                LastOutcome = CloudCallOutcome.SkippedUploadNotReady,
                LastReasonCode = reasonCode,
                LastBlockedReason = blockedReason,
                LastDeviceName = context?.DeviceName,
                LastModuleId = context?.ModuleId,
                LastTaskKey = context?.TaskKey,
                LastScenario = context?.Scenario
            };
        }

        public void SetRuntimeState(CloudRetryRuntimeState state)
            => Snapshot = Snapshot with { RuntimeState = state };

        public void MarkCapacityBlocked(
            CapacityBlockedChannel channel,
            string blockedReason,
            string? processType = null,
            DateTime? occurredAt = null)
            => Snapshot = Snapshot with
            {
                IsCapacityBlocked = true,
                BlockedChannel = channel,
                BlockedReason = blockedReason,
                LastCapacityBlockAt = occurredAt ?? DateTime.UtcNow
            };

        public void ClearCapacityBlocked()
            => Snapshot = Snapshot with
            {
                IsCapacityBlocked = false,
                BlockedChannel = null,
                BlockedReason = "none"
            };
    }

    private sealed class InMemoryDieCuttingProductionRecordStore : IDieCuttingProductionRecordStore
    {
        private readonly List<DieCuttingProductionRecord> _records = [];

        public Task AddAsync(DieCuttingProductionRecord record, CancellationToken cancellationToken = default)
        {
            _records.Add(record);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<DieCuttingProductionRecord>> QueryAsync(
            string moduleId,
            string? deviceName = null,
            int limit = 200,
            CancellationToken cancellationToken = default)
        {
            var rows = _records
                .Where(record => string.Equals(record.ModuleId, moduleId, StringComparison.OrdinalIgnoreCase))
                .Where(record => string.IsNullOrWhiteSpace(deviceName)
                                 || string.Equals(record.DeviceName, deviceName, StringComparison.OrdinalIgnoreCase))
                .Take(limit)
                .ToArray();
            return Task.FromResult<IReadOnlyList<DieCuttingProductionRecord>>(rows);
        }
    }

    private sealed class CapturingDataPipelineService : IDataPipelineService
    {
        public List<CellCompletedRecord> Records { get; } = [];
        public DataPipelineEnqueueResult Result { get; set; } = DataPipelineEnqueueResult.Accepted();
        public Exception? ExceptionToThrow { get; set; }

        public int PendingCount => Records.Count;
        public int OverflowCount => 0;
        public int SpillCount => 0;

        public ValueTask<DataPipelineEnqueueResult> EnqueueAsync(
            CellCompletedRecord record,
            CancellationToken cancellationToken = default)
        {
            if (ExceptionToThrow is not null)
            {
                throw ExceptionToThrow;
            }

            Records.Add(record);
            return ValueTask.FromResult(Result);
        }

        public bool TryDequeue(out CellCompletedRecord? record)
        {
            if (Records.Count == 0)
            {
                record = null;
                return false;
            }

            record = Records[0];
            Records.RemoveAt(0);
            return true;
        }

        public ValueTask<bool> WaitToReadAsync(CancellationToken cancellationToken = default)
            => ValueTask.FromResult(Records.Count > 0);
    }

    private sealed class CountingDieCuttingMesChannel : IDieCuttingMesScenarioChannel
    {
        public CountingDieCuttingMesChannel(string processType)
        {
            ProcessType = processType;
        }

        public string ProcessType { get; }
        public ProcessUploadMode UploadMode => ProcessUploadMode.Single;
        public int RealtimeUploadCount { get; private set; }
        public int DeviceStatusUploadCount { get; private set; }

        public Task<MesCallResult> UploadAsync(
            ProcessUploadContext context,
            IReadOnlyList<IIoT.Edge.SharedKernel.DataPipeline.CellCompletedRecord> records,
            CancellationToken cancellationToken = default)
            => Task.FromResult(MesCallResult.Success());

        public Task<MesCallResult<DieCuttingMainPlan>> GetMainPlanAsync(
            DieCuttingMainPlanRequest request,
            CancellationToken cancellationToken = default)
            => Task.FromResult(MesCallResult<DieCuttingMainPlan>.Success(new DieCuttingMainPlan([])));

        public Task<MesCallResult<DieCuttingTraceBatchResult>> GenerateTraceBatchNumberAsync(
            DieCuttingTraceBatchRequest request,
            CancellationToken cancellationToken = default)
            => Task.FromResult(MesCallResult<DieCuttingTraceBatchResult>.Success(
                new DieCuttingTraceBatchResult("TRACE-TEST", default)));

        public Task<MesCallResult> UploadRealtimeAsync(
            DeviceSession? device,
            DieCuttingRealtimeSnapshot snapshot,
            CancellationToken cancellationToken = default)
        {
            RealtimeUploadCount++;
            return Task.FromResult(MesCallResult.Success());
        }

        public Task<MesCallResult> UploadEquipmentStatusAsync(
            DeviceSession? device,
            DieCuttingDeviceStatusSnapshot snapshot,
            CancellationToken cancellationToken = default)
        {
            DeviceStatusUploadCount++;
            return Task.FromResult(MesCallResult.Success());
        }
    }

    private sealed class ContractDieCuttingModuleParamProvider
        : IModuleParamProvider<DieCuttingParams.Mes, DieCuttingParams.Cloud, DieCuttingParams.Business>,
          ICloudExecutionPolicy
    {
        private readonly string _moduleId;
        private readonly string _upperComputerNo;
        private readonly string _operationCode;
        private readonly string _mesBaseUrl;
        private readonly string _outboundPath;

        public ContractDieCuttingModuleParamProvider(
            string moduleId,
            string upperComputerNo,
            string operationCode,
            string mesBaseUrl = TestMesBaseUrl,
            string outboundPath = "/dev/dev/electrode/exit/push",
            bool mesEnabled = true,
            bool cloudEnabled = false)
        {
            _moduleId = moduleId;
            _upperComputerNo = upperComputerNo;
            _operationCode = operationCode;
            _mesBaseUrl = mesBaseUrl;
            _outboundPath = outboundPath;
            MesEnabled = mesEnabled;
            CloudEnabled = cloudEnabled;
        }

        private bool MesEnabled { get; }

        private bool CloudEnabled { get; }

        public bool IsEnabled => CloudEnabled;

        public Task<ModuleParamSnapshot<DieCuttingParams.Mes, DieCuttingParams.Cloud, DieCuttingParams.Business>> GetAsync(
            CancellationToken cancellationToken = default)
            => Task.FromResult(new ModuleParamSnapshot<DieCuttingParams.Mes, DieCuttingParams.Cloud, DieCuttingParams.Business>(
                _moduleId,
                new ModuleParamGroup<DieCuttingParams.Mes>(
                    _moduleId,
                    ModuleParamCategory.Mes,
                    new Dictionary<DieCuttingParams.Mes, string>(),
                    new Dictionary<DieCuttingParams.Mes, string?>
                    {
                        [DieCuttingParams.Mes.启用] = MesEnabled ? "true" : "false",
                        [DieCuttingParams.Mes.服务地址] = _mesBaseUrl,
                        [DieCuttingParams.Mes.UpperComputerNo] = _upperComputerNo,
                        [DieCuttingParams.Mes.OperationCode] = _operationCode,
                        [DieCuttingParams.Mes.OrderPath] = "/dev/dev/get/order",
                        [DieCuttingParams.Mes.BatchNumberPath] = "/dev/dev/get/batchNumber",
                        [DieCuttingParams.Mes.OutboundPath] = _outboundPath,
                        [DieCuttingParams.Mes.EquipmentStatusPath] = "/dev/dev/realTime/status",
                        [DieCuttingParams.Mes.签名令牌] = ExpectedMesSignSecret
                    },
                    new Dictionary<DieCuttingParams.Mes, ParamValueKind>
                    {
                        [DieCuttingParams.Mes.启用] = ParamValueKind.Bool,
                        [DieCuttingParams.Mes.服务地址] = ParamValueKind.String,
                        [DieCuttingParams.Mes.UpperComputerNo] = ParamValueKind.String,
                        [DieCuttingParams.Mes.OperationCode] = ParamValueKind.String,
                        [DieCuttingParams.Mes.OrderPath] = ParamValueKind.String,
                        [DieCuttingParams.Mes.BatchNumberPath] = ParamValueKind.String,
                        [DieCuttingParams.Mes.OutboundPath] = ParamValueKind.String,
                        [DieCuttingParams.Mes.EquipmentStatusPath] = ParamValueKind.String,
                        [DieCuttingParams.Mes.签名令牌] = ParamValueKind.String
                    },
                    warn: null),
                new ModuleParamGroup<DieCuttingParams.Cloud>(
                    _moduleId,
                    ModuleParamCategory.Cloud,
                    new Dictionary<DieCuttingParams.Cloud, string>(),
                    new Dictionary<DieCuttingParams.Cloud, string?>(),
                    new Dictionary<DieCuttingParams.Cloud, ParamValueKind>(),
                    warn: null),
                new ModuleParamGroup<DieCuttingParams.Business>(
                    _moduleId,
                    ModuleParamCategory.Business,
                    new Dictionary<DieCuttingParams.Business, string>(),
                    new Dictionary<DieCuttingParams.Business, string?>
                    {
                        [DieCuttingParams.Business.采集频率毫秒] = "1000"
                    },
                    new Dictionary<DieCuttingParams.Business, ParamValueKind>
                    {
                        [DieCuttingParams.Business.采集频率毫秒] = ParamValueKind.Int
                    },
                    warn: null)));
    }

    private sealed class ContractDieCuttingMesChannel : IDieCuttingMesScenarioChannel
    {
        public ContractDieCuttingMesChannel(string processType)
        {
            ProcessType = processType;
        }

        public string ProcessType { get; }
        public ProcessUploadMode UploadMode => ProcessUploadMode.Single;

        public Task<MesCallResult> UploadAsync(
            ProcessUploadContext context,
            IReadOnlyList<IIoT.Edge.SharedKernel.DataPipeline.CellCompletedRecord> records,
            CancellationToken cancellationToken = default)
            => Task.FromResult(MesCallResult.Success());

        public Task<MesCallResult<DieCuttingMainPlan>> GetMainPlanAsync(
            DieCuttingMainPlanRequest request,
            CancellationToken cancellationToken = default)
            => Task.FromResult(MesCallResult<DieCuttingMainPlan>.Success(new DieCuttingMainPlan([])));

        public Task<MesCallResult<DieCuttingTraceBatchResult>> GenerateTraceBatchNumberAsync(
            DieCuttingTraceBatchRequest request,
            CancellationToken cancellationToken = default)
            => Task.FromResult(MesCallResult<DieCuttingTraceBatchResult>.Success(
                new DieCuttingTraceBatchResult("TRACE-TEST", default)));

        public Task<MesCallResult> UploadRealtimeAsync(
            DeviceSession? device,
            DieCuttingRealtimeSnapshot snapshot,
            CancellationToken cancellationToken = default)
            => Task.FromResult(MesCallResult.Success());

        public Task<MesCallResult> UploadEquipmentStatusAsync(
            DeviceSession? device,
            DieCuttingDeviceStatusSnapshot snapshot,
            CancellationToken cancellationToken = default)
            => Task.FromResult(MesCallResult.Success());
    }

    private sealed class CapturingMesHttpClient : IMesHttpClient
    {
        public string PostResponse { get; init; } = """{"code":200,"msg":"OK"}""";
        public string GetResponse { get; init; } = """{"code":200,"msg":"OK","data":{}}""";
        public string? LastUrl { get; private set; }
        public object? LastPayload { get; private set; }

        public Task<bool> PostAsync(
            string processType,
            string url,
            object payload,
            IReadOnlyDictionary<string, string>? headers = null,
            CancellationToken cancellationToken = default)
            => Task.FromResult(true);

        public Task<string?> PostWithResponseAsync(
            string processType,
            string url,
            object payload,
            IReadOnlyDictionary<string, string>? headers = null,
            CancellationToken cancellationToken = default)
        {
            LastUrl = url;
            LastPayload = payload;
            return Task.FromResult<string?>(PostResponse);
        }

        public Task<string?> GetAsync(
            string processType,
            string url,
            IReadOnlyDictionary<string, string>? headers = null,
            CancellationToken cancellationToken = default)
        {
            LastUrl = url;
            return Task.FromResult<string?>(GetResponse);
        }
    }

    private sealed class ContractMesEndpointProvider : IMesEndpointProvider
    {
        public Task<bool> IsConfiguredAsync(string processType, CancellationToken cancellationToken = default)
            => Task.FromResult(true);

        public Task<string> BuildUrlAsync(
            string processType,
            string relativeOrAbsoluteUrl,
            CancellationToken cancellationToken = default)
            => Task.FromResult($"{TestMesBaseUrl}{relativeOrAbsoluteUrl}");

        public Task<string?> TryBuildFirstConfiguredUrlAsync(
            IReadOnlyCollection<string> processTypes,
            string relativeOrAbsoluteUrl,
            CancellationToken cancellationToken = default)
            => Task.FromResult<string?>($"{TestMesBaseUrl}{relativeOrAbsoluteUrl}");

        public IReadOnlyDictionary<string, string> GetDefaultHeaders() => new Dictionary<string, string>();
    }

    private sealed class ContractModuleParamRoleProvider(
        string stationNo,
        string? mesSignSecret = ExpectedMesSignSecret) : IModuleParamRoleProvider
    {
        public Task<ModuleParamRoleValue?> GetAsync(
            string moduleId,
            ModuleParamCategory category,
            ModuleParamRole role,
            CancellationToken cancellationToken = default)
            => Task.FromResult(CreateValue(moduleId, category, role));

        public Task<IReadOnlyList<ModuleParamRoleValue>> GetAllAsync(
            ModuleParamCategory category,
            ModuleParamRole role,
            IReadOnlyCollection<string>? moduleIds = null,
            CancellationToken cancellationToken = default)
        {
            var values = (moduleIds ?? ["DieCutting"])
                .Select(moduleId => CreateValue(moduleId, category, role))
                .Where(static value => value is not null)
                .Cast<ModuleParamRoleValue>()
                .ToArray();
            return Task.FromResult<IReadOnlyList<ModuleParamRoleValue>>(values);
        }

        public Task<string?> GetStringAsync(
            string moduleId,
            ModuleParamCategory category,
            ModuleParamRole role,
            string? defaultValue = null,
            CancellationToken cancellationToken = default)
            => Task.FromResult(CreateValue(moduleId, category, role)?.Value ?? defaultValue);

        public Task<string?> FirstStringAsync(
            ModuleParamCategory category,
            ModuleParamRole role,
            IReadOnlyCollection<string>? moduleIds = null,
            CancellationToken cancellationToken = default)
            => Task.FromResult(CreateValue((moduleIds ?? ["DieCutting"]).First(), category, role)?.Value);

        public Task<bool> GetBoolAsync(
            string moduleId,
            ModuleParamCategory category,
            ModuleParamRole role,
            bool defaultValue = false,
            CancellationToken cancellationToken = default)
            => Task.FromResult(role == ModuleParamRole.MesEnabled || defaultValue);

        public Task<bool> AnyBoolAsync(
            ModuleParamCategory category,
            ModuleParamRole role,
            IReadOnlyCollection<string>? moduleIds = null,
            bool defaultValue = false,
            CancellationToken cancellationToken = default)
            => Task.FromResult(role == ModuleParamRole.MesEnabled || defaultValue);

        private ModuleParamRoleValue? CreateValue(
            string moduleId,
            ModuleParamCategory category,
            ModuleParamRole role)
        {
            if (category != ModuleParamCategory.Mes)
            {
                return null;
            }

            return role switch
            {
                ModuleParamRole.MesEnabled => Build("启用", "true", ParamValueKind.Bool),
                ModuleParamRole.StationNo => Build("工站编号", stationNo, ParamValueKind.String),
                ModuleParamRole.MesSignToken when !string.IsNullOrWhiteSpace(mesSignSecret) => Build("签名令牌", mesSignSecret, ParamValueKind.String),
                _ => null
            };

            ModuleParamRoleValue Build(string name, string value, ParamValueKind kind)
                => new(
                    moduleId,
                    category,
                    role,
                    kind,
                    name,
                    $"Module:{moduleId}:Mes:{name}",
                    value,
                    value);
        }
    }

    private sealed class ContractLogService : ILogService
    {
        public event Action<LogEntry>? EntryAdded;
        public void Debug(string message) => Raise(message);
        public void Info(string message) => Raise(message);
        public void Warn(string message) => Raise(message);
        public void Error(string message) => Raise(message);
        public void Fatal(string message) => Raise(message);
        private void Raise(string message) => EntryAdded?.Invoke(new LogEntry { Level = "Test", Message = message, Time = DateTime.UtcNow });
    }

    private sealed class ContractPlcConnectionManager : IPlcConnectionManager
    {
        public Task InitializeAsync(CancellationToken ct = default) => Task.CompletedTask;

        public Task StopAsync(CancellationToken ct = default) => Task.CompletedTask;

        public Task ReloadAsync(string deviceName, CancellationToken ct = default) => Task.CompletedTask;

        public Task StopDeviceAsync(int networkDeviceId, CancellationToken ct = default) => Task.CompletedTask;

        public void RegisterTasks(string deviceName, Func<IPlcBuffer, ProductionContext, List<IPlcTask>> factory)
        {
        }

        public IPlcService? GetPlc(int networkDeviceId) => null;

        public ProductionContext? GetContext(string deviceName) => null;

        public void MarkRuntimeFault(int networkDeviceId, string deviceName, string error)
        {
        }

        public PlcConnectionRuntimeSnapshot? GetRuntimeStatus(int networkDeviceId)
            => new()
            {
                NetworkDeviceId = networkDeviceId,
                DeviceName = "Contract-PLC",
                IsConnected = true
            };

        public IReadOnlyCollection<PlcConnectionRuntimeSnapshot> GetRuntimeStatuses() => [];

        public void Dispose()
        {
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class DisconnectedPlcConnectionManager : IPlcConnectionManager
    {
        public Task InitializeAsync(CancellationToken ct = default) => Task.CompletedTask;

        public Task StopAsync(CancellationToken ct = default) => Task.CompletedTask;

        public Task ReloadAsync(string deviceName, CancellationToken ct = default) => Task.CompletedTask;

        public Task StopDeviceAsync(int networkDeviceId, CancellationToken ct = default) => Task.CompletedTask;

        public void RegisterTasks(string deviceName, Func<IPlcBuffer, ProductionContext, List<IPlcTask>> factory)
        {
        }

        public IPlcService? GetPlc(int networkDeviceId) => null;

        public ProductionContext? GetContext(string deviceName) => null;

        public void MarkRuntimeFault(int networkDeviceId, string deviceName, string error)
        {
        }

        public PlcConnectionRuntimeSnapshot? GetRuntimeStatus(int networkDeviceId)
            => new()
            {
                NetworkDeviceId = networkDeviceId,
                DeviceName = "Contract-PLC",
                IsConnected = false,
                ConnectionState = PlcConnectionState.Disconnected
            };

        public IReadOnlyCollection<PlcConnectionRuntimeSnapshot> GetRuntimeStatuses() => [];

        public void Dispose()
        {
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class ContractProductionTimeProvider : IProductionTimeProvider
    {
        public TimeZoneInfo BusinessTimeZone { get; } = TimeZoneInfo.Local;
        public DateTime UtcNow => new(2026, 6, 24, 10, 0, 0, DateTimeKind.Utc);
        public DateTime BusinessNow => ToBusinessTime(UtcNow);
        public DateTime ToUtc(DateTime value) => value.Kind == DateTimeKind.Utc ? value : value.ToUniversalTime();
        public DateTime ToBusinessTime(DateTime value)
            => value.Kind == DateTimeKind.Utc ? TimeZoneInfo.ConvertTimeFromUtc(value, BusinessTimeZone) : value;
        public string FormatBusinessTimestamp(DateTime value) => ToBusinessTime(value).ToString("yyyy-MM-dd HH:mm:ss");
    }
}
