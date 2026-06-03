using IIoT.Edge.Module.Homogenization.Payload;
using System.Text.Json;

namespace IIoT.Edge.Module.Homogenization.Integration;

/// <summary>
/// 匀浆 MES 响应解析器，负责主计划和追溯批次响应的 JSON 映射。
/// </summary>
internal static class HomogenizationMesResponseParser
{
    /// <summary>
    /// 解析主计划响应中的订单字段数组。
    /// </summary>
    public static HomogenizationMainPlan ParseMainPlan(JsonElement data)
    {
        var orders = new List<IReadOnlyList<HomogenizationMesField>>();
        if (!data.TryGetProperty("orders", out var ordersElement)
            || ordersElement.ValueKind != JsonValueKind.Array)
        {
            return new HomogenizationMainPlan(orders);
        }

        foreach (var orderElement in ordersElement.EnumerateArray())
        {
            if (orderElement.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            var fields = orderElement
                .EnumerateArray()
                .Where(static item => item.ValueKind == JsonValueKind.Object)
                .Select(ParseMesField)
                .ToArray();
            orders.Add(fields);
        }

        return new HomogenizationMainPlan(orders);
    }

    /// <summary>
    /// 解析追溯批次号响应，兼容 MES 返回的多个批次字段名。
    /// </summary>
    public static HomogenizationTraceBatchResult ParseTraceBatch(JsonElement data)
    {
        var batchNumber = data.ValueKind switch
        {
            JsonValueKind.String => data.GetString(),
            JsonValueKind.Object => TryGetString(data, "batchNumber")
                ?? TryGetString(data, "traceBatchNumber")
                ?? TryGetString(data, "batchNo"),
            _ => null
        };

        return new HomogenizationTraceBatchResult(batchNumber, data.Clone());
    }

    private static HomogenizationMesField ParseMesField(JsonElement item)
        => new(
            TryGetString(item, "code") ?? string.Empty,
            TryGetString(item, "name") ?? string.Empty,
            TryGetString(item, "val"));

    private static string? TryGetString(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var value)
            || value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return null;
        }

        return value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : value.GetRawText();
    }
}
