using IIoT.Edge.Application.Features.Production.Monitor;

namespace IIoT.Edge.Presentation.Navigation.Features.Production.Monitor;

internal static class MonitorViewModelSnapshotApplier
{
    public static string? ResolveSelectedDevice(
        IReadOnlyList<DeviceMonitorSnapshot> snapshots,
        string? selectedDevice)
    {
        if (snapshots.Count == 0)
        {
            return null;
        }

        if (!string.IsNullOrWhiteSpace(selectedDevice)
            && snapshots.Any(snapshot => string.Equals(snapshot.DeviceName, selectedDevice, StringComparison.Ordinal)))
        {
            return selectedDevice;
        }

        return snapshots[0].DeviceName;
    }

    public static DeviceMonitorSnapshot? FindSelectedSnapshot(
        IReadOnlyList<DeviceMonitorSnapshot> snapshots,
        string? selectedDevice)
        => string.IsNullOrWhiteSpace(selectedDevice)
            ? null
            : snapshots.FirstOrDefault(snapshot =>
                string.Equals(snapshot.DeviceName, selectedDevice, StringComparison.Ordinal));
}
