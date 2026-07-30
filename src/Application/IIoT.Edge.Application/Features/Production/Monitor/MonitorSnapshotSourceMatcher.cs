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
        var snapshots = plcConnectionManager.GetRuntimeStatuses();
        if (source is IPlcIdentifiable plcSource
            && !string.IsNullOrWhiteSpace(plcSource.PlcCode))
        {
            var byPlcCode = snapshots
                .Where(snapshot => string.Equals(
                    snapshot.PlcCode,
                    plcSource.PlcCode,
                    StringComparison.OrdinalIgnoreCase))
                .Take(2)
                .ToArray();
            return byPlcCode.Length == 1 ? byPlcCode[0] : null;
        }

        if (source.NetworkDeviceId > 0)
        {
            var byId = plcConnectionManager.GetRuntimeStatus(source.NetworkDeviceId);
            if (byId is not null)
            {
                return byId;
            }
        }

        return null;
    }

    public T? ResolveConfiguredDevice<T>(IDeviceIdentifiable source, IReadOnlyList<T> devices)
        where T : class, IDeviceIdentifiable
    {
        if (source is IPlcIdentifiable plcSource
            && !string.IsNullOrWhiteSpace(plcSource.PlcCode))
        {
            var byPlcCode = devices
                .Where(device =>
                    device is IPlcIdentifiable identifiable
                    && string.Equals(
                        identifiable.PlcCode,
                        plcSource.PlcCode,
                        StringComparison.OrdinalIgnoreCase))
                .Take(2)
                .ToArray();
            return byPlcCode.Length == 1 ? byPlcCode[0] : null;
        }

        if (source.NetworkDeviceId > 0)
        {
            var byId = devices.FirstOrDefault(device => device.NetworkDeviceId == source.NetworkDeviceId);
            if (byId is not null)
            {
                return byId;
            }
        }

        return null;
    }
}
