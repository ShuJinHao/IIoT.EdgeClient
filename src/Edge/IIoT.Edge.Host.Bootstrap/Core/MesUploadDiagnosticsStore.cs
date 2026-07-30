using IIoT.Edge.Module.Contracts.Modules;

using IIoT.Edge.Module.Contracts.Mes;
namespace IIoT.Edge.Shell.Core;

public sealed class MesUploadDiagnosticsStore : IMesUploadDiagnosticsStore
{
    private readonly object _sync = new();
    private readonly Dictionary<string, MesChannelDiagnostics> _diagnostics = new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyList<MesChannelDiagnostics> GetAll()
    {
        lock (_sync)
        {
            return _diagnostics.Values
                .OrderBy(x => x.ProcessType, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }
    }

    public MesChannelDiagnostics? Get(string processType)
    {
        if (string.IsNullOrWhiteSpace(processType))
        {
            return null;
        }

        lock (_sync)
        {
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

        lock (_sync)
        {
            var key = BuildKey(processType, context);
            var current = _diagnostics.TryGetValue(key, out var existing)
                ? ApplyContext(existing, context)
                : CreateInitial(processType, context);

            _diagnostics[key] = update(current);
        }
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
                Scenario: Normalize(context?.Scenario))
            {
                PlcCode = Normalize(context?.PlcCode)
            };

    private static string BuildKey(string processType, MesUploadDiagnosticsContext? context)
    {
        var plcCode = Normalize(context?.PlcCode);
        var taskKey = Normalize(context?.TaskKey);
        if (plcCode is null && taskKey is null)
        {
            return processType;
        }

        return $"{processType}|{plcCode ?? "<unresolved>"}|{taskKey ?? string.Empty}";
    }

    private static MesChannelDiagnostics ApplyContext(
        MesChannelDiagnostics existing,
        MesUploadDiagnosticsContext? context)
    {
        if (context is null)
        {
            return existing;
        }

        return existing with
        {
            PlcCode = Normalize(context.PlcCode) ?? existing.PlcCode,
            DeviceName = Normalize(context.DeviceName) ?? existing.DeviceName,
            ModuleId = Normalize(context.ModuleId) ?? existing.ModuleId,
            TaskKey = Normalize(context.TaskKey) ?? existing.TaskKey,
            Scenario = Normalize(context.Scenario) ?? existing.Scenario
        };
    }

    private static string? Normalize(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
