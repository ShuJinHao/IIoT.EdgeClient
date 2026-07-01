using System.Text.Json;

namespace IIoT.Edge.Module.DieCutting.Mes;

/// <summary>
/// 模切 MES 主批计划查询请求。
/// </summary>
public sealed record DieCuttingMainPlanRequest(
    string UpperComputerNo,
    DateTime Timestamp);

/// <summary>
/// 模切 MES 主批计划结果。MES 返回 orders 二维字段数组，插件只负责解析，不猜测未确认业务语义。
/// </summary>
public sealed record DieCuttingMainPlan(
    IReadOnlyList<IReadOnlyList<DieCuttingMesField>> Orders);

/// <summary>
/// MES 字段项，来自主批计划接口 orders 内的 code/name/val 结构。
/// </summary>
public sealed record DieCuttingMesField(
    string Code,
    string Name,
    string? Value);

/// <summary>
/// 模切 MES 追溯批次号生成请求。
/// </summary>
public sealed record DieCuttingTraceBatchRequest(
    string MasterPlanCode,
    string OperationCode);

/// <summary>
/// 模切 MES 追溯批次号结果。
/// </summary>
public sealed record DieCuttingTraceBatchResult(
    string? BatchNumber,
    JsonElement RawData);

/// <summary>
/// 模切 MES 响应解析器，保持和匀浆一致的主批计划/追溯批次号解析口径。
/// </summary>
internal static class DieCuttingMesResponseParser
{
    public static DieCuttingMainPlan ParseMainPlan(JsonElement data)
    {
        var orders = new List<IReadOnlyList<DieCuttingMesField>>();
        if (!data.TryGetProperty("orders", out var ordersElement)
            || ordersElement.ValueKind != JsonValueKind.Array)
        {
            return new DieCuttingMainPlan(orders);
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

        return new DieCuttingMainPlan(orders);
    }

    public static DieCuttingTraceBatchResult ParseTraceBatch(JsonElement data)
    {
        var batchNumber = data.ValueKind switch
        {
            JsonValueKind.String => data.GetString(),
            JsonValueKind.Object => TryGetString(data, "batchNumber")
                ?? TryGetString(data, "traceBatchNumber")
                ?? TryGetString(data, "batchNo"),
            _ => null
        };

        return new DieCuttingTraceBatchResult(batchNumber, data.Clone());
    }

    private static DieCuttingMesField ParseMesField(JsonElement item)
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
