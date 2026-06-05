using System.Text.Json;

namespace IIoT.Edge.Module.Homogenization.Integration.Mes;

/// <summary>
/// 匀浆 MES 主批计划查询请求。upperComputerNo 由上位机编码传入，timestamp 使用 MES 要求的业务时间格式。
/// </summary>
public sealed record HomogenizationMainPlanRequest(
    string UpperComputerNo,
    DateTime Timestamp);

/// <summary>
/// 匀浆 MES 主批计划结果。MES 当前返回 orders 二维字段数组，插件保留字段 code/name/val 供后续业务按需解释。
/// </summary>
public sealed record HomogenizationMainPlan(
    IReadOnlyList<IReadOnlyList<HomogenizationMesField>> Orders);

/// <summary>
/// MES 字段项，来自主批计划接口 orders 内的 code/name/val 结构。
/// </summary>
public sealed record HomogenizationMesField(
    string Code,
    string Name,
    string? Value);

/// <summary>
/// 匀浆 MES 追溯批次号生成请求。masterPlanCode 与 operationCode 是 MES 方法入参，不属于 PLC 信号。
/// </summary>
public sealed record HomogenizationTraceBatchRequest(
    string MasterPlanCode,
    string OperationCode);

/// <summary>
/// 匀浆 MES 追溯批次号结果。当前测试接口未返回成功样例，先保留原始 data 以避免猜错结构。
/// </summary>
public sealed record HomogenizationTraceBatchResult(
    string? BatchNumber,
    JsonElement RawData);

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
