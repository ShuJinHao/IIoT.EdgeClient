using IIoT.Edge.Module.Contracts.Plc;
using IIoT.Edge.Module.Contracts.Identity;

namespace IIoT.Edge.Application.Features.Production.Monitor;

internal sealed class MonitorSnapshotSourceMatcher(IPlcConnectionManager plcConnectionManager)
    : IMonitorSnapshotSourceMatcher
{
    public IReadOnlyCollection<PlcConnectionRuntimeSnapshot> GetRuntimeStatuses()
        => plcConnectionManager?.GetRuntimeStatuses() ?? [];

    public PlcConnectionRuntimeSnapshot? ResolveRuntimeStatus(IDeviceIdentifiable source)
    {
        if (source.NetworkDeviceId > 0)
        {
            var byId = plcConnectionManager.GetRuntimeStatus(source.NetworkDeviceId);
            if (byId is not null)
            {
                return byId;
            }
        }

        return plcConnectionManager.GetRuntimeStatuses()
            .FirstOrDefault(snapshot =>
                string.Equals(snapshot.DeviceName, source.DeviceName, StringComparison.OrdinalIgnoreCase));
    }

    public T? ResolveConfiguredDevice<T>(IDeviceIdentifiable source, IReadOnlyList<T> devices)
        where T : class, IDeviceIdentifiable
    {
        if (source.NetworkDeviceId > 0)
        {
            var byId = devices.FirstOrDefault(device => device.NetworkDeviceId == source.NetworkDeviceId);
            if (byId is not null)
            {
                return byId;
            }
        }

        return devices.FirstOrDefault(device =>
            string.Equals(device.DeviceName, source.DeviceName, StringComparison.OrdinalIgnoreCase));
    }
}
