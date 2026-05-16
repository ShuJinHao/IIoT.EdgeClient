using IIoT.Edge.Application.Abstractions.Plc.Diagnostics;

namespace IIoT.Edge.Presentation.Navigation.Avalonia.Features.Hardware.IOView;

internal static class IoViewPlcWriteTraceFormatter
{
    public static PlcIoWriteTraceEntry? FindLatest(
        IPlcIoWriteTraceStore? writeTraceStore,
        int deviceId,
        IReadOnlyCollection<string> signalKeys,
        IoInteractionRowModel row)
    {
        if (writeTraceStore is null || signalKeys.Count == 0)
        {
            return null;
        }

        var keys = signalKeys.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var acceptedAt = row.LastRuntimeBufferAcceptedAt;
        return writeTraceStore.GetRecent()
            .FirstOrDefault(entry =>
                entry.DeviceId == deviceId
                && entry.SignalKeys.Any(keys.Contains)
                && (!row.AwaitingPlcWriteTrace
                    || acceptedAt is null
                    || entry.OccurredAt >= acceptedAt.Value));
    }

    public static string Format(
        PlcIoWriteTraceEntry trace,
        Func<string, string, string> text,
        Func<string, string, object[], string> format)
    {
        var status = trace.Kind switch
        {
            PlcIoWriteTraceKind.Attempt => text("Navigation_Io_PlcWriteTrace_Attempt", "尝试"),
            PlcIoWriteTraceKind.Success => text("Navigation_Io_PlcWriteTrace_Success", "成功"),
            PlcIoWriteTraceKind.Failed => text("Navigation_Io_PlcWriteTrace_Failed", "失败"),
            _ => trace.Kind.ToString()
        };
        var message = format(
            "Navigation_Io_PlcWriteTrace_Format",
            "PLC 块写入{0}：{1} / {2} 字 / {3:yyyy-MM-dd HH:mm:ss}",
            [status, trace.StartAddress, trace.WordCount, trace.OccurredAt.ToLocalTime()]);

        return string.IsNullOrWhiteSpace(trace.ErrorMessage)
            ? message
            : $"{message}；原因：{trace.ErrorMessage}";
    }
}
