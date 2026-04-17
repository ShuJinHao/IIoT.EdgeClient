using IIoT.Edge.Application.Abstractions.Modules;

namespace IIoT.Edge.Shell.Core;

public sealed class StartupDiagnosticsStore : IStartupDiagnosticsStore
{
    private StartupDiagnosticsReport _current = StartupDiagnosticsReport.Empty();

    public StartupDiagnosticsReport Current => _current;

    public void Update(StartupDiagnosticsReport report)
    {
        _current = report ?? StartupDiagnosticsReport.Empty();
    }
}
