using System.Threading;

namespace IIoT.Edge.Presentation.Navigation.Features.DiagnosticsView;

public interface IDiagnosticsRefreshCoordinator
{
    Task RunIfIdleAsync(Func<CancellationToken, Task> refreshCore, CancellationToken ct = default);
}

internal sealed class DiagnosticsRefreshCoordinator : IDiagnosticsRefreshCoordinator
{
    private int _refreshInProgress;

    public async Task RunIfIdleAsync(Func<CancellationToken, Task> refreshCore, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(refreshCore);

        if (Interlocked.Exchange(ref _refreshInProgress, 1) == 1)
        {
            return;
        }

        try
        {
            await refreshCore(ct);
        }
        finally
        {
            Volatile.Write(ref _refreshInProgress, 0);
        }
    }
}
