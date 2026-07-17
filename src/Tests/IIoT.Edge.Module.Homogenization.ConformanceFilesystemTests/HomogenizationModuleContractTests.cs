using IIoT.Edge.Application.Abstractions.Config;
using IIoT.Edge.Application.Abstractions.Cloud;
using IIoT.Edge.Application.Abstractions.Device;
using IIoT.Edge.Application.Abstractions.Mes;
using IIoT.Edge.Application.Abstractions.Modules;
using IIoT.Edge.Application.Abstractions.Shared;
using IIoT.Edge.Application.Features.Production.Planning;
using IIoT.Edge.Application.Modules.Mes;
using System.Text.Json;
using IIoT.Edge.Module.Homogenization;
using IIoT.Edge.Module.Homogenization.Config;
using IIoT.Edge.Module.Homogenization.Config.Io;
using IIoT.Edge.Module.Homogenization.Config.Parameters;
using IIoT.Edge.Module.Homogenization.Mes;
using IIoT.Edge.Module.Homogenization.Payload;
using IIoT.Edge.Module.Homogenization.Production;
using IIoT.Edge.Module.Homogenization.Samples;
using IIoT.Edge.SharedKernel.Context;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace IIoT.Edge.Module.Homogenization.ConformanceFilesystemTests;

public sealed class HomogenizationModuleContractTests : ModuleContractTestBase<DependencyInjection>
{
    protected override bool RequiresCloudUploader => true;
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
        services.AddSingleton<ICloudUploadDiagnosticsStore, ContractCloudUploadDiagnosticsStore>();
        services.AddSingleton<IHomogenizationMesScenarioChannel, ContractHomogenizationMesChannel>();
        services.AddSingleton<HomogenizationCellDataValidator>();
        services.AddSingleton<IModuleParamProvider<HomogenizationParams.Mes, HomogenizationParams.Cloud, HomogenizationParams.Business>, ContractHomogenizationModuleParamProvider>();
        services.AddSingleton<IProductionPlanSelectionService, ContractProductionPlanSelectionService>();
        services.AddSingleton<IHomogenizationProductionGate, HomogenizationProductionGate>();
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
            GetModuleSourceDirectory(),
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
            descriptor => descriptor.ServiceType == typeof(HomogenizationMesPayloadBuilder)
                          && descriptor.ImplementationType == typeof(HomogenizationMesPayloadBuilder));
        Assert.Contains(
            result.Services,
            descriptor => descriptor.ServiceType == typeof(HomogenizationMesChannel)
                          && descriptor.ImplementationType == typeof(HomogenizationMesChannel));
        Assert.Contains(
            result.Services,
            descriptor => descriptor.ServiceType == typeof(IHomogenizationMesScenarioChannel)
                          && descriptor.ImplementationFactory is not null);
        Assert.Contains(
            result.Services,
            descriptor => descriptor.ServiceType == typeof(IProcessMesUploader)
                          && descriptor.ImplementationFactory is not null);
    }

    [Fact]
    public void DependencyInjection_Configure_BindsAllOptions()
    {
        var configuration = new ConfigurationBuilder()
            .AddJsonFile(
                Path.Combine(
                    GetModuleSourceDirectory(),
                    "Config",
                    "homogenization.module.json"),
                optional: false,
                reloadOnChange: false)
            .Build();

        var result = new ModuleContractFixture().RegisterModule(new DependencyInjection(), configuration);
        using var provider = result.Services.BuildServiceProvider();

        Assert.Equal(500, provider.GetRequiredService<IOptions<HomogenizationModuleOptions>>().Value.Presentation.MaxOutboundRecords);
        Assert.Equal(11, provider.GetRequiredService<IOptions<HomogenizationCodeOptions>>().Value.Plc.SignalTrigger);
        Assert.Equal("报警", provider.GetRequiredService<IOptions<HomogenizationCodeOptions>>().Value.Mes.EquipmentStatusTexts["-1"]);
        Assert.Contains(
            result.ModuleParamRegistry.GetDescriptors(DependencyInjection.ModuleKey, ModuleParamCategory.Mes),
            descriptor => descriptor.Role == ModuleParamRole.MesSignToken
                          && descriptor.Name == nameof(HomogenizationParams.Mes.签名令牌));
    }

    [Fact]
    public void RuntimeFactory_TaskCandidates_ShouldDefaultEnableRealtimeDataAndEquipmentStatus()
    {
        var factory = new HomogenizationStationRuntimeFactory();
        var candidates = factory.GetTaskCandidates();

        var defaultEnabledKeys = candidates
            .Where(static candidate => candidate.DefaultEnabled)
            .Select(static candidate => candidate.Key)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        Assert.Equal(
            ["Homogenization.EquipmentStatus", "Homogenization.Realtime"],
            defaultEnabledKeys);

        var realtimeCandidate = Assert.Single(candidates, static candidate => candidate.Key == "Homogenization.Realtime");
        Assert.Contains(
            realtimeCandidate.RequiredSignals,
            static signal => signal.SignalKey == "Homogenization.RealtimeStirringSpeed"
                             && signal.Direction == "Read");
        Assert.Contains(
            realtimeCandidate.RequiredSignals,
            static signal => signal.SignalKey == "Homogenization.RealtimeTemperature"
                             && signal.Direction == "Read");

        var equipmentStatusCandidate = Assert.Single(candidates, static candidate => candidate.Key == "Homogenization.EquipmentStatus");
        Assert.Contains(
            equipmentStatusCandidate.RequiredSignals,
            static signal => signal.SignalKey == "Homogenization.EquipmentStatusValue"
                             && signal.Direction == "Read");
        Assert.Contains(
            equipmentStatusCandidate.RequiredSignals,
            static signal => signal.SignalKey == "Homogenization.Interaction.EquipmentStatus"
                             && signal.Direction == "Read");
        Assert.Contains(
            equipmentStatusCandidate.RequiredSignals,
            static signal => signal.SignalKey == "Homogenization.Interaction.EquipmentStatus"
                             && signal.Direction == "Write");
    }

    [Fact]
    public void HomogenizationLanguageDictionaries_ShouldContainSameNonEmptyKeys()
    {
        var resourceDirectory = Path.Combine(
            GetModuleSourceDirectory(),
            "Resources",
            "Languages");
        var zhKeys = ReadLanguageDictionary(resourceDirectory, "zh-CN.axaml");
        var enKeys = ReadLanguageDictionary(resourceDirectory, "en-US.axaml");

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

    private static string GetModuleSourceDirectory()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "IIoT.EdgeClient.slnx")))
            {
                return Path.Combine(directory.FullName, "src", "Modules", "IIoT.Edge.Module.Homogenization");
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Could not locate IIoT.EdgeClient repository root.");
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

    private sealed class ContractHomogenizationMesChannel : IHomogenizationMesScenarioChannel
    {
        public string ProcessType => "Homogenization";
        public ProcessUploadMode UploadMode => ProcessUploadMode.Single;

        public Task<MesCallResult> UploadAsync(
            ProcessUploadContext context,
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

        public Task<MesCallResult<HomogenizationMainPlan>> GetMainPlanAsync(
            HomogenizationMainPlanRequest request,
            CancellationToken cancellationToken = default)
            => Task.FromResult(MesCallResult<HomogenizationMainPlan>.Success(new HomogenizationMainPlan([])));

        public Task<MesCallResult<HomogenizationTraceBatchResult>> GenerateTraceBatchNumberAsync(
            HomogenizationTraceBatchRequest request,
            CancellationToken cancellationToken = default)
            => Task.FromResult(MesCallResult<HomogenizationTraceBatchResult>.Success(null));
    }

    private sealed class ContractProductionPlanSelectionService : IProductionPlanSelectionService
    {
        public string ProcessType => DependencyInjection.ModuleKey;

        public ProductionPlanOption? CurrentPlan => null;

        public Task<ProductionPlanSelectionState> GetStateAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(new ProductionPlanSelectionState(false, false, null, string.Empty));

        public Task<IReadOnlyList<ProductionPlanOption>> LoadPlansAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<ProductionPlanOption>>([]);

        public Task SelectPlanAsync(ProductionPlanOption option, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }
}
