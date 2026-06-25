using IIoT.Edge.Application.Features.Production.DataView;

namespace IIoT.Edge.Presentation.VisualTestData;

/// <summary>
/// 生产数据页面视觉验收数据源，只返回展示快照，不写数据库、不触发上传链路。
/// </summary>
public sealed class VisualTestProductionDataQueryFacade(VisualTestDataOptions options) : IProductionDataQueryFacade
{
    public Task<DataViewSnapshot> QueryAsync(
        string selectedDeviceKey,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        const string allFilterKey = "__all__";
        var batchCode = VisualTestScenario.ResolveBatchCode(options);
        var deviceNames = new[] { "P1-AP01", "P1-AP02", "P1-AP03" };
        var records = Enumerable.Range(0, 24)
            .Select(index =>
            {
                var time = DateTime.Today.AddHours(8).AddMinutes(index * 25);
                var total = 68 + index % 6 * 5;
                var ng = index % 7 == 0 ? 2 : index % 4 == 0 ? 1 : 0;
                var ok = total - ng;
                return new ProductionRecordItem(
                    DeviceName: deviceNames[index % deviceNames.Length],
                    Time: time.ToString("HH:mm"),
                    BatchNo: $"{batchCode}-{index + 1:D2}",
                    Total: total,
                    OkCount: ok,
                    NgCount: ng,
                    Yield: $"{ok * 100.0 / total:F1}%");
            })
            .Where(row =>
                string.IsNullOrWhiteSpace(selectedDeviceKey)
                || string.Equals(selectedDeviceKey, allFilterKey, StringComparison.OrdinalIgnoreCase)
                || string.Equals(row.DeviceName, selectedDeviceKey, StringComparison.OrdinalIgnoreCase))
            .ToList();

        return Task.FromResult(new DataViewSnapshot(records));
    }
}
