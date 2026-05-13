namespace IIoT.Edge.Application.Features.Production.DataView;

/// <summary>
/// 生产记录列表项。
/// </summary>
public record ProductionRecordItem(
    string Time,
    string BatchNo,
    int Total,
    int OkCount,
    int NgCount,
    string Yield);

/// <summary>
/// 生产数据页面快照。
/// </summary>
public record DataViewSnapshot(
    int TodayTotal,
    int TodayOk,
    int TodayNg,
    string TodayYield,
    List<ProductionRecordItem> Records);

/// <summary>
/// 生产数据页面服务契约。
/// </summary>
public interface IDataViewService
{
    Task<DataViewSnapshot> QueryAsync(DateTime dateFrom, DateTime dateTo, CancellationToken cancellationToken = default);
}

/// <summary>
/// 生产数据页面服务。
/// 当前返回模拟数据，用于界面展示与联调。
/// </summary>
public sealed class DataViewService : IDataViewService
{
    public Task<DataViewSnapshot> QueryAsync(DateTime dateFrom, DateTime dateTo, CancellationToken cancellationToken = default)
    {
        var seed = HashCode.Combine(dateFrom.Date, dateTo.Date);
        var random = new Random(seed);
        var records = new List<ProductionRecordItem>();

        for (int i = 0; i < 30; i++)
        {
            var time = dateFrom.Date.AddHours(8).AddMinutes(i * 15);
            var total = random.Next(30, 60);
            var ng = random.Next(0, 3);
            records.Add(new ProductionRecordItem(
                Time: time.ToString("HH:mm"),
                BatchNo: $"LOT-{dateFrom:yyyyMMdd}-{i + 1:D3}",
                Total: total,
                OkCount: total - ng,
                NgCount: ng,
                Yield: $"{(total - ng) * 100.0 / total:F1}%"));
        }

        var todayTotal = records.Sum(item => item.Total);
        var todayOk = records.Sum(item => item.OkCount);
        var todayNg = records.Sum(item => item.NgCount);
        var todayYield = todayTotal > 0 ? $"{todayOk * 100.0 / todayTotal:F2}%" : "0.00%";

        return Task.FromResult(new DataViewSnapshot(todayTotal, todayOk, todayNg, todayYield, records));
    }
}
