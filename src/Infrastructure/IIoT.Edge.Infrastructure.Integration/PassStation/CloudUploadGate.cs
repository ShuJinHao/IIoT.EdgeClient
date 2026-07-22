using IIoT.Edge.Module.Contracts.Config;
using IIoT.Edge.Module.Contracts.Device;
using IIoT.Edge.Application.Common.Device;

using IIoT.Edge.Module.Contracts.Cloud;
using IIoT.Edge.Module.Contracts.Shared;
namespace IIoT.Edge.Infrastructure.Integration.PassStation;

public sealed class CloudUploadGate : ICloudUploadGate
{
    private readonly ILocalSystemRuntimeConfigService _runtimeConfig;
    private readonly IDeviceService _deviceService;

    public CloudUploadGate(
        ILocalSystemRuntimeConfigService runtimeConfig,
        IDeviceService deviceService)
    {
        _runtimeConfig = runtimeConfig;
        _deviceService = deviceService;
    }

    public ExternalSystemKind System => ExternalSystemKind.Cloud;

    public UploadGateSnapshot GetSnapshot()
    {
        if (!_runtimeConfig.Current.SystemCloudEnabled)
        {
            return UploadGateSnapshot.Blocked(System, "cloud_upload_disabled", "云端上传已关闭。");
        }

        if (_deviceService.CanUploadToCloud)
        {
            return UploadGateSnapshot.Ready(System);
        }

        var gate = _deviceService.CurrentUploadGate;
        return UploadGateSnapshot.Blocked(
            System,
            gate.Reason.ToReasonCode(),
            $"云端上传门控已阻塞：{gate.Reason}。");
    }
}
