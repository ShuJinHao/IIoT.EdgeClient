using System.Globalization;
using System.Text;

namespace IIoT.Edge.UI.Avalonia.Services;

public sealed class AvaloniaCsvExportService : IAvaloniaCsvExportService
{
    public async Task<string> ExportAsync(
        string directory,
        string pageType,
        IReadOnlyList<string> headers,
        IEnumerable<IReadOnlyList<object?>> rows,
        DateTime timestamp,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        ArgumentException.ThrowIfNullOrWhiteSpace(pageType);

        Directory.CreateDirectory(directory);
        var fileName = $"{SanitizeFileName(pageType)}_{timestamp:yyyyMMdd_HHmmss}.csv";
        var path = Path.Combine(directory, fileName);

        await using var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.Read);
        await using var writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));

        await writer.WriteLineAsync(string.Join(",", headers.Select(Escape)));
        foreach (var row in rows)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await writer.WriteLineAsync(string.Join(",", row.Select(FormatValue).Select(Escape)));
        }

        return path;
    }

    private static string FormatValue(object? value)
        => value switch
        {
            null => string.Empty,
            IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
            _ => value.ToString() ?? string.Empty
        };

    private static string Escape(string value)
    {
        if (!value.Contains(',') && !value.Contains('"') && !value.Contains('\r') && !value.Contains('\n'))
        {
            return value;
        }

        return $"\"{value.Replace("\"", "\"\"")}\"";
    }

    private static string SanitizeFileName(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var chars = value.Select(ch => invalid.Contains(ch) ? '_' : ch).ToArray();
        return new string(chars);
    }
}
