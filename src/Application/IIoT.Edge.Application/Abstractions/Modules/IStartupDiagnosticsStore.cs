namespace IIoT.Edge.Application.Abstractions.Modules;

public interface IStartupDiagnosticsStore
{
    StartupDiagnosticsReport Current { get; }

    void Update(StartupDiagnosticsReport report);
}
