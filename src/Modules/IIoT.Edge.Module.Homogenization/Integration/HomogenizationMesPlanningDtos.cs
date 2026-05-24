using System.Text.Json;

namespace IIoT.Edge.Module.Homogenization.Integration;

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
