using System.Globalization;
using System.Text.RegularExpressions;
using IIoT.Edge.Application.Context;
using IIoT.Edge.Application.Abstractions.Logging;

namespace IIoT.Edge.Runtime.Context;

internal interface IProductionContextCorruptFileQuarantine
{
    string? TryQuarantine(string persistPath, string persistFileName);

    ProductionContextPersistenceDiagnostics BuildDiagnostics(string persistPath);
}

internal sealed class ProductionContextCorruptFileQuarantine(ILogService logger)
    : IProductionContextCorruptFileQuarantine
{
    private static readonly Regex CorruptFileTimestampPattern = new(
        @"^production_context\.corrupt-(\d{17})(?:-\d+)?\.json$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public string? TryQuarantine(string persistPath, string persistFileName)
    {
        try
        {
            var directory = Path.GetDirectoryName(persistPath) ?? ".";
            var baseName = Path.GetFileNameWithoutExtension(persistFileName);
            var extension = Path.GetExtension(persistFileName);
            var timestamp = DateTime.UtcNow.ToString("yyyyMMddHHmmssfff", CultureInfo.InvariantCulture);
            var candidatePath = Path.Combine(directory, $"{baseName}.corrupt-{timestamp}{extension}");
            var suffix = 0;

            while (File.Exists(candidatePath))
            {
                suffix++;
                candidatePath = Path.Combine(directory, $"{baseName}.corrupt-{timestamp}-{suffix}{extension}");
            }

            File.Move(persistPath, candidatePath);
            return candidatePath;
        }
        catch (Exception moveEx)
        {
            logger.Error($"[ContextStore] 隔离损坏运行状态失败：{moveEx.Message}");
            return null;
        }
    }

    public ProductionContextPersistenceDiagnostics BuildDiagnostics(string persistPath)
    {
        var directory = Path.GetDirectoryName(persistPath) ?? ".";
        if (!Directory.Exists(directory))
        {
            return new ProductionContextPersistenceDiagnostics(0, null);
        }

        var files = Directory.GetFiles(directory, "production_context.corrupt-*.json");
        var lastCorruptDetectedAt = files
            .Select(ParseCorruptTimestamp)
            .Where(x => x.HasValue)
            .Max();

        return new ProductionContextPersistenceDiagnostics(
            CorruptFileCount: files.Length,
            LastCorruptDetectedAt: lastCorruptDetectedAt);
    }

    private static DateTime? ParseCorruptTimestamp(string path)
    {
        var fileName = Path.GetFileName(path);
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return null;
        }

        var match = CorruptFileTimestampPattern.Match(fileName);
        if (match.Success
            && DateTime.TryParseExact(
                match.Groups[1].Value,
                "yyyyMMddHHmmssfff",
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out var timestampUtc))
        {
            return timestampUtc;
        }

        return File.Exists(path)
            ? File.GetLastWriteTimeUtc(path)
            : null;
    }
}
