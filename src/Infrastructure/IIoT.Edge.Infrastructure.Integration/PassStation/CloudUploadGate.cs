using IIoT.Edge.Application.Abstractions.Device;
using IIoT.Edge.Application.Abstractions.Integration;
using IIoT.Edge.Application.Common.Device;

namespace IIoT.Edge.Infrastructure.Integration.PassStation;

public sealed class CloudUploadGate : ICloudUploadGate
{
    private readonly IDeviceService _deviceService;

    public CloudUploadGate(IDeviceService deviceService)
    {
        _deviceService = deviceService;
    }

    public ExternalSystemKind System => ExternalSystemKind.Cloud;

    public UploadGateSnapshot GetSnapshot()
    {
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
