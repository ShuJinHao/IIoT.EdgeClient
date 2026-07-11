namespace IIoT.Edge.Application.Features.Production.CapacityView;

/// <summary>
/// 产能只读查询状态。
/// </summary>
public enum CapacityQueryState
{
    Success = 0,
    Empty = 1,
    Unavailable = 2,
    InvalidPayload = 3
}

/// <summary>
/// 产能只读查询边界结果。
/// 合法空集、Cloud 不可用和响应契约错误必须保持不同语义。
/// </summary>
public sealed record CapacityQueryResult<T>(
    CapacityQueryState State,
    T? Value,
    string ReasonCode)
{
    public static CapacityQueryResult<T> Success(T value)
        => new(CapacityQueryState.Success, value, CapacityQueryReasonCodes.Success);

    public static CapacityQueryResult<T> Empty()
        => new(CapacityQueryState.Empty, default, CapacityQueryReasonCodes.Empty);

    public static CapacityQueryResult<T> Unavailable(string reasonCode)
        => new(CapacityQueryState.Unavailable, default, reasonCode);

    public static CapacityQueryResult<T> InvalidPayload(string reasonCode)
        => new(CapacityQueryState.InvalidPayload, default, reasonCode);
}

internal static class CapacityQueryReasonCodes
{
    internal const string Success = "success";
    internal const string Empty = "empty";
    internal const string CloudGateNotReady = "cloud_gate_not_ready";
    internal const string DeviceNotIdentified = "device_not_identified";
    internal const string CapacityPathUnavailable = "capacity_path_unavailable";
    internal const string CloudQueryException = "cloud_query_exception";
    internal const string CloudResponseEmpty = "cloud_response_empty";
    internal const string CloudResponseJsonInvalid = "cloud_response_json_invalid";
    internal const string CloudUnauthorized = "cloud_unauthorized";
    internal const string CloudHttpFailure = "cloud_http_failure";
    internal const string CloudNetworkFailure = "cloud_network_failure";
    internal const string CloudClientFailure = "cloud_client_failure";
    internal const string CloudUnavailable = "cloud_unavailable";
    internal const string CapacityHourlyRootInvalid = "capacity_hourly_root_invalid";
    internal const string CapacityHourlyItemInvalid = "capacity_hourly_item_invalid";
    internal const string CapacitySummaryPayloadInvalid = "capacity_summary_payload_invalid";
    internal const string CapacityRangeRootInvalid = "capacity_range_root_invalid";
    internal const string CapacityRangeItemInvalid = "capacity_range_item_invalid";
    internal const string CapacityStateInvalid = "capacity_state_invalid";
    internal const string CapacityParserException = "capacity_parser_exception";
}
