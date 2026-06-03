using IIoT.Edge.Application.Features.Production.DataView;

namespace IIoT.Edge.Presentation.VisualTestData;

/// <summary>
/// 生产数据页面视觉验收数据源，只返回展示快照，不写数据库、不触发上传链路。
/// </summary>
public sealed class VisualTestProductionDataQueryFacade(VisualTestDataOptions options) : IProductionDataQueryFacade
{
    public Task<DataViewSnapshot> QueryAsync(
        DateTime dateFrom,
        DateTime dateTo,
        CancellationToken cancellationToken = default)
    {
        var records = Enumerable.Range(0, 24)
            .Select(index =>
            {
                var time = dateFrom.Date.AddHours(8).AddMinutes(index * 25);
                var total = 52 + index % 6 * 4;
                var ng = index % 7 == 0 ? 1 : 0;
                var ok = total - ng;
                return new ProductionRecordItem(
                    Time: time.ToString("HH:mm"),
                    BatchNo: $"{options.BatchCode}-{index + 1:D2}",
                    Total: total,
                    OkCount: ok,
                    NgCount: ng,
                    Yield: $"{ok * 100.0 / total:F1}%");
            })
            .ToList();

        var todayTotal = records.Sum(static row => row.Total);
        var todayOk = records.Sum(static row => row.OkCount);
        var todayNg = records.Sum(static row => row.NgCount);
        var todayYield = todayTotal > 0 ? $"{todayOk * 100.0 / todayTotal:F2}%" : "0.00%";

        return Task.FromResult(new DataViewSnapshot(todayTotal, todayOk, todayNg, todayYield, records));
    }
}
