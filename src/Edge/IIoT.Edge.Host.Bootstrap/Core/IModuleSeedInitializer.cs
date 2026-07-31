using IIoT.Edge.Module.Contracts.Diagnostics;

namespace IIoT.Edge.Shell.Core;

public interface IModuleSeedInitializer
{
    Task<IReadOnlyList<StartupDiagnosticIssue>> ApplyConfigurationAsync(
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<StartupDiagnosticIssue>> RestoreRuntimeStateAsync(
        CancellationToken cancellationToken = default);
}
