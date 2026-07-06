using IIoT.Edge.Application.Abstractions.Modules;

using IIoT.Edge.Application.Abstractions.Mes;
namespace IIoT.Edge.Shell.Core;

public sealed class MesUploadDiagnosticsStore : IMesUploadDiagnosticsStore
{
    private readonly Dictionary<string, MesChannelDiagnostics> _diagnostics = new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyList<MesChannelDiagnostics> GetAll()
        => _diagnostics.Values
            .OrderBy(x => x.ProcessType, StringComparer.OrdinalIgnoreCase)
            .ToArray();

    public MesChannelDiagnostics? Get(string processType)
    {
        if (string.IsNullOrWhiteSpace(processType))
        {
            return null;
        }

        if (_diagnostics.TryGetValue(processType, out var diagnostics))
        {
            return diagnostics;
        }

        var matches = _diagnostics.Values
            .Where(x => string.Equals(x.ProcessType, processType, StringComparison.OrdinalIgnoreCase))
            .Take(2)
            .ToArray();
        return matches.Length == 1 ? matches[0] : null;
    }

    public void RecordSuccess(string processType, MesUploadDiagnosticsContext? context = null)
    {
        Upsert(processType, context, existing =>
        {
            var now = DateTime.UtcNow;
            return existing with
            {
                LastAttemptAt = now,
                LastSuccessAt = now,
                LastResult = "Success",
                LastFailureReason = null,
                LastBlockedAt = null,
                LastBlockedReason = null
            };
        });
    }

    public void RecordFailure(string processType, string failureReason, MesUploadDiagnosticsContext? context = null)
    {
        Upsert(processType, context, existing => existing with
        {
            LastAttemptAt = DateTime.UtcNow,
            LastResult = "Failed",
            LastFailureReason = failureReason,
            LastBlockedAt = null,
            LastBlockedReason = null
        });
    }

    public void RecordBlocked(string processType, string blockedReason, MesUploadDiagnosticsContext? context = null)
    {
        Upsert(processType, context, existing =>
        {
            var now = DateTime.UtcNow;
            return existing with
            {
                LastAttemptAt = now,
                LastResult = "Blocked",
                LastBlockedAt = now,
                LastBlockedReason = blockedReason,
                LastFailureReason = null
            };
        });
    }

    private void Upsert(
        string processType,
        MesUploadDiagnosticsContext? context,
        Func<MesChannelDiagnostics, MesChannelDiagnostics> update)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(processType);
        ArgumentNullException.ThrowIfNull(update);

        var key = BuildKey(processType, context);
        var current = _diagnostics.TryGetValue(key, out var existing)
            ? existing
            : CreateInitial(processType, context);

        _diagnostics[key] = update(current);
    }

    private static MesChannelDiagnostics CreateInitial(
        string processType,
        MesUploadDiagnosticsContext? context)
        => new(
            processType,
            null,
            null,
            "NoAttempts",
            null,
            DeviceName: Normalize(context?.DeviceName),
            ModuleId: Normalize(context?.ModuleId),
            TaskKey: Normalize(context?.TaskKey),
            Scenario: Normalize(context?.Scenario));

    private static string BuildKey(string processType, MesUploadDiagnosticsContext? context)
    {
        var deviceName = Normalize(context?.DeviceName);
        var taskKey = Normalize(context?.TaskKey);
        if (deviceName is null && taskKey is null)
        {
            return processType;
        }

        return $"{processType}|{deviceName ?? string.Empty}|{taskKey ?? string.Empty}";
    }

    private static string? Normalize(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
