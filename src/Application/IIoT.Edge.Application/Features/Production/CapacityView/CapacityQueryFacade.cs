using IIoT.Edge.Application.Abstractions.Cloud;
using IIoT.Edge.Application.Abstractions.Device;
using MediatR;

namespace IIoT.Edge.Application.Features.Production.CapacityView;

/// <summary>
/// 产能查询 facade 契约。
/// 提供联网状态和产能查询能力。
/// </summary>
public interface ICapacityQueryFacade
{
    event Action<EdgeUploadGateSnapshot>? UploadGateChanged;

    bool IsOnline { get; }

    Task<CapacityViewResult> LoadTodayAsync(string plcName, CancellationToken cancellationToken = default);

    Task<CapacityViewResult> QueryHistoryAsync(string queryMode, DateTime queryDate, string plcName, CancellationToken cancellationToken = default);
}

/// <summary>
/// 产能查询 facade。
/// 负责衔接设备上下文、联网状态与产能查询用例。
/// </summary>
public sealed class CapacityQueryFacade(
    ISender sender,
    IDeviceService deviceService) : ICapacityQueryFacade
{
    public event Action<EdgeUploadGateSnapshot>? UploadGateChanged
    {
        add => deviceService.UploadGateChanged += value;
        remove => deviceService.UploadGateChanged -= value;
    }

    public bool IsOnline => deviceService.CanUploadToCloud;

    public async Task<CapacityViewResult> LoadTodayAsync(string plcName, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!deviceService.CanUploadToCloud)
        {
            return CapacityViewResult.Unavailable(CapacityQueryReasonCodes.CloudGateNotReady);
        }

        var deviceId = deviceService.CurrentDevice?.DeviceId;
        if (deviceId is null)
        {
            return CapacityViewResult.Unavailable(CapacityQueryReasonCodes.DeviceNotIdentified);
        }

        return await sender.Send(
            new LoadTodayCapacityQuery(deviceId.Value, DateTime.Now, plcName),
            cancellationToken);
    }

    public async Task<CapacityViewResult> QueryHistoryAsync(
        string queryMode,
        DateTime queryDate,
        string plcName,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!deviceService.CanUploadToCloud)
        {
            return CapacityViewResult.Unavailable(CapacityQueryReasonCodes.CloudGateNotReady);
        }

        var deviceId = deviceService.CurrentDevice?.DeviceId;
        if (deviceId is null)
        {
            return CapacityViewResult.Unavailable(CapacityQueryReasonCodes.DeviceNotIdentified);
        }

        return await sender.Send(
            new QueryCapacityHistoryQuery(deviceId.Value, queryMode, queryDate, plcName),
            cancellationToken);
    }
}
