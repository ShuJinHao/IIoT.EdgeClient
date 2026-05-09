using IIoT.Edge.Application.Abstractions.Config;
using IIoT.Edge.Application.Abstractions.Device;
using IIoT.Edge.Application.Abstractions.Integration;
using IIoT.Edge.Infrastructure.Integration.Mes;
using IIoT.Edge.Infrastructure.Integration.PassStation;

namespace IIoT.Edge.NonUiRegressionTests;

public sealed class UploadGateBehaviorTests
{
    [Fact]
    public void CloudUploadGate_WhenDeviceGateIsReady_ShouldAllowUpload()
    {
        var deviceService = CreateOnlineDeviceService();
        var gate = new CloudUploadGate(new FakeLocalSystemRuntimeConfigService(), deviceService);

        var snapshot = gate.GetSnapshot();

        Assert.True(snapshot.CanUpload);
        Assert.Equal(ExternalSystemKind.Cloud, snapshot.System);
        Assert.Equal("ready", snapshot.ReasonCode);
    }

    [Fact]
    public void CloudUploadGate_WhenDeviceGateIsBlocked_ShouldExposeCloudReason()
    {
        var deviceService = CreateOnlineDeviceService();
        deviceService.MarkUploadGateBlocked(EdgeUploadBlockReason.UploadTokenRejected, DateTimeOffset.UtcNow);
        var gate = new CloudUploadGate(new FakeLocalSystemRuntimeConfigService(), deviceService);

        var snapshot = gate.GetSnapshot();

        Assert.False(snapshot.CanUpload);
        Assert.Equal("upload_token_rejected", snapshot.ReasonCode);
    }

    [Fact]
    public void CloudUploadGate_WhenCloudUploadDisabled_ShouldBlockWithDisabledReason()
    {
        var runtimeConfig = new FakeLocalSystemRuntimeConfigService
        {
            Current = SystemRuntimeConfigSnapshot.Default with { CloudUploadEnabled = false }
        };
        var gate = new CloudUploadGate(runtimeConfig, CreateOnlineDeviceService());

        var snapshot = gate.GetSnapshot();

        Assert.False(snapshot.CanUpload);
        Assert.Equal("cloud_upload_disabled", snapshot.ReasonCode);
    }

    [Fact]
    public void MesUploadGate_WhenMesUploadDisabled_ShouldBlockWithDisabledReason()
    {
        var runtimeConfig = new FakeLocalSystemRuntimeConfigService
        {
            Current = SystemRuntimeConfigSnapshot.Default with { MesUploadEnabled = false }
        };
        var heartbeatStore = new FakeExternalHeartbeatStateStore();
        heartbeatStore.MarkReady(ExternalSystemKind.Mes);

        var snapshot = new MesUploadGate(runtimeConfig, heartbeatStore).GetSnapshot();

        Assert.False(snapshot.CanUpload);
        Assert.Equal("mes_upload_disabled", snapshot.ReasonCode);
    }

    [Fact]
    public void MesUploadGate_WhenHeartbeatIsNotReady_ShouldBlockWithHeartbeatReason()
    {
        var heartbeatStore = new FakeExternalHeartbeatStateStore();
        heartbeatStore.MarkNotReady(ExternalSystemKind.Mes, "mes_heartbeat_timeout");

        var snapshot = new MesUploadGate(new FakeLocalSystemRuntimeConfigService(), heartbeatStore).GetSnapshot();

        Assert.False(snapshot.CanUpload);
        Assert.Equal("mes_heartbeat_timeout", snapshot.ReasonCode);
    }

    [Fact]
    public void MesUploadGate_WhenHeartbeatReady_ShouldAllowUpload()
    {
        var heartbeatStore = new FakeExternalHeartbeatStateStore();
        heartbeatStore.MarkReady(ExternalSystemKind.Mes);

        var snapshot = new MesUploadGate(new FakeLocalSystemRuntimeConfigService(), heartbeatStore).GetSnapshot();

        Assert.True(snapshot.CanUpload);
        Assert.Equal(ExternalSystemKind.Mes, snapshot.System);
        Assert.Equal("ready", snapshot.ReasonCode);
    }

    private static FakeDeviceService CreateOnlineDeviceService()
    {
        var deviceService = new FakeDeviceService();
        deviceService.SetOnline(new DeviceSession
        {
            DeviceId = Guid.NewGuid(),
            DeviceName = "PLC-GATE",
            ClientCode = "CLIENT-GATE",
            ProcessId = Guid.NewGuid()
        });
        return deviceService;
    }
}
