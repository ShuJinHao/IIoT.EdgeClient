namespace IIoT.Edge.Launcher.Services;

public static class LauncherStartupDiagnosticAreas
{
    public const string Language = "Language";
    public const string AccountCatalog = "AccountCatalog";
    public const string UpdateConfiguration = "UpdateConfiguration";
    public const string UpdateRecovery = "UpdateRecovery";
    public const string EnabledPluginSelection = "EnabledPluginSelection";
    public const string PluginActivationDiscovery = "PluginActivationDiscovery";
    public const string PluginActivationMaterialization = "PluginActivationMaterialization";
    public const string DeviceBinding = "DeviceBinding";
}

public static class LauncherStartupDiagnosticRepairTargets
{
    public const string LauncherConfiguration = "Launcher.Configuration";
    public const string LocalAccount = "Launcher.LocalAccount";
    public const string UpdateRecovery = "Launcher.UpdateRecovery";
    public const string PluginSelection = "Launcher.PluginSelection";
    public const string PluginActivation = "Launcher.PluginActivation";
    public const string DeviceBinding = "Launcher.DeviceBinding";
}

public sealed record LauncherStartupDiagnostic(
    string Area,
    string ReasonCode,
    string RepairTarget,
    string? Subject = null,
    string? ExceptionType = null);

public interface ILauncherStartupDiagnosticReader
{
    IReadOnlyList<LauncherStartupDiagnostic> Snapshot { get; }

    event EventHandler? Changed;
}

public interface ILauncherStartupDiagnosticWriter
{
    void ReplaceArea(
        string area,
        IReadOnlyCollection<LauncherStartupDiagnostic> diagnostics);
}

public sealed class LauncherStartupDiagnosticStore :
    ILauncherStartupDiagnosticReader,
    ILauncherStartupDiagnosticWriter
{
    private readonly object _syncRoot = new();
    private readonly Dictionary<string, IReadOnlyList<LauncherStartupDiagnostic>> _byArea =
        new(StringComparer.Ordinal);

    public IReadOnlyList<LauncherStartupDiagnostic> Snapshot
    {
        get
        {
            lock (_syncRoot)
            {
                return _byArea.Values
                    .SelectMany(static values => values)
                    .OrderBy(static item => item.Area, StringComparer.Ordinal)
                    .ThenBy(static item => item.ReasonCode, StringComparer.Ordinal)
                    .ThenBy(static item => item.Subject, StringComparer.OrdinalIgnoreCase)
                    .ToArray();
            }
        }
    }

    public event EventHandler? Changed;

    public void ReplaceArea(
        string area,
        IReadOnlyCollection<LauncherStartupDiagnostic> diagnostics)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(area);
        ArgumentNullException.ThrowIfNull(diagnostics);

        var normalized = diagnostics
            .Where(item => string.Equals(item.Area, area, StringComparison.Ordinal))
            .Select(Normalize)
            .Distinct()
            .OrderBy(static item => item.ReasonCode, StringComparer.Ordinal)
            .ThenBy(static item => item.Subject, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var changed = false;
        lock (_syncRoot)
        {
            if (normalized.Length == 0)
            {
                changed = _byArea.Remove(area);
            }
            else if (!_byArea.TryGetValue(area, out var current)
                     || !current.SequenceEqual(normalized))
            {
                _byArea[area] = normalized;
                changed = true;
            }
        }

        if (changed)
        {
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    private static LauncherStartupDiagnostic Normalize(LauncherStartupDiagnostic diagnostic)
    {
        ArgumentNullException.ThrowIfNull(diagnostic);
        if (string.IsNullOrWhiteSpace(diagnostic.ReasonCode)
            || diagnostic.ReasonCode.Length > 128
            || diagnostic.ReasonCode.Any(char.IsControl))
        {
            throw new ArgumentException("Launcher 诊断原因码无效。", nameof(diagnostic));
        }

        if (string.IsNullOrWhiteSpace(diagnostic.RepairTarget)
            || diagnostic.RepairTarget.Length > 128
            || diagnostic.RepairTarget.Any(char.IsControl))
        {
            throw new ArgumentException("Launcher 诊断修复入口无效。", nameof(diagnostic));
        }

        return diagnostic with
        {
            Area = diagnostic.Area.Trim(),
            ReasonCode = diagnostic.ReasonCode.Trim(),
            RepairTarget = diagnostic.RepairTarget.Trim(),
            Subject = NormalizeOptional(diagnostic.Subject, 256),
            ExceptionType = NormalizeOptional(diagnostic.ExceptionType, 256)
        };
    }

    private static string? NormalizeOptional(string? value, int maximumLength)
    {
        var normalized = value?.Trim();
        return string.IsNullOrWhiteSpace(normalized)
               || normalized.Length > maximumLength
               || normalized.Any(char.IsControl)
            ? null
            : normalized;
    }
}
