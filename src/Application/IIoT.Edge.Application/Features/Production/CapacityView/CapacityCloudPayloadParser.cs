using System.Globalization;
using System.Text.Json;

namespace IIoT.Edge.Application.Features.Production.CapacityView;

/// <summary>
/// 当前 Cloud 产能查询 JSON 契约解析器。
/// 只接受正式 camelCase object/null/array 契约，不保留旧字段或旧根节点兼容。
/// </summary>
internal static class CapacityCloudPayloadParser
{
    internal static CapacityQueryResult<IReadOnlyList<HourlyCapacitySlotSnapshot>> ParseHourly(
        JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Array)
        {
            return CapacityQueryResult<IReadOnlyList<HourlyCapacitySlotSnapshot>>.InvalidPayload(
                CapacityQueryReasonCodes.CapacityHourlyRootInvalid);
        }

        if (root.GetArrayLength() == 0)
        {
            return CapacityQueryResult<IReadOnlyList<HourlyCapacitySlotSnapshot>>.Empty();
        }

        var slots = new List<HourlyCapacitySlotSnapshot>();
        foreach (var item in root.EnumerateArray())
        {
            if (!TryReadRequiredInt(item, "hour", out var hour)
                || !TryReadRequiredInt(item, "minute", out var minute)
                || !TryReadRequiredInt(item, "totalCount", out var total)
                || !TryReadRequiredInt(item, "okCount", out var ok)
                || !TryReadRequiredInt(item, "ngCount", out var ng)
                || !TryReadRequiredString(item, "shiftCode", out var shift)
                || !TryReadRequiredString(item, "timeLabel", out var label)
                || hour is < 0 or > 23
                || minute is not (0 or 30)
                || total < 0
                || ok < 0
                || ng < 0)
            {
                return CapacityQueryResult<IReadOnlyList<HourlyCapacitySlotSnapshot>>.InvalidPayload(
                    CapacityQueryReasonCodes.CapacityHourlyItemInvalid);
            }

            slots.Add(new HourlyCapacitySlotSnapshot
            {
                SlotOrder = hour * 2 + (minute == 30 ? 1 : 0),
                Hour = hour,
                Minute = minute,
                StartHour = hour,
                StartMinute = minute,
                TimeLabel = label,
                ShiftCode = shift,
                TotalCount = total,
                OkCount = ok,
                NgCount = ng
            });
        }

        return CapacityQueryResult<IReadOnlyList<HourlyCapacitySlotSnapshot>>.Success(
            slots.OrderBy(slot => slot.SlotOrder).ToList());
    }

    internal static CapacityQueryResult<DailyCapacitySummarySnapshot> ParseSummary(JsonElement root)
    {
        if (root.ValueKind == JsonValueKind.Null)
        {
            return CapacityQueryResult<DailyCapacitySummarySnapshot>.Empty();
        }

        return root.ValueKind == JsonValueKind.Object && TryReadSummary(root, out var summary)
            ? CapacityQueryResult<DailyCapacitySummarySnapshot>.Success(summary)
            : CapacityQueryResult<DailyCapacitySummarySnapshot>.InvalidPayload(
                CapacityQueryReasonCodes.CapacitySummaryPayloadInvalid);
    }

    internal static CapacityQueryResult<IReadOnlyList<DailyCapacitySnapshot>> ParseSummaryRange(
        JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Array)
        {
            return CapacityQueryResult<IReadOnlyList<DailyCapacitySnapshot>>.InvalidPayload(
                CapacityQueryReasonCodes.CapacityRangeRootInvalid);
        }

        if (root.GetArrayLength() == 0)
        {
            return CapacityQueryResult<IReadOnlyList<DailyCapacitySnapshot>>.Empty();
        }

        var rows = new List<DailyCapacitySnapshot>();
        foreach (var item in root.EnumerateArray())
        {
            if (!TryReadRequiredString(item, "date", out var dateText)
                || !DateOnly.TryParseExact(
                    dateText,
                    "yyyy-MM-dd",
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.None,
                    out var date)
                || !TryReadSummary(item, out var summary))
            {
                return CapacityQueryResult<IReadOnlyList<DailyCapacitySnapshot>>.InvalidPayload(
                    CapacityQueryReasonCodes.CapacityRangeItemInvalid);
            }

            var dateTime = date.ToDateTime(TimeOnly.MinValue);
            rows.Add(new DailyCapacitySnapshot
            {
                Date = dateTime.ToString("MM-dd", CultureInfo.InvariantCulture),
                DateFull = dateText,
                DayOfWeek = dateTime.ToString("ddd", CultureInfo.CurrentCulture),
                Total = summary.TotalCount,
                OkCount = summary.OkCount,
                NgCount = summary.NgCount,
                Yield = summary.TotalCount > 0
                    ? $"{summary.OkCount * 100.0 / summary.TotalCount:F1}%"
                    : "0%",
                DayShiftTotal = summary.DayShiftTotal,
                DayShiftOk = summary.DayShiftOk,
                DayShiftNg = summary.DayShiftNg,
                NightShiftTotal = summary.NightShiftTotal,
                NightShiftOk = summary.NightShiftOk,
                NightShiftNg = summary.NightShiftNg
            });
        }

        return CapacityQueryResult<IReadOnlyList<DailyCapacitySnapshot>>.Success(rows);
    }

    private static bool TryReadSummary(
        JsonElement root,
        out DailyCapacitySummarySnapshot summary)
    {
        summary = new DailyCapacitySummarySnapshot();
        if (!TryReadRequiredInt(root, "totalCount", out var total)
            || !TryReadRequiredInt(root, "okCount", out var ok)
            || !TryReadRequiredInt(root, "ngCount", out var ng)
            || !TryReadRequiredInt(root, "dayShiftTotal", out var dayTotal)
            || !TryReadRequiredInt(root, "dayShiftOk", out var dayOk)
            || !TryReadRequiredInt(root, "dayShiftNg", out var dayNg)
            || !TryReadRequiredInt(root, "nightShiftTotal", out var nightTotal)
            || !TryReadRequiredInt(root, "nightShiftOk", out var nightOk)
            || !TryReadRequiredInt(root, "nightShiftNg", out var nightNg)
            || total < 0
            || ok < 0
            || ng < 0
            || dayTotal < 0
            || dayOk < 0
            || dayNg < 0
            || nightTotal < 0
            || nightOk < 0
            || nightNg < 0)
        {
            return false;
        }

        summary = new DailyCapacitySummarySnapshot
        {
            TotalCount = total,
            OkCount = ok,
            NgCount = ng,
            DayShiftTotal = dayTotal,
            DayShiftOk = dayOk,
            DayShiftNg = dayNg,
            NightShiftTotal = nightTotal,
            NightShiftOk = nightOk,
            NightShiftNg = nightNg
        };
        return true;
    }

    private static bool TryReadRequiredInt(JsonElement root, string key, out int value)
    {
        value = 0;
        return root.ValueKind == JsonValueKind.Object
               && root.TryGetProperty(key, out var property)
               && property.ValueKind == JsonValueKind.Number
               && property.TryGetInt32(out value);
    }

    private static bool TryReadRequiredString(JsonElement root, string key, out string value)
    {
        value = string.Empty;
        if (root.ValueKind != JsonValueKind.Object
            || !root.TryGetProperty(key, out var property)
            || property.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        value = property.GetString() ?? string.Empty;
        return !string.IsNullOrWhiteSpace(value);
    }
}
