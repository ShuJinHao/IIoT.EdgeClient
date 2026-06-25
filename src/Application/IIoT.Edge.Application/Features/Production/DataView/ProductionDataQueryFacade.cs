namespace IIoT.Edge.Application.Features.Production.DataView;

/// <summary>
/// 生产记录列表项。
/// </summary>
public record ProductionRecordItem(
    string DeviceName,
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
    List<ProductionRecordItem> Records);

/// <summary>
/// 生产数据查询 facade 契约。
/// </summary>
public interface IProductionDataQueryFacade
{
    Task<DataViewSnapshot> QueryAsync(string selectedDeviceKey, CancellationToken cancellationToken = default);
}

/// <summary>
/// 生产数据查询 facade。
/// 正式运行路径只允许返回真实采集链路或真实本地缓存记录。
/// </summary>
public sealed class ProductionDataQueryFacade : IProductionDataQueryFacade
{
    public Task<DataViewSnapshot> QueryAsync(string selectedDeviceKey, CancellationToken cancellationToken = default)
    {
        _ = selectedDeviceKey;
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(new DataViewSnapshot([]));
    }
}
