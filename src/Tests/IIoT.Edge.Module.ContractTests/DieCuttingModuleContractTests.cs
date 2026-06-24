using IIoT.Edge.Application.Abstractions.Config;
using IIoT.Edge.Application.Abstractions.Device;
using IIoT.Edge.Application.Abstractions.Logging;
using IIoT.Edge.Application.Abstractions.Mes;
using IIoT.Edge.Application.Abstractions.Modules;
using IIoT.Edge.Application.Abstractions.Time;
using IIoT.Edge.Application.Features.Production.Planning;
using IIoT.Edge.Application.Modules;
using IIoT.Edge.Module.DieCutting;
using IIoT.Edge.Module.DieCutting.Config;
using IIoT.Edge.Module.DieCutting.Config.Parameters;
using IIoT.Edge.Module.DieCutting.Mes;
using IIoT.Edge.Module.DieCutting.Payload;
using IIoT.Edge.Module.DieCutting.Production;
using IIoT.Edge.Module.DieCutting.Samples;
using IIoT.Edge.SharedKernel.Context;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
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
    protected override string ExpectedFirstIpAddress => "10.110.0.11";
    protected override string ExpectedLastIpAddress => "10.110.0.22";
    protected override string ExpectedUpperComputerNo => "P1-APUC";
    protected override string ExpectedOperationCode => "AP";
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
    protected override string ExpectedFirstIpAddress => "10.110.1.11";
    protected override string ExpectedLastIpAddress => "10.110.1.22";
    protected override string ExpectedUpperComputerNo => "P2-CPUC";
    protected override string ExpectedOperationCode => "CP";
}

public abstract class DieCuttingModuleContractTestsBase<TModule> : ModuleContractTestBase<TModule>
    where TModule : IEdgeProcessModule, new()
{
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

    protected override bool RequiresHardwareProfile => true;
    protected override bool RequiresMesUploader => true;
    protected override int ExpectedRuntimeTaskCount => 1;
    protected override int MinimumRouteCount => 6;

    protected override ProductionContext CreateRuntimeContext()
        => new DieCuttingContext { DeviceName = ExpectedFirstDevice };

    protected override void ConfigureRuntimeServices(IServiceCollection services)
    {
        AddDefaultRuntimeServices(services);
        services.AddSingleton<IMesUploadDiagnosticsStore, ContractMesUploadDiagnosticsStore>();
        services.AddSingleton<IMesHttpClient, CapturingMesHttpClient>();
        services.AddSingleton<IMesEndpointProvider, ContractMesEndpointProvider>();
        services.AddSingleton<IModuleParamRoleProvider>(new ContractModuleParamRoleProvider(ExpectedFirstDevice));
        services.AddSingleton<MesRequestExecutor>();
        services.AddSingleton<IDieCuttingMesScenarioChannel>(new ContractDieCuttingMesChannel(ExpectedModuleId));
        services.AddSingleton<IModuleParamProvider<DieCuttingParams.Mes, DieCuttingParams.Cloud, DieCuttingParams.Business>>(
            new ContractDieCuttingModuleParamProvider(
                ExpectedModuleId,
                ExpectedUpperComputerNo,
                ExpectedOperationCode));
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
            "IIoT.Edge.Module.DieCutting",
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
                Assert.True(device.IsEnabled);
            });
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
        Assert.DoesNotContain(
            result.ModuleParamRegistry.GetDescriptors(ExpectedModuleId, ModuleParamCategory.Mes),
            descriptor => descriptor.Role == ModuleParamRole.MesSignToken);
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
            BuildExpectedSign(ExpectedUpperComputerNo, timestamp, "hdc2023"),
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
        var bytes = Encoding.UTF8.GetBytes($"{upperComputerNo}{timestamp}{signToken}");
        var hash = MD5.HashData(bytes);
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
        Assert.Equal(65530, device.Port1);
    }

    private sealed class ContractMesUploadDiagnosticsStore : IMesUploadDiagnosticsStore
    {
        public IReadOnlyList<MesChannelDiagnostics> GetAll() => [];
        public MesChannelDiagnostics? Get(string processType) => null;
        public void RecordSuccess(string processType) { }
        public void RecordFailure(string processType, string failureReason) { }
    }

    private sealed class ContractDieCuttingModuleParamProvider
        : IModuleParamProvider<DieCuttingParams.Mes, DieCuttingParams.Cloud, DieCuttingParams.Business>
    {
        private readonly string _moduleId;
        private readonly string _upperComputerNo;
        private readonly string _operationCode;
        private readonly string _outboundPath;

        public ContractDieCuttingModuleParamProvider(
            string moduleId,
            string upperComputerNo,
            string operationCode,
            string outboundPath = "/dev/dev/electrode/exit/push")
        {
            _moduleId = moduleId;
            _upperComputerNo = upperComputerNo;
            _operationCode = operationCode;
            _outboundPath = outboundPath;
        }

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
                        [DieCuttingParams.Mes.启用] = "true",
                        [DieCuttingParams.Mes.UpperComputerNo] = _upperComputerNo,
                        [DieCuttingParams.Mes.OperationCode] = _operationCode,
                        [DieCuttingParams.Mes.OrderPath] = "/dev/dev/get/order",
                        [DieCuttingParams.Mes.BatchNumberPath] = "/dev/dev/get/batchNumber",
                        [DieCuttingParams.Mes.OutboundPath] = _outboundPath,
                        [DieCuttingParams.Mes.上传频率毫秒] = "10000",
                        [DieCuttingParams.Mes.数据新鲜度超时毫秒] = "5000"
                    },
                    new Dictionary<DieCuttingParams.Mes, ParamValueKind>
                    {
                        [DieCuttingParams.Mes.启用] = ParamValueKind.Bool,
                        [DieCuttingParams.Mes.UpperComputerNo] = ParamValueKind.String,
                        [DieCuttingParams.Mes.OperationCode] = ParamValueKind.String,
                        [DieCuttingParams.Mes.OrderPath] = ParamValueKind.String,
                        [DieCuttingParams.Mes.BatchNumberPath] = ParamValueKind.String,
                        [DieCuttingParams.Mes.OutboundPath] = ParamValueKind.String,
                        [DieCuttingParams.Mes.上传频率毫秒] = ParamValueKind.Int,
                        [DieCuttingParams.Mes.数据新鲜度超时毫秒] = ParamValueKind.Int
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
            => Task.FromResult($"http://10.98.101.247:8080{relativeOrAbsoluteUrl}");

        public Task<string?> TryBuildFirstConfiguredUrlAsync(
            IReadOnlyCollection<string> processTypes,
            string relativeOrAbsoluteUrl,
            CancellationToken cancellationToken = default)
            => Task.FromResult<string?>($"http://10.98.101.247:8080{relativeOrAbsoluteUrl}");

        public IReadOnlyDictionary<string, string> GetDefaultHeaders() => new Dictionary<string, string>();
    }

    private sealed class ContractModuleParamRoleProvider(string stationNo) : IModuleParamRoleProvider
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
