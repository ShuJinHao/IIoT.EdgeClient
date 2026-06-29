using IIoT.Edge.Application.Features.Production.Monitor;
using IIoT.Edge.Presentation.Panels.Features.DeviceSelection;

namespace IIoT.Edge.Presentation.Navigation.Features.Production.Monitor;

internal static class MonitorViewModelSnapshotApplier
{
    public static string? ResolveSelectedDevice(
        IReadOnlyList<DeviceMonitorSnapshot> snapshots,
        string? selectedDevice)
    {
        if (snapshots.Count == 0
            || string.IsNullOrWhiteSpace(selectedDevice)
            || string.Equals(selectedDevice, IDeviceSelectionService.AllFilterKey, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        if (snapshots.Any(snapshot => string.Equals(snapshot.DeviceName, selectedDevice, StringComparison.OrdinalIgnoreCase)))
        {
            return selectedDevice;
        }

        return null;
    }

    public static DeviceMonitorSnapshot? FindSelectedSnapshot(
        IReadOnlyList<DeviceMonitorSnapshot> snapshots,
        string? selectedDevice)
        => string.IsNullOrWhiteSpace(selectedDevice)
            ? null
            : snapshots.FirstOrDefault(snapshot =>
                string.Equals(snapshot.DeviceName, selectedDevice, StringComparison.Ordinal));
}
