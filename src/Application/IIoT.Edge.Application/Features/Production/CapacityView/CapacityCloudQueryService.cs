using System.Globalization;
using System.Text.Json;
using IIoT.Edge.Application.Abstractions.Cloud;
using IIoT.Edge.Application.Abstractions.Logging;
using IIoT.Edge.SharedKernel.DataPipeline.Capacity;

namespace IIoT.Edge.Application.Features.Production.CapacityView;

/// <summary>
/// 产能云端查询服务。
/// 负责封装产能相关的云端 HTTP 调用与响应解析。
/// <c>deviceId</c> 表示云端为设备分配的唯一标识，<c>plcName</c> 用于区分同一上位机下的不同 PLC。
/// </summary>
public class CapacityCloudQueryService
{
    private const string HourlyScene = "hourly";
    private const string SummaryScene = "summary";
    private const string SummaryRangeScene = "summary_range";

    private readonly ICloudHttpClient _cloudHttpClient;
    private readonly ICloudApiPathProvider _apiPathProvider;
    private readonly ShiftConfig _shiftConfig;
    private readonly ILogService _logger;

    public CapacityCloudQueryService(
        ICloudHttpClient cloudHttpClient,
        ICloudApiPathProvider apiPathProvider,
        ShiftConfig shiftConfig,
        ILogService logger)
    {
        _cloudHttpClient = cloudHttpClient;
        _apiPathProvider = apiPathProvider;
        _shiftConfig = shiftConfig;
        _logger = logger;
    }

    /// <summary>
    /// 按生产日查询：优先使用分时明细；只有分时接口返回合法空集时才查询汇总。
    /// </summary>
    public async Task<CapacityQueryResult<IReadOnlyList<DailyCapacitySnapshot>>> QueryByProductionDayAsync(
        Guid deviceId,
        DateTime productionDate,
        string plcName,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var nextDay = productionDate.AddDays(1);

        var hourlyToday = await QueryHourlyAsync(deviceId, productionDate, plcName, cancellationToken);
        if (hourlyToday.State is CapacityQueryState.Unavailable or CapacityQueryState.InvalidPayload)
        {
            return ForwardFailure<
                IReadOnlyList<HourlyCapacitySlotSnapshot>,
                IReadOnlyList<DailyCapacitySnapshot>>(hourlyToday);
        }

        var hourlyNextDay = await QueryHourlyAsync(deviceId, nextDay, plcName, cancellationToken);
        if (hourlyNextDay.State is CapacityQueryState.Unavailable or CapacityQueryState.InvalidPayload)
        {
            return ForwardFailure<
                IReadOnlyList<HourlyCapacitySlotSnapshot>,
                IReadOnlyList<DailyCapacitySnapshot>>(hourlyNextDay);
        }

        var hourlyTodayRows = hourlyToday.Value ?? [];
        var nightSlots = (hourlyNextDay.Value ?? [])
            .Where(x => x.StartHour < _shiftConfig.DayStartTime.Hours
                        || (x.StartHour == _shiftConfig.DayStartTime.Hours
                            && x.StartMinute < _shiftConfig.DayStartTime.Minutes))
            .ToList();

        var productionDaySlots = hourlyTodayRows
            .Concat(nightSlots)
            .OrderBy(x => x.SlotOrder)
            .ToList();

        if (productionDaySlots.Count > 0)
        {
            var rows = productionDaySlots
                .Select(x => new DailyCapacitySnapshot
                {
                    Date = productionDate.ToString("MM-dd", CultureInfo.InvariantCulture),
                    DateFull = x.TimeLabel,
                    DayOfWeek = x.ShiftCode,
                    Total = x.TotalCount,
                    OkCount = x.OkCount,
                    NgCount = x.NgCount,
                    Yield = x.TotalCount > 0
                        ? $"{x.OkCount * 100.0 / x.TotalCount:F1}%"
                        : "0%"
                })
                .ToList();

            return CapacityQueryResult<IReadOnlyList<DailyCapacitySnapshot>>.Success(rows);
        }

        var summaryToday = await QuerySummaryAsync(deviceId, productionDate, plcName, cancellationToken);
        if (summaryToday.State is CapacityQueryState.Unavailable or CapacityQueryState.InvalidPayload)
        {
            return ForwardFailure<
                DailyCapacitySummarySnapshot,
                IReadOnlyList<DailyCapacitySnapshot>>(summaryToday);
        }

        var summaryNextDay = await QuerySummaryAsync(deviceId, nextDay, plcName, cancellationToken);
        if (summaryNextDay.State is CapacityQueryState.Unavailable or CapacityQueryState.InvalidPayload)
        {
            return ForwardFailure<
                DailyCapacitySummarySnapshot,
                IReadOnlyList<DailyCapacitySnapshot>>(summaryNextDay);
        }

        if (summaryToday.State == CapacityQueryState.Empty
            && summaryNextDay.State == CapacityQueryState.Empty)
        {
            return CapacityQueryResult<IReadOnlyList<DailyCapacitySnapshot>>.Empty();
        }

        var today = summaryToday.Value;
        var tomorrow = summaryNextDay.Value;
        var totalCount = (today?.TotalCount ?? 0) + (tomorrow?.NightShiftTotal ?? 0);
        var okCount = (today?.OkCount ?? 0) + (tomorrow?.NightShiftOk ?? 0);
        var ngCount = (today?.NgCount ?? 0) + (tomorrow?.NightShiftNg ?? 0);

        IReadOnlyList<DailyCapacitySnapshot> summaryRows =
        [
            new()
            {
                Date = productionDate.ToString("MM-dd", CultureInfo.InvariantCulture),
                DateFull = productionDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                DayOfWeek = productionDate.ToString("ddd", CultureInfo.CurrentCulture),
                Total = totalCount,
                OkCount = okCount,
                NgCount = ngCount,
                Yield = totalCount > 0 ? $"{okCount * 100.0 / totalCount:F1}%" : "0%",
                DayShiftTotal = today?.DayShiftTotal ?? 0,
                DayShiftOk = today?.DayShiftOk ?? 0,
                DayShiftNg = today?.DayShiftNg ?? 0,
                NightShiftTotal = (today?.NightShiftTotal ?? 0) + (tomorrow?.NightShiftTotal ?? 0),
                NightShiftOk = (today?.NightShiftOk ?? 0) + (tomorrow?.NightShiftOk ?? 0),
                NightShiftNg = (today?.NightShiftNg ?? 0) + (tomorrow?.NightShiftNg ?? 0)
            }
        ];

        return CapacityQueryResult<IReadOnlyList<DailyCapacitySnapshot>>.Success(summaryRows);
    }

    /// <summary>
    /// 按月查询：返回当月每日汇总。
    /// </summary>
    public async Task<CapacityQueryResult<IReadOnlyList<DailyCapacitySnapshot>>> QueryByMonthAsync(
        Guid deviceId,
        int year,
        int month,
        string plcName,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var startDate = new DateTime(year, month, 1);
        var endDate = startDate.AddMonths(1).AddDays(-1);

        return await QuerySummaryRangeAsync(
            deviceId,
            startDate,
            endDate,
            plcName,
            cancellationToken);
    }

    /// <summary>
    /// 按年查询：按月份聚合全年汇总。
    /// </summary>
    public async Task<CapacityQueryResult<IReadOnlyList<DailyCapacitySnapshot>>> QueryByYearAsync(
        Guid deviceId,
        int year,
        string plcName,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var startDate = new DateTime(year, 1, 1);
        var endDate = new DateTime(year, 12, 31);

        var result = await QuerySummaryRangeAsync(deviceId, startDate, endDate, plcName, cancellationToken);
        if (result.State is CapacityQueryState.Unavailable or CapacityQueryState.InvalidPayload)
        {
            return result;
        }

        var rows = (result.Value ?? [])
            .GroupBy(row => row.DateFull[..7])
            .Select(group =>
            {
                var total = group.Sum(x => x.Total);
                var ok = group.Sum(x => x.OkCount);
                var ng = group.Sum(x => x.NgCount);
                return new DailyCapacitySnapshot
                {
                    Date = group.Key,
                    DateFull = group.Key,
                    DayOfWeek = "--",
                    Total = total,
                    OkCount = ok,
                    NgCount = ng,
                    Yield = total > 0 ? $"{ok * 100.0 / total:F1}%" : "0%"
                };
            })
            .OrderBy(row => row.DateFull)
            .ToList();

        return rows.Count == 0
            ? CapacityQueryResult<IReadOnlyList<DailyCapacitySnapshot>>.Empty()
            : CapacityQueryResult<IReadOnlyList<DailyCapacitySnapshot>>.Success(rows);
    }

    /// <summary>
    /// 返回当前时间所属的生产日。
    /// </summary>
    public DateTime GetProductionDate(DateTime now)
        => now.TimeOfDay < _shiftConfig.DayStartTime
            ? now.Date.AddDays(-1)
            : now.Date;

    private async Task<CapacityQueryResult<IReadOnlyList<HourlyCapacitySlotSnapshot>>> QueryHourlyAsync(
        Guid deviceId,
        DateTime date,
        string plcName,
        CancellationToken cancellationToken)
    {
        if (!TryResolvePath(_apiPathProvider.GetCapacityHourlyPath, HourlyScene, out var path))
        {
            return CapacityQueryResult<IReadOnlyList<HourlyCapacitySlotSnapshot>>.Unavailable(
                CapacityQueryReasonCodes.CapacityPathUnavailable);
        }

        var url = string.IsNullOrEmpty(plcName)
            ? $"{path}?deviceId={deviceId}&date={date:yyyy-MM-dd}"
            : $"{path}?deviceId={deviceId}&date={date:yyyy-MM-dd}&plcName={Uri.EscapeDataString(plcName)}";

        var jsonResult = await GetJsonAsync(url, HourlyScene, cancellationToken);
        if (jsonResult.State is CapacityQueryState.Unavailable or CapacityQueryState.InvalidPayload)
        {
            return ForwardFailure<JsonElement, IReadOnlyList<HourlyCapacitySlotSnapshot>>(jsonResult);
        }

        return ParsePayload(
            HourlyScene,
            jsonResult.Value,
            CapacityCloudPayloadParser.ParseHourly);
    }

    private async Task<CapacityQueryResult<DailyCapacitySummarySnapshot>> QuerySummaryAsync(
        Guid deviceId,
        DateTime date,
        string plcName,
        CancellationToken cancellationToken)
    {
        if (!TryResolvePath(_apiPathProvider.GetCapacitySummaryPath, SummaryScene, out var path))
        {
            return CapacityQueryResult<DailyCapacitySummarySnapshot>.Unavailable(
                CapacityQueryReasonCodes.CapacityPathUnavailable);
        }

        var url = string.IsNullOrEmpty(plcName)
            ? $"{path}?deviceId={deviceId}&date={date:yyyy-MM-dd}"
            : $"{path}?deviceId={deviceId}&date={date:yyyy-MM-dd}&plcName={Uri.EscapeDataString(plcName)}";

        var jsonResult = await GetJsonAsync(url, SummaryScene, cancellationToken);
        if (jsonResult.State is CapacityQueryState.Unavailable or CapacityQueryState.InvalidPayload)
        {
            return ForwardFailure<JsonElement, DailyCapacitySummarySnapshot>(jsonResult);
        }

        return ParsePayload(
            SummaryScene,
            jsonResult.Value,
            CapacityCloudPayloadParser.ParseSummary);
    }

    private async Task<CapacityQueryResult<IReadOnlyList<DailyCapacitySnapshot>>> QuerySummaryRangeAsync(
        Guid deviceId,
        DateTime startDate,
        DateTime endDate,
        string plcName,
        CancellationToken cancellationToken)
    {
        if (!TryResolvePath(
                _apiPathProvider.GetCapacitySummaryRangePath,
                SummaryRangeScene,
                out var path))
        {
            return CapacityQueryResult<IReadOnlyList<DailyCapacitySnapshot>>.Unavailable(
                CapacityQueryReasonCodes.CapacityPathUnavailable);
        }

        var url = string.IsNullOrEmpty(plcName)
            ? $"{path}?deviceId={deviceId}&startDate={startDate:yyyy-MM-dd}&endDate={endDate:yyyy-MM-dd}"
            : $"{path}?deviceId={deviceId}&startDate={startDate:yyyy-MM-dd}&endDate={endDate:yyyy-MM-dd}&plcName={Uri.EscapeDataString(plcName)}";

        var jsonResult = await GetJsonAsync(url, SummaryRangeScene, cancellationToken);
        if (jsonResult.State is CapacityQueryState.Unavailable or CapacityQueryState.InvalidPayload)
        {
            return ForwardFailure<JsonElement, IReadOnlyList<DailyCapacitySnapshot>>(jsonResult);
        }

        return ParsePayload(
            SummaryRangeScene,
            jsonResult.Value,
            CapacityCloudPayloadParser.ParseSummaryRange);
    }

    private async Task<CapacityQueryResult<JsonElement>> GetJsonAsync(
        string url,
        string scene,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        CloudCallResult<string> response;
        try
        {
            response = await _cloudHttpClient.GetAsync(url);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            WriteSafeWarning(scene, CapacityQueryReasonCodes.CloudQueryException, ex.GetType().Name);
            return CapacityQueryResult<JsonElement>.Unavailable(CapacityQueryReasonCodes.CloudQueryException);
        }

        cancellationToken.ThrowIfCancellationRequested();

        if (!response.IsSuccess)
        {
            var reasonCode = GetUnavailableReasonCode(response.Outcome);
            WriteSafeWarning(scene, reasonCode);
            return CapacityQueryResult<JsonElement>.Unavailable(reasonCode);
        }

        if (string.IsNullOrWhiteSpace(response.Payload))
        {
            WriteSafeWarning(scene, CapacityQueryReasonCodes.CloudResponseEmpty);
            return CapacityQueryResult<JsonElement>.Unavailable(CapacityQueryReasonCodes.CloudResponseEmpty);
        }

        try
        {
            using var document = JsonDocument.Parse(response.Payload);
            return CapacityQueryResult<JsonElement>.Success(document.RootElement.Clone());
        }
        catch (JsonException)
        {
            WriteSafeWarning(scene, CapacityQueryReasonCodes.CloudResponseJsonInvalid, nameof(JsonException));
            return CapacityQueryResult<JsonElement>.InvalidPayload(
                CapacityQueryReasonCodes.CloudResponseJsonInvalid);
        }
    }

    private bool TryResolvePath(Func<string> resolve, string scene, out string path)
    {
        try
        {
            path = resolve();
            if (!string.IsNullOrWhiteSpace(path))
            {
                return true;
            }

            WriteSafeWarning(scene, CapacityQueryReasonCodes.CapacityPathUnavailable);
            return false;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            path = string.Empty;
            WriteSafeWarning(
                scene,
                CapacityQueryReasonCodes.CapacityPathUnavailable,
                ex.GetType().Name);
            return false;
        }
    }

    private static string GetUnavailableReasonCode(CloudCallOutcome outcome)
        => outcome switch
        {
            CloudCallOutcome.SkippedUploadNotReady => CapacityQueryReasonCodes.CloudGateNotReady,
            CloudCallOutcome.UnauthorizedAfterRetry => CapacityQueryReasonCodes.CloudUnauthorized,
            CloudCallOutcome.HttpFailure => CapacityQueryReasonCodes.CloudHttpFailure,
            CloudCallOutcome.NetworkFailure => CapacityQueryReasonCodes.CloudNetworkFailure,
            CloudCallOutcome.Exception => CapacityQueryReasonCodes.CloudClientFailure,
            _ => CapacityQueryReasonCodes.CloudUnavailable
        };

    private static CapacityQueryResult<TTarget> ForwardFailure<TSource, TTarget>(
        CapacityQueryResult<TSource> source)
        => source.State switch
        {
            CapacityQueryState.Unavailable =>
                CapacityQueryResult<TTarget>.Unavailable(source.ReasonCode),
            CapacityQueryState.InvalidPayload =>
                CapacityQueryResult<TTarget>.InvalidPayload(source.ReasonCode),
            _ => CapacityQueryResult<TTarget>.InvalidPayload(
                CapacityQueryReasonCodes.CapacityStateInvalid)
        };

    private CapacityQueryResult<T> ParsePayload<T>(
        string scene,
        JsonElement root,
        Func<JsonElement, CapacityQueryResult<T>> parse)
    {
        try
        {
            var result = parse(root);
            if (result.State == CapacityQueryState.InvalidPayload)
            {
                WriteSafeWarning(scene, result.ReasonCode);
            }

            return result;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            WriteSafeWarning(
                scene,
                CapacityQueryReasonCodes.CapacityParserException,
                ex.GetType().Name);
            return CapacityQueryResult<T>.InvalidPayload(
                CapacityQueryReasonCodes.CapacityParserException);
        }
    }

    private void WriteSafeWarning(string scene, string reasonCode, string? exceptionType = null)
    {
        var exceptionSegment = string.IsNullOrWhiteSpace(exceptionType)
            ? string.Empty
            : $"；异常类型={exceptionType}";
        _logger.Warn($"产能查询未完成；场景={scene}；原因码={reasonCode}{exceptionSegment}。");
    }
}
