using IIoT.Edge.Application.Abstractions.Config;
using IIoT.Edge.Application.Abstractions.Device;
using IIoT.Edge.Application.Abstractions.Modules;
using IIoT.Edge.Application.Modules.Mes;
using System.Text.Json;
using IIoT.Edge.Module.Homogenization;
using IIoT.Edge.Module.Homogenization.Config;
using IIoT.Edge.Module.Homogenization.Config.Parameters;
using IIoT.Edge.Module.Homogenization.Integration;
using IIoT.Edge.Module.Homogenization.Integration.Cloud;
using IIoT.Edge.Module.Homogenization.Payload;
using IIoT.Edge.Module.Homogenization.Runtime;
using IIoT.Edge.Module.Homogenization.Samples;
using IIoT.Edge.SharedKernel.Context;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using HomogenizationCloudUploadChannel = IIoT.Edge.Application.Modules.Cloud.ICloudUploadChannel<
    IIoT.Edge.Module.Homogenization.Payload.HomogenizationCellData,
    object>;
using HomogenizationMesScenarioChannel = IIoT.Edge.Application.Modules.Mes.IMesScenarioChannel<
    IIoT.Edge.Module.Homogenization.Payload.HomogenizationCellData,
    string,
    IIoT.Edge.Module.Homogenization.Payload.HomogenizationRealtimeSnapshot,
    IIoT.Edge.Module.Homogenization.Payload.HomogenizationRecipeSnapshot,
    IIoT.Edge.Module.Homogenization.Payload.HomogenizationEquipmentStatusSnapshot>;

namespace IIoT.Edge.Module.ContractTests;

public sealed class HomogenizationModuleContractTests : ModuleContractTestBase<DependencyInjection>
{
    protected override bool RequiresHardwareProfile => true;
    protected override bool RequiresMesUploader => true;
    protected override int ExpectedRuntimeTaskCount => 6;
    protected override int MinimumRouteCount => 7;

    protected override ProductionContext CreateRuntimeContext()
        => new HomogenizationContext { DeviceName = "PLC-A" };

    protected override void ConfigureRuntimeServices(IServiceCollection services)
    {
        AddDefaultRuntimeServices(services);
        services.AddSingleton<IDeviceService, ContractDeviceService>();
        services.AddSingleton<IMesUploadDiagnosticsStore, ContractMesUploadDiagnosticsStore>();
        services.AddSingleton<HomogenizationMesScenarioChannel, ContractHomogenizationMesChannel>();
        services.AddSingleton<HomogenizationCellDataValidator>();
        services.AddSingleton<IModuleParamProvider<HomogenizationParams.Mes, HomogenizationParams.Cloud, HomogenizationParams.Business>, ContractHomogenizationModuleParamProvider>();
        services.AddSingleton(Options.Create(new HomogenizationModuleOptions()));
        services.AddSingleton(Options.Create(new HomogenizationCodeOptions()));
    }

    [Fact]
    public void RegisterServices_ShouldRegisterDevelopmentSampleContributor()
    {
        var result = new ModuleContractFixture().RegisterModule(new DependencyInjection());

        Assert.Contains(
            result.Services,
            descriptor => descriptor.ServiceType == typeof(IDevelopmentSampleContributor)
                          && descriptor.ImplementationType == typeof(HomogenizationDevelopmentSampleContributor));
    }

    [Fact]
    public void PluginManifest_ShouldMatchHomogenizationModuleEntry()
    {
        var manifestPath = Path.Combine(
            ContractTestPathHelper.GetModuleSourceDirectory("Homogenization"),
            "plugin.json");

        using var document = JsonDocument.Parse(File.ReadAllText(manifestPath));
        var root = document.RootElement;

        Assert.Equal("Homogenization", root.GetProperty("moduleId").GetString());
        Assert.Equal("Homogenization", root.GetProperty("supportedProcessType").GetString());
        Assert.Equal("IIoT.Edge.Module.Homogenization.DependencyInjection", root.GetProperty("entryType").GetString());
    }

    [Fact]
    public void RegisterServices_ShouldRegisterMesChannelAsProcessUploader()
    {
        var result = new ModuleContractFixture().RegisterModule(new DependencyInjection());

        Assert.Contains(
            result.Services,
            descriptor => descriptor.ServiceType == typeof(HomogenizationMesChannel)
                          && descriptor.ImplementationType == typeof(HomogenizationMesChannel));
        Assert.Contains(
            result.Services,
            descriptor => descriptor.ServiceType == typeof(HomogenizationMesScenarioChannel)
                          && descriptor.ImplementationFactory is not null);
        Assert.Contains(
            result.Services,
            descriptor => descriptor.ServiceType == typeof(IProcessMesUploader)
                          && descriptor.ImplementationFactory is not null);
    }

    [Fact]
    public void RegisterServices_ShouldRegisterCloudChannelAbstractionAndProcessUploader()
    {
        var result = new ModuleContractFixture().RegisterModule(new DependencyInjection());

        Assert.Contains(
            result.Services,
            descriptor => descriptor.ServiceType == typeof(HomogenizationCloudUploader)
                          && descriptor.ImplementationType == typeof(HomogenizationCloudUploader));
        Assert.Contains(
            result.Services,
            descriptor => descriptor.ServiceType == typeof(HomogenizationCloudUploadChannel)
                          && descriptor.ImplementationFactory is not null);
        Assert.Contains(
            result.Services,
            descriptor => descriptor.ServiceType == typeof(IProcessCloudUploader)
                          && descriptor.ImplementationFactory is not null);
    }

    [Fact]
    public void DependencyInjection_Configure_BindsAllOptions()
    {
        var configuration = new ConfigurationBuilder()
            .AddJsonFile(
                Path.Combine(
                    ContractTestPathHelper.GetModuleCoreSourceDirectory("Homogenization"),
                    "Config",
                    "homogenization.module.json"),
                optional: false,
                reloadOnChange: false)
            .Build();

        var result = new ModuleContractFixture().RegisterModule(new DependencyInjection(), configuration);
        using var provider = result.Services.BuildServiceProvider();

        Assert.Equal(500, provider.GetRequiredService<IOptions<HomogenizationModuleOptions>>().Value.Presentation.MaxOutboundRecords);
        Assert.Equal("hdc2023", provider.GetRequiredService<IOptions<HomogenizationMesOptions>>().Value.SignToken);
        Assert.Equal(11, provider.GetRequiredService<IOptions<HomogenizationCodeOptions>>().Value.Plc.SignalTrigger);
        Assert.Equal("ERROR", provider.GetRequiredService<IOptions<HomogenizationCodeOptions>>().Value.Cloud.EquipmentStatusLevels["-1"]);
    }

    [Fact]
    public void HomogenizationLanguageDictionaries_ShouldContainSameNonEmptyKeys()
    {
        var resourceDirectory = Path.Combine(
            ContractTestPathHelper.GetModuleSourceDirectory("Homogenization"),
            "Resources",
            "Languages");
        var zhKeys = ReadLanguageDictionary(resourceDirectory, "zh-CN.xaml");
        var enKeys = ReadLanguageDictionary(resourceDirectory, "en-US.xaml");

        Assert.NotEmpty(zhKeys);
        Assert.Equal(zhKeys.Keys.Order(), enKeys.Keys.Order());
        Assert.All(zhKeys, item => Assert.False(string.IsNullOrWhiteSpace(item.Value), item.Key));
        Assert.All(enKeys, item => Assert.False(string.IsNullOrWhiteSpace(item.Value), item.Key));
    }

    private static IReadOnlyDictionary<string, string> ReadLanguageDictionary(string directory, string fileName)
    {
        var path = Path.Combine(directory, fileName);
        var text = File.ReadAllText(path);
        return System.Text.RegularExpressions.Regex
            .Matches(text, "<sys:String x:Key=\"(?<key>[^\"]+)\">(?<value>.*?)</sys:String>")
            .ToDictionary(
                match => match.Groups["key"].Value,
                match => match.Groups["value"].Value);
    }

    private sealed class ContractDeviceService : IDeviceService
    {
        public DeviceSession? CurrentDevice { get; } = new()
        {
            DeviceId = Guid.NewGuid(),
            DeviceName = "PLC-H",
            ClientCode = "PLC-H"
        };

        public NetworkState CurrentState => NetworkState.Online;
        public EdgeUploadGateSnapshot CurrentUploadGate => new() { State = EdgeUploadGateState.Ready };
        public bool HasDeviceId => true;
        public bool CanUploadToCloud => true;
        public Task StartAsync(CancellationToken ct) => Task.CompletedTask;
        public Task StopAsync() => Task.CompletedTask;
        public Task RefreshBootstrapAsync(CancellationToken ct = default) => Task.CompletedTask;
        public void MarkUploadGateBlocked(EdgeUploadBlockReason reason, DateTimeOffset occurredAtUtc) { }
        public event Action<NetworkState>? NetworkStateChanged
        {
            add { }
            remove { }
        }

        public event Action<DeviceSession?>? DeviceIdentified
        {
            add { }
            remove { }
        }

        public event Action<EdgeUploadGateSnapshot>? UploadGateChanged
        {
            add { }
            remove { }
        }
    }

    private sealed class ContractMesUploadDiagnosticsStore : IMesUploadDiagnosticsStore
    {
        public IReadOnlyList<MesChannelDiagnostics> GetAll() => [];
        public MesChannelDiagnostics? Get(string processType) => null;
        public void RecordSuccess(string processType) { }
        public void RecordFailure(string processType, string failureReason) { }
    }

    private sealed class ContractHomogenizationModuleParamProvider
        : IModuleParamProvider<HomogenizationParams.Mes, HomogenizationParams.Cloud, HomogenizationParams.Business>
    {
        public Task<ModuleParamSnapshot<HomogenizationParams.Mes, HomogenizationParams.Cloud, HomogenizationParams.Business>> GetAsync(
            CancellationToken cancellationToken = default)
            => Task.FromResult(new ModuleParamSnapshot<HomogenizationParams.Mes, HomogenizationParams.Cloud, HomogenizationParams.Business>(
                "Homogenization",
                new ModuleParamGroup<HomogenizationParams.Mes>(
                    "Homogenization",
                    ModuleParamCategory.Mes,
                    new Dictionary<HomogenizationParams.Mes, string>(),
                    new Dictionary<HomogenizationParams.Mes, string?>(),
                    new Dictionary<HomogenizationParams.Mes, ParamValueKind>(),
                    warn: null),
                new ModuleParamGroup<HomogenizationParams.Cloud>(
                    "Homogenization",
                    ModuleParamCategory.Cloud,
                    new Dictionary<HomogenizationParams.Cloud, string>(),
                    new Dictionary<HomogenizationParams.Cloud, string?>(),
                    new Dictionary<HomogenizationParams.Cloud, ParamValueKind>(),
                    warn: null),
                new ModuleParamGroup<HomogenizationParams.Business>(
                    "Homogenization",
                    ModuleParamCategory.Business,
                    new Dictionary<HomogenizationParams.Business, string>(),
                    new Dictionary<HomogenizationParams.Business, string?>
                    {
                        [HomogenizationParams.Business.启用托盘码重码验证] = "false"
                    },
                    new Dictionary<HomogenizationParams.Business, ParamValueKind>
                    {
                        [HomogenizationParams.Business.启用托盘码重码验证] = ParamValueKind.Bool
                    },
                    warn: null)));
    }

    private sealed class ContractHomogenizationMesChannel : HomogenizationMesScenarioChannel
    {
        public string ProcessType => "Homogenization";
        public MesUploadMode UploadMode => MesUploadMode.Single;

        public Task<MesCallResult> UploadAsync(
            ProcessMesUploadContext context,
            IReadOnlyList<IIoT.Edge.SharedKernel.DataPipeline.CellCompletedRecord> records,
            CancellationToken cancellationToken = default)
            => Task.FromResult(MesCallResult.Success());

        public Task<MesCallResult> UploadInboundAsync(
            DeviceSession? device,
            string trayCode,
            CancellationToken cancellationToken = default)
            => Task.FromResult(MesCallResult.Success());

        public Task<MesCallResult> UploadOutboundAsync(
            DeviceSession? device,
            HomogenizationCellData cellData,
            CancellationToken cancellationToken = default)
            => Task.FromResult(MesCallResult.Success());

        public Task<MesCallResult> UploadRealtimeAsync(
            DeviceSession? device,
            HomogenizationRealtimeSnapshot snapshot,
            CancellationToken cancellationToken = default)
            => Task.FromResult(MesCallResult.Success());

        public Task<MesCallResult> UploadRecipeAsync(
            DeviceSession? device,
            HomogenizationRecipeSnapshot snapshot,
            CancellationToken cancellationToken = default)
            => Task.FromResult(MesCallResult.Success());

        public Task<MesCallResult> UploadEquipmentStatusAsync(
            DeviceSession? device,
            HomogenizationEquipmentStatusSnapshot snapshot,
            CancellationToken cancellationToken = default)
            => Task.FromResult(MesCallResult.Success());
    }
}
