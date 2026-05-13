using IIoT.Edge.Application.Abstractions.Modules;

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

        return _diagnostics.TryGetValue(processType, out var diagnostics)
            ? diagnostics
            : null;
    }

    public void RecordSuccess(string processType)
    {
        Upsert(processType, static existing =>
        {
            var now = DateTime.UtcNow;
            return existing with
            {
                LastAttemptAt = now,
                LastSuccessAt = now,
                LastResult = "Success",
                LastFailureReason = null
            };
        });
    }

    public void RecordFailure(string processType, string failureReason)
    {
        Upsert(processType, existing => existing with
        {
            LastAttemptAt = DateTime.UtcNow,
            LastResult = "Failed",
            LastFailureReason = failureReason
        });
    }

    private void Upsert(string processType, Func<MesChannelDiagnostics, MesChannelDiagnostics> update)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(processType);
        ArgumentNullException.ThrowIfNull(update);

        var current = _diagnostics.TryGetValue(processType, out var existing)
            ? existing
            : new MesChannelDiagnostics(processType, null, null, "NoAttempts", null);

        _diagnostics[processType] = update(current);
    }
}
