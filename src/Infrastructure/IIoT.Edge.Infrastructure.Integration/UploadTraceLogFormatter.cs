using IIoT.Edge.Application.Common.DataPipeline;
using IIoT.Edge.Module.Contracts.DataPipeline;

namespace IIoT.Edge.Infrastructure.Integration;

internal static class UploadTraceLogFormatter
{
    public static string Format(CellCompletedRecord record, string channel)
    {
        ArgumentNullException.ThrowIfNull(record);
        ArgumentNullException.ThrowIfNull(record.CellData);
        return $"[CorrelationId={TryCreateCorrelationId(record)}]" +
               $"[Channel={Normalize(channel, "Unresolved")}]" +
               $"[PlcCode={Normalize(record.ResolvePlcCode(), "Unresolved")}]" +
               $"[ModuleId={Normalize(record.ModuleId, "Unresolved")}]" +
               $"[ProcessType={Normalize(record.CellData.ProcessType, "Unresolved")}]" +
               $"[TaskKey={Normalize(record.TaskKey, "Unresolved")}]" +
               $"[BusinessId={Normalize(record.CellData.DisplayLabel, "Unresolved")}]";
    }

    private static string TryCreateCorrelationId(CellCompletedRecord record)
    {
        try
        {
            return DataPipelineCompletionIdentity.Create(record);
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidDataException)
        {
            // Diagnostics must remain available for a malformed v3 envelope so the consumer
            // can fail it closed with the stable identity reason code instead of throwing while
            // formatting the log entry.
            return "InvalidIdentity";
        }
    }

    public static string ReasonCode(string prefix, Enum outcome)
        => $"{Normalize(prefix, "upload")}_{Normalize(outcome.ToString(), "unknown")}";

    private static string Normalize(string? value, string fallback)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return fallback;
        }

        var normalized = value.Trim()
            .Replace('\r', '_')
            .Replace('\n', '_')
            .Replace(']', '_');
        return normalized.Length <= 128 ? normalized : normalized[..128];
    }
}
