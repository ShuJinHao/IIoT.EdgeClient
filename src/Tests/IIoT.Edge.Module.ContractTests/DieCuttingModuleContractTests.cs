using IIoT.Edge.Application.Abstractions.Config;
using IIoT.Edge.Application.Abstractions.Device;
using IIoT.Edge.Application.Abstractions.Mes;
using IIoT.Edge.Application.Abstractions.Modules;
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
        services.AddSingleton<IDieCuttingMesScenarioChannel>(new ContractDieCuttingMesChannel(ExpectedModuleId));
        services.AddSingleton<IModuleParamProvider<DieCuttingParams.Mes, DieCuttingParams.Cloud, DieCuttingParams.Business>>(
            new ContractDieCuttingModuleParamProvider(ExpectedModuleId));
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

    private static void AssertSeededMesIdentity(JsonElement devices, string deviceCode, string upperComputerNo)
    {
        var identity = devices.GetProperty(deviceCode);
        Assert.Equal(deviceCode, identity.GetProperty("DeviceCode").GetString());
        Assert.Equal(deviceCode, identity.GetProperty("DeviceName").GetString());
        Assert.Equal(upperComputerNo, identity.GetProperty("UpperComputerNo").GetString());
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

        public ContractDieCuttingModuleParamProvider(string moduleId)
        {
            _moduleId = moduleId;
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
                        [DieCuttingParams.Mes.上传频率毫秒] = "10000",
                        [DieCuttingParams.Mes.数据新鲜度超时毫秒] = "5000"
                    },
                    new Dictionary<DieCuttingParams.Mes, ParamValueKind>
                    {
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

        public Task<MesCallResult> UploadRealtimeAsync(
            DeviceSession? device,
            DieCuttingRealtimeSnapshot snapshot,
            CancellationToken cancellationToken = default)
            => Task.FromResult(MesCallResult.Success());
    }
}
