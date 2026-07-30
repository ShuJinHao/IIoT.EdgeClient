using IIoT.Edge.Module.Contracts.UI;
using IIoT.Edge.Application.Common.Identity;

namespace IIoT.Edge.Presentation.Panels.Features.DeviceSelection;

public interface IDeviceSelectionService : IPlcDeviceSelectionContext
{
    new const string AllFilterKey = IDeviceSelectionContext.AllFilterKey;

    IReadOnlyList<string> SelectedDeviceNameAliases { get; }

    void SelectDevice(string deviceKey);

    void UpdatePlcIdentities(IEnumerable<PlcDeviceSelectionIdentity> identities);
}

public sealed class DeviceSelectionService : IDeviceSelectionService
{
    private readonly IPlcIdentityAliasRegistry _identityAliasRegistry;
    private string _selectedDeviceKey = IDeviceSelectionService.AllFilterKey;
    private string? _selectedPlcCode;
    private IReadOnlyDictionary<string, string> _plcCodeByDeviceName =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    private IReadOnlyDictionary<string, string> _deviceNameByPlcCode =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, HashSet<string>> _knownDeviceNamesByPlcCode =
        new(StringComparer.OrdinalIgnoreCase);

    public DeviceSelectionService(IPlcIdentityAliasRegistry? identityAliasRegistry = null)
    {
        _identityAliasRegistry =
            identityAliasRegistry ?? new InMemoryPlcIdentityAliasRegistry();
    }

    public string SelectedDeviceKey => _selectedDeviceKey;

    public string? SelectedPlcCode => _selectedPlcCode;

    public IReadOnlyList<string> SelectedDeviceNameAliases
    {
        get
        {
            if (string.IsNullOrWhiteSpace(_selectedPlcCode)
                || !_deviceNameByPlcCode.ContainsKey(_selectedPlcCode)
                || !_knownDeviceNamesByPlcCode.TryGetValue(_selectedPlcCode, out var names))
            {
                return [];
            }

            var currentCodes = _deviceNameByPlcCode.Keys.ToHashSet(StringComparer.OrdinalIgnoreCase);
            return names
                .Where(name =>
                    !currentCodes.Contains(name)
                    || string.Equals(name, _selectedPlcCode, StringComparison.OrdinalIgnoreCase))
                .Where(name =>
                    !_plcCodeByDeviceName.TryGetValue(name, out var currentCode)
                    || string.Equals(currentCode, _selectedPlcCode, StringComparison.OrdinalIgnoreCase))
                .Where(name => _knownDeviceNamesByPlcCode
                    .Where(pair => currentCodes.Contains(pair.Key))
                    .Count(pair => pair.Value.Contains(name)) == 1)
                .OrderByDescending(name => string.Equals(
                    name,
                    _deviceNameByPlcCode[_selectedPlcCode],
                    StringComparison.OrdinalIgnoreCase))
                .ThenBy(static name => name, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }
    }

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
        var resolvedPlcCode = string.Equals(
            normalizedKey,
            IDeviceSelectionService.AllFilterKey,
            StringComparison.OrdinalIgnoreCase)
            ? null
            : _plcCodeByDeviceName.GetValueOrDefault(normalizedKey);
        if (string.Equals(_selectedDeviceKey, normalizedKey, StringComparison.OrdinalIgnoreCase)
            && string.Equals(_selectedPlcCode, resolvedPlcCode, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        _selectedDeviceKey = normalizedKey;
        _selectedPlcCode = resolvedPlcCode;
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
        var uniqueNames = candidates
            .GroupBy(static identity => identity.DeviceName, StringComparer.OrdinalIgnoreCase)
            .Where(static group => group.Take(2).Count() == 1)
            .Select(static group => group.Key)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var resolvable = candidates
            .Where(identity =>
                uniqueCodes.Contains(identity.PlcCode)
                && uniqueNames.Contains(identity.DeviceName))
            .ToArray();
        _plcCodeByDeviceName = resolvable
            .ToDictionary(
                static identity => identity.DeviceName,
                static identity => identity.PlcCode,
                StringComparer.OrdinalIgnoreCase);
        _deviceNameByPlcCode = resolvable
            .ToDictionary(
                static identity => identity.PlcCode,
                static identity => identity.DeviceName,
                StringComparer.OrdinalIgnoreCase);

        foreach (var identity in resolvable)
        {
            _identityAliasRegistry.ObserveVerifiedAlias(
                identity.PlcCode,
                identity.DeviceName);
        }

        foreach (var identity in resolvable)
        {
            if (!_knownDeviceNamesByPlcCode.TryGetValue(identity.PlcCode, out var aliases))
            {
                aliases = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                _knownDeviceNamesByPlcCode.Add(identity.PlcCode, aliases);
            }

            aliases.Clear();
            aliases.UnionWith(
                _identityAliasRegistry.GetVerifiedAliases(identity.PlcCode));
        }

        _selectedPlcCode = IsAllSelected
            ? null
            : !string.IsNullOrWhiteSpace(previousSelectedPlcCode)
                ? previousSelectedPlcCode
                : _plcCodeByDeviceName.GetValueOrDefault(_selectedDeviceKey);

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
    public string PlcCode { get; init; } = string.Empty;

    public bool IsResolved { get; init; } = true;

    public override string ToString() => DisplayName;
}
