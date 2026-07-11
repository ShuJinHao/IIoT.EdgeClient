using MediatR;

namespace IIoT.Edge.Application.Features.Production.CapacityView;

public record CapacityViewResult(
    CapacityQueryState State,
    IReadOnlyList<DailyCapacitySnapshot> Rows,
    int PeriodTotal,
    int PeriodOk,
    int PeriodNg,
    string PeriodYield,
    string AvgDaily,
    string ReasonCode)
{
    public static CapacityViewResult Success(
        IReadOnlyList<DailyCapacitySnapshot> rows,
        int periodTotal,
        int periodOk,
        int periodNg,
        string periodYield,
        string avgDaily)
        => new(
            CapacityQueryState.Success,
            rows,
            periodTotal,
            periodOk,
            periodNg,
            periodYield,
            avgDaily,
            CapacityQueryReasonCodes.Success);

    public static CapacityViewResult Empty()
        => new(
            CapacityQueryState.Empty,
            [],
            0,
            0,
            0,
            "0%",
            "0",
            CapacityQueryReasonCodes.Empty);

    public static CapacityViewResult Unavailable(string reasonCode)
        => new(CapacityQueryState.Unavailable, [], 0, 0, 0, "0%", "0", reasonCode);

    public static CapacityViewResult InvalidPayload(string reasonCode)
        => new(CapacityQueryState.InvalidPayload, [], 0, 0, 0, "0%", "0", reasonCode);
}

public record LoadTodayCapacityQuery(
    Guid DeviceId,
    DateTime Now,
    string PlcName) : IRequest<CapacityViewResult>;

public record QueryCapacityHistoryQuery(
    Guid DeviceId,
    string QueryMode,
    DateTime QueryDate,
    string PlcName) : IRequest<CapacityViewResult>;

public static class CapacityQueryModes
{
    public const string Day = "Day";
    public const string Month = "Month";
    public const string Year = "Year";
}

public class LoadTodayCapacityHandler(CapacityCloudQueryService service)
    : IRequestHandler<LoadTodayCapacityQuery, CapacityViewResult>
{
    public async Task<CapacityViewResult> Handle(
        LoadTodayCapacityQuery request,
        CancellationToken cancellationToken)
    {
        var productionDate = service.GetProductionDate(request.Now);
        var rows = await service.QueryByProductionDayAsync(
            request.DeviceId,
            productionDate,
            request.PlcName,
            cancellationToken);

        return CapacityQueryHelper.ToResult(rows, 1);
    }
}

public class QueryCapacityHistoryHandler(CapacityCloudQueryService service)
    : IRequestHandler<QueryCapacityHistoryQuery, CapacityViewResult>
{
    public async Task<CapacityViewResult> Handle(
        QueryCapacityHistoryQuery request,
        CancellationToken cancellationToken)
    {
        var rows = request.QueryMode switch
        {
            CapacityQueryModes.Month => await service.QueryByMonthAsync(
                request.DeviceId,
                request.QueryDate.Year,
                request.QueryDate.Month,
                request.PlcName,
                cancellationToken),

            CapacityQueryModes.Year => await service.QueryByYearAsync(
                request.DeviceId,
                request.QueryDate.Year,
                request.PlcName,
                cancellationToken),

            _ => await service.QueryByProductionDayAsync(
                request.DeviceId,
                request.QueryDate.Date,
                request.PlcName,
                cancellationToken)
        };

        var divisor = request.QueryMode == CapacityQueryModes.Year
            ? 12
            : Math.Max(1, rows.Value?.Count ?? 0);
        return CapacityQueryHelper.ToResult(rows, divisor);
    }
}

internal static class CapacityQueryHelper
{
    internal static CapacityViewResult ToResult(
        CapacityQueryResult<IReadOnlyList<DailyCapacitySnapshot>> queryResult,
        int divisor)
    {
        if (queryResult.State != CapacityQueryState.Success)
        {
            return queryResult.State switch
            {
                CapacityQueryState.Empty => CapacityViewResult.Empty(),
                CapacityQueryState.Unavailable => CapacityViewResult.Unavailable(queryResult.ReasonCode),
                CapacityQueryState.InvalidPayload => CapacityViewResult.InvalidPayload(queryResult.ReasonCode),
                _ => CapacityViewResult.InvalidPayload(
                    CapacityQueryReasonCodes.CapacityStateInvalid)
            };
        }

        var rows = queryResult.Value ?? [];
        if (rows.Count == 0)
        {
            return CapacityViewResult.Empty();
        }

        var total = rows.Sum(item => item.Total);
        var ok = rows.Sum(item => item.OkCount);
        var ng = rows.Sum(item => item.NgCount);
        var yield = total > 0 ? $"{ok * 100.0 / total:F2}%" : "0%";
        var avgDaily = $"{total / Math.Max(1, divisor)}";

        return CapacityViewResult.Success(
            rows,
            total,
            ok,
            ng,
            yield,
            avgDaily);
    }
}
