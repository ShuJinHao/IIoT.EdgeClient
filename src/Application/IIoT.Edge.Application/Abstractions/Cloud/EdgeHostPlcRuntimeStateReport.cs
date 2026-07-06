namespace IIoT.Edge.Application.Abstractions.Cloud;

public interface IEdgeHostPlcRuntimeStateSnapshotProvider
{
    Task<IReadOnlyList<EdgeHostPlcRuntimeStateReportItem>> GetCurrentAsync(
        CancellationToken cancellationToken = default);
}

public interface IEdgeHostPlcRuntimeStateReporter
{
    Task<EdgeHostPlcRuntimeStateReportResult> ReportOnceAsync(
        CancellationToken cancellationToken = default);
}

public sealed record EdgeHostPlcRuntimeStateReport(
    Guid DeviceId,
    string ClientCode,
    DateTime ReportedAtUtc,
    IReadOnlyList<EdgeHostPlcRuntimeStateReportItem> PlcStates);

public sealed record EdgeHostPlcRuntimeStateReportItem(
    string PlcCode,
    string? ReportedPlcName,
    bool IsConnected,
    string? RuntimeStatus = null,
    DateTime? ObservedAtUtc = null,
    string? StationCode = null,
    string? Protocol = null,
    string? Address = null,
    string? LastError = null);

public sealed record EdgeHostPlcRuntimeStateReportResult(
    bool Success,
    int ReportedCount,
    string? ReasonCode = null)
{
    public static EdgeHostPlcRuntimeStateReportResult Succeeded(int reportedCount)
        => new(true, reportedCount, "success");

    public static EdgeHostPlcRuntimeStateReportResult Skipped(string reasonCode)
        => new(false, 0, reasonCode);

    public static EdgeHostPlcRuntimeStateReportResult Failed(string reasonCode)
        => new(false, 0, reasonCode);
}
