namespace IIoT.Edge.UI.Avalonia.Services;

public interface IAvaloniaDataExportService
{
    Task<AvaloniaDataExportResult> ExportAsync(
        AvaloniaDataExportRequest request,
        CancellationToken cancellationToken = default);
}

public sealed record AvaloniaDataExportRequest(
    string Directory,
    string PageType,
    IReadOnlyList<string> Headers,
    IEnumerable<IReadOnlyList<object?>> Rows,
    DateTime Timestamp);

public sealed record AvaloniaDataExportResult(
    bool IsSuccess,
    string? FilePath,
    string Message)
{
    public static AvaloniaDataExportResult Success(string filePath)
        => new(true, filePath, $"已导出：{filePath}");

    public static AvaloniaDataExportResult Failure(string message)
        => new(false, null, message);
}
