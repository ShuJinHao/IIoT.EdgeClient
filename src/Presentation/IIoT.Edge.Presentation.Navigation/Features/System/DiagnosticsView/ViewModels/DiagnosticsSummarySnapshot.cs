namespace IIoT.Edge.Presentation.Navigation.Features.DiagnosticsView;

internal sealed record DiagnosticsSummarySnapshot
{
    public string DiscoveredModulesSummary { get; init; } = string.Empty;
    public string EnabledModulesSummary { get; init; } = string.Empty;
    public string ActivatedModulesSummary { get; init; } = string.Empty;
    public string ConfigurationProfileSummary { get; init; } = string.Empty;
    public string LastUpdatedSummary { get; init; } = string.Empty;
    public string DeviceSummary { get; init; } = string.Empty;
    public string CloudGateSummary { get; init; } = string.Empty;
    public string CloudRuntimeSummary { get; init; } = string.Empty;
    public string CloudResultSummary { get; init; } = string.Empty;
    public string CloudPendingSummary { get; init; } = string.Empty;
    public string CloudCapacitySummary { get; init; } = string.Empty;
    public string CloudPersistenceSummary { get; init; } = string.Empty;
    public string CloudLastAttemptSummary { get; init; } = string.Empty;
    public string CloudLastSuccessSummary { get; init; } = string.Empty;
    public string CloudLastFailureSummary { get; init; } = string.Empty;
    public string MesRuntimeSummary { get; init; } = string.Empty;
    public string MesPendingSummary { get; init; } = string.Empty;
    public string MesCapacitySummary { get; init; } = string.Empty;
    public string MesPersistenceSummary { get; init; } = string.Empty;
    public string MesLastAttemptSummary { get; init; } = string.Empty;
    public string MesLastSuccessSummary { get; init; } = string.Empty;
    public string MesLastFailureSummary { get; init; } = string.Empty;
    public string ContextPersistenceSummary { get; init; } = string.Empty;
    public string? StartupStatusMessage { get; init; }
}

