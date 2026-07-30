using IIoT.Edge.Application.Features.Production.Monitor;
using IIoT.Edge.Presentation.Panels.Features.DeviceSelection;

namespace IIoT.Edge.Presentation.Navigation.Features.Production.Monitor;

internal static class MonitorViewModelSnapshotApplier
{
    public static string? ResolveSelectedDevice(
        IReadOnlyList<DeviceMonitorSnapshot> snapshots,
        string? selectedDevice,
        string? selectedPlcCode = null)
    {
        if (string.IsNullOrWhiteSpace(selectedDevice)
            || string.Equals(selectedDevice, IDeviceSelectionService.AllFilterKey, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var selectedKey = selectedDevice.Trim();
        if (!string.IsNullOrWhiteSpace(selectedPlcCode))
        {
            var byPlcCode = snapshots
                .Where(snapshot => string.Equals(
                    snapshot.PlcCode,
                    selectedPlcCode.Trim(),
                    StringComparison.OrdinalIgnoreCase))
                .Take(2)
                .ToArray();
            return byPlcCode.Length == 1
                ? byPlcCode[0].DeviceName
                : selectedKey;
        }

        var byDeviceName = snapshots
            .Where(snapshot => string.Equals(
                snapshot.DeviceName,
                selectedKey,
                StringComparison.Ordinal))
            .Take(2)
            .ToArray();
        if (byDeviceName.Length == 1)
        {
            return byDeviceName[0].DeviceName;
        }

        return selectedKey;
    }

    public static DeviceMonitorSnapshot? FindSelectedSnapshot(
        IReadOnlyList<DeviceMonitorSnapshot> snapshots,
        string? selectedDevice,
        string? selectedPlcCode = null)
    {
        if (string.IsNullOrWhiteSpace(selectedDevice))
        {
            return null;
        }

        var selectedKey = selectedDevice.Trim();
        if (!string.IsNullOrWhiteSpace(selectedPlcCode))
        {
            var byPlcCode = snapshots
                .Where(snapshot => string.Equals(
                    snapshot.PlcCode,
                    selectedPlcCode.Trim(),
                    StringComparison.OrdinalIgnoreCase))
                .Take(2)
                .ToArray();
            return byPlcCode.Length == 1 ? byPlcCode[0] : null;
        }

        var byDeviceName = snapshots
            .Where(snapshot => string.Equals(
                snapshot.DeviceName,
                selectedKey,
                StringComparison.Ordinal))
            .Take(2)
            .ToArray();
        return byDeviceName.Length == 1 ? byDeviceName[0] : null;
    }
}
