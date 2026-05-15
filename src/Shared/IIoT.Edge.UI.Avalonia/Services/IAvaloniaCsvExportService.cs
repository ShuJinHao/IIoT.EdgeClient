namespace IIoT.Edge.UI.Avalonia.Services;

public interface IAvaloniaCsvExportService
{
    Task<string> ExportAsync(
        string directory,
        string pageType,
        IReadOnlyList<string> headers,
        IEnumerable<IReadOnlyList<object?>> rows,
        DateTime timestamp,
        CancellationToken cancellationToken = default);
}
