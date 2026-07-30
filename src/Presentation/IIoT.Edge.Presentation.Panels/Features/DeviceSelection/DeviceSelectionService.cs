using IIoT.Edge.Module.Contracts.UI;

namespace IIoT.Edge.Presentation.Panels.Features.DeviceSelection;

public interface IDeviceSelectionService : IDeviceSelectionContext
{
    new const string AllFilterKey = IDeviceSelectionContext.AllFilterKey;

    string? SelectedPlcCode { get; }

    void SelectDevice(string deviceKey);

    void UpdatePlcIdentities(IEnumerable<PlcDeviceSelectionIdentity> identities);
}

public sealed class DeviceSelectionService : IDeviceSelectionService
{
    private string _selectedDeviceKey = IDeviceSelectionService.AllFilterKey;
    private IReadOnlyDictionary<string, string> _plcCodeByDeviceName =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    public string SelectedDeviceKey => _selectedDeviceKey;

    public string? SelectedPlcCode
        => _plcCodeByDeviceName.TryGetValue(_selectedDeviceKey, out var plcCode)
            ? plcCode
            : null;

    public bool IsAllSelected => string.Equals(
        _selectedDeviceKey,
        IDeviceSelectionContext.AllFilterKey,
        StringComparison.OrdinalIgnoreCase);

    public event EventHandler? SelectionChanged;

    public void SelectDevice(string deviceKey)
    {
        var normalizedKey = string.IsNullOrWhiteSpace(deviceKey)
            ? IDeviceSelectionService.AllFilterKey
            : deviceKey.Trim();
        if (string.Equals(_selectedDeviceKey, normalizedKey, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        _selectedDeviceKey = normalizedKey;
        SelectionChanged?.Invoke(this, EventArgs.Empty);
    }

    public void UpdatePlcIdentities(IEnumerable<PlcDeviceSelectionIdentity> identities)
    {
        ArgumentNullException.ThrowIfNull(identities);

        var previousSelectedPlcCode = SelectedPlcCode;
        var candidates = identities
            .Select(static identity => new PlcDeviceSelectionIdentity(
                identity.DeviceName?.Trim() ?? string.Empty,
                identity.PlcCode?.Trim() ?? string.Empty))
            .Where(static identity =>
                !string.IsNullOrWhiteSpace(identity.DeviceName)
                && !string.IsNullOrWhiteSpace(identity.PlcCode))
            .ToArray();
        var uniqueCodes = candidates
            .GroupBy(static identity => identity.PlcCode, StringComparer.OrdinalIgnoreCase)
            .Where(static group => group.Take(2).Count() == 1)
            .Select(static group => group.Key)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        _plcCodeByDeviceName = candidates
            .Where(identity => uniqueCodes.Contains(identity.PlcCode))
            .GroupBy(static identity => identity.DeviceName, StringComparer.OrdinalIgnoreCase)
            .Where(static group => group.Take(2).Count() == 1)
            .ToDictionary(
                static group => group.Key,
                static group => group.First().PlcCode,
                StringComparer.OrdinalIgnoreCase);

        if (!string.Equals(
                previousSelectedPlcCode,
                SelectedPlcCode,
                StringComparison.OrdinalIgnoreCase))
        {
            SelectionChanged?.Invoke(this, EventArgs.Empty);
        }
    }
}

public sealed record PlcDeviceSelectionIdentity(string DeviceName, string PlcCode);

public sealed record DeviceSelectionOption(string Key, string DisplayName)
{
    public override string ToString() => DisplayName;
}
