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

namespace IIoT.Edge.Module.ContractTests;

public sealed class DieCuttingModuleContractTests : ModuleContractTestBase<DependencyInjection>
{
    protected override bool RequiresHardwareProfile => true;
    protected override bool RequiresMesUploader => true;
    protected override int ExpectedRuntimeTaskCount => 1;
    protected override int MinimumRouteCount => 6;

    protected override ProductionContext CreateRuntimeContext()
        => new DieCuttingContext { DeviceName = "P1-AP01" };

    protected override void ConfigureRuntimeServices(IServiceCollection services)
    {
        AddDefaultRuntimeServices(services);
        services.AddSingleton<IMesUploadDiagnosticsStore, ContractMesUploadDiagnosticsStore>();
        services.AddSingleton<IDieCuttingMesScenarioChannel, ContractDieCuttingMesChannel>();
        services.AddSingleton<IModuleParamProvider<DieCuttingParams.Mes, DieCuttingParams.Cloud, DieCuttingParams.Business>, ContractDieCuttingModuleParamProvider>();
        services.AddSingleton(Options.Create(new DieCuttingModuleOptions()));
    }

    [Fact]
    public void RegisterServices_ShouldRegisterDevelopmentSampleContributor()
    {
        var result = new ModuleContractFixture().RegisterModule(new DependencyInjection());

        Assert.Contains(
            result.Services,
            descriptor => descriptor.ServiceType == typeof(IDevelopmentSampleContributor)
                          && descriptor.ImplementationType == typeof(DieCuttingDevelopmentSampleContributor));
    }

    [Fact]
    public void PluginManifest_ShouldMatchDieCuttingModuleEntry()
    {
        var manifestPath = Path.Combine(
            ContractTestPathHelper.GetModuleSourceDirectory("DieCutting"),
            "plugin.json");

        using var document = JsonDocument.Parse(File.ReadAllText(manifestPath));
        var root = document.RootElement;

        Assert.Equal("DieCutting", root.GetProperty("moduleId").GetString());
        Assert.Equal("DieCutting", root.GetProperty("supportedProcessType").GetString());
        Assert.Equal("IIoT.Edge.Module.DieCutting.DependencyInjection", root.GetProperty("entryType").GetString());
    }

    [Fact]
    public void RegisterServices_ShouldRegisterMesChannelAsProcessUploader()
    {
        var result = new ModuleContractFixture().RegisterModule(new DependencyInjection());

        Assert.Contains(
            result.Services,
            descriptor => descriptor.ServiceType == typeof(DieCuttingMesChannel)
                          && descriptor.ImplementationType == typeof(DieCuttingMesChannel));
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
    public void DependencyInjection_Configure_BindsLocalOptionsAndParameterRoles()
    {
        var configuration = new ConfigurationBuilder()
            .AddJsonFile(
                Path.Combine(
                    ContractTestPathHelper.GetModuleSourceDirectory("DieCutting"),
                    "Config",
                    "diecutting.module.json"),
                optional: false,
                reloadOnChange: false)
            .Build();

        var result = new ModuleContractFixture().RegisterModule(new DependencyInjection(), configuration);
        using var provider = result.Services.BuildServiceProvider();

        Assert.Equal(1000, provider.GetRequiredService<IOptions<DieCuttingModuleOptions>>().Value.Runtime.DataReadLoopIntervalMs);
        Assert.Contains(
            result.ModuleParamRegistry.GetDescriptors(DependencyInjection.ModuleKey, ModuleParamCategory.Business),
            descriptor => descriptor.Role == ModuleParamRole.DataReadLoopIntervalMs
                          && descriptor.Name == nameof(DieCuttingParams.Business.采集频率毫秒));
    }

    [Fact]
    public void MesIdentity_WhenCodeIsMissing_ShouldReserveEmptyDeviceCodeByDefault()
    {
        var identity = new DieCuttingMesIdentityOptions().Resolve("P1-AP01");

        Assert.Equal(string.Empty, identity.DeviceCode);
        Assert.Equal("P1-AP01", identity.DeviceName);
        Assert.Equal("P1-AP01", identity.UpperComputerNo);
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
        public Task<ModuleParamSnapshot<DieCuttingParams.Mes, DieCuttingParams.Cloud, DieCuttingParams.Business>> GetAsync(
            CancellationToken cancellationToken = default)
            => Task.FromResult(new ModuleParamSnapshot<DieCuttingParams.Mes, DieCuttingParams.Cloud, DieCuttingParams.Business>(
                "DieCutting",
                new ModuleParamGroup<DieCuttingParams.Mes>(
                    "DieCutting",
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
                    "DieCutting",
                    ModuleParamCategory.Cloud,
                    new Dictionary<DieCuttingParams.Cloud, string>(),
                    new Dictionary<DieCuttingParams.Cloud, string?>(),
                    new Dictionary<DieCuttingParams.Cloud, ParamValueKind>(),
                    warn: null),
                new ModuleParamGroup<DieCuttingParams.Business>(
                    "DieCutting",
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
        public string ProcessType => "DieCutting";
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
