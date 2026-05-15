namespace IIoT.Edge.UI.Avalonia.Services;

public sealed class AvaloniaDataExportService(IAvaloniaCsvExportService csvExportService)
    : IAvaloniaDataExportService
{
    public async Task<AvaloniaDataExportResult> ExportAsync(
        AvaloniaDataExportRequest request,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var path = await csvExportService.ExportAsync(
                request.Directory,
                request.PageType,
                request.Headers,
                request.Rows,
                request.Timestamp,
                cancellationToken);

            return AvaloniaDataExportResult.Success(path);
        }
        catch (Exception ex)
        {
            return AvaloniaDataExportResult.Failure(ex.Message);
        }
    }
}
