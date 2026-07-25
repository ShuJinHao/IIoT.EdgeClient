using IIoT.Edge.Module.Contracts.UI;

namespace IIoT.Edge.Presentation.Panels.Features.DeviceSelection;

public interface IDeviceSelectionService : IDeviceSelectionContext
{
    new const string AllFilterKey = IDeviceSelectionContext.AllFilterKey;

    void SelectDevice(string deviceKey);
}

public sealed class DeviceSelectionService : IDeviceSelectionService
{
    private string _selectedDeviceKey = IDeviceSelectionService.AllFilterKey;

    public string SelectedDeviceKey => _selectedDeviceKey;

    public bool IsAllSelected => string.Equals(
        _selectedDeviceKey,
        IDeviceSelectionContext.AllFilterKey,
        StringComparison.OrdinalIgnoreCase);

    public event EventHandler? SelectionChanged;

    public void SelectDevice(string deviceKey)
    {
        var normalizedKey = string.IsNullOrWhiteSpace(deviceKey)
            ? IDeviceSelectionService.AllFilterKey
            : deviceKey;
        if (string.Equals(_selectedDeviceKey, normalizedKey, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        _selectedDeviceKey = normalizedKey;
        SelectionChanged?.Invoke(this, EventArgs.Empty);
    }
}

public sealed record DeviceSelectionOption(string Key, string DisplayName)
{
    public override string ToString() => DisplayName;
}
