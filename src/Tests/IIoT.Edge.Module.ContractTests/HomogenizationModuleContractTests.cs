using IIoT.Edge.Application.Abstractions.Device;
using IIoT.Edge.Application.Abstractions.Modules;
using IIoT.Edge.Module.Homogenization;
using IIoT.Edge.Module.Homogenization.Config;
using IIoT.Edge.Module.Homogenization.Integration;
using IIoT.Edge.Module.Homogenization.Payload;
using IIoT.Edge.Module.Homogenization.Runtime;
using IIoT.Edge.Module.Abstractions;
using IIoT.Edge.SharedKernel.Context;

namespace IIoT.Edge.Module.ContractTests;

public sealed class HomogenizationModuleContractTests : ModuleContractTestBase<HomogenizationModule>
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
        services.AddSingleton<IHomogenizationMesApiService, ContractHomogenizationMesApiService>();
        services.AddSingleton<HomogenizationCellDataValidator>();
    }

    [Fact]
    public void RegisterServices_ShouldRegisterDevelopmentSampleContributor()
    {
        var result = new ModuleContractFixture().RegisterModule(new HomogenizationModule());

        Assert.Contains(
            result.Services,
            descriptor => descriptor.ServiceType == typeof(IDevelopmentSampleContributor)
                          && descriptor.ImplementationType == typeof(HomogenizationDevelopmentSampleContributor));
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
        public event Action<NetworkState>? NetworkStateChanged;
        public event Action<DeviceSession?>? DeviceIdentified;
        public event Action<EdgeUploadGateSnapshot>? UploadGateChanged;
    }

    private sealed class ContractMesUploadDiagnosticsStore : IMesUploadDiagnosticsStore
    {
        public IReadOnlyList<MesChannelDiagnostics> GetAll() => [];
        public MesChannelDiagnostics? Get(string processType) => null;
        public void RecordSuccess(string processType) { }
        public void RecordFailure(string processType, string failureReason) { }
    }

    private sealed class ContractHomogenizationMesApiService : IHomogenizationMesApiService
    {
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
