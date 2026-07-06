namespace IIoT.Edge.Application.Common.DataPipeline;

public static class DataPipelineUploadScenarioResolver
{
    public static string? Resolve(string? taskKey, string? recordKind, string? processType = null)
    {
        var value = $"{taskKey} {recordKind} {processType}";
        if (ContainsAny(value, "DeviceStatus", "EquipmentStatus"))
        {
            return "设备状态上传";
        }

        if (ContainsAny(value, "Realtime", "Sample"))
        {
            return "生产上传";
        }

        if (value.Contains("Inbound", StringComparison.OrdinalIgnoreCase))
        {
            return "进站上传";
        }

        if (value.Contains("Outbound", StringComparison.OrdinalIgnoreCase))
        {
            return "出站上传";
        }

        if (value.Contains("Recipe", StringComparison.OrdinalIgnoreCase))
        {
            return "配方上传";
        }

        return null;
    }

    public static bool IsDeviceStatus(string? taskKey, string? recordKind, string? processType = null)
        => string.Equals(Resolve(taskKey, recordKind, processType), "设备状态上传", StringComparison.Ordinal);

    public static string? TryReadRecordKind(object? cellData)
        => cellData?.GetType()
               .GetProperty("RecordKind")
               ?.GetValue(cellData)
               ?.ToString();

    private static bool ContainsAny(string value, params string[] tokens)
        => tokens.Any(token => value.Contains(token, StringComparison.OrdinalIgnoreCase));
}
