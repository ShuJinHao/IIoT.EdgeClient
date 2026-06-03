using MediatR;

namespace IIoT.Edge.Application.Features.Production.CapacityView;

public record CapacityViewResult(
    List<DailyCapacitySnapshot> Rows,
    int PeriodTotal,
    int PeriodOk,
    int PeriodNg,
    string PeriodYield,
    string AvgDaily);

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
            request.PlcName);

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
                request.PlcName),

            CapacityQueryModes.Year => await service.QueryByYearAsync(
                request.DeviceId,
                request.QueryDate.Year,
                request.PlcName),

            _ => await service.QueryByProductionDayAsync(
                request.DeviceId,
                request.QueryDate.Date,
                request.PlcName)
        };

        var divisor = request.QueryMode == CapacityQueryModes.Year ? 12 : Math.Max(1, rows.Count);
        return CapacityQueryHelper.ToResult(rows, divisor);
    }
}

internal static class CapacityQueryHelper
{
    internal static CapacityViewResult ToResult(List<DailyCapacitySnapshot> rows, int divisor)
    {
        var total = rows.Sum(item => item.Total);
        var ok = rows.Sum(item => item.OkCount);
        var ng = rows.Sum(item => item.NgCount);
        var yield = total > 0 ? $"{ok * 100.0 / total:F2}%" : "0%";
        var avgDaily = $"{total / Math.Max(1, divisor)}";

        return new CapacityViewResult(rows, total, ok, ng, yield, avgDaily);
    }
}
