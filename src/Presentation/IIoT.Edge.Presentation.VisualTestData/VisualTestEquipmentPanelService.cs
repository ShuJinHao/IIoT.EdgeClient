using IIoT.Edge.Application.Features.Production.Equipment;

namespace IIoT.Edge.Presentation.VisualTestData;

/// <summary>
/// 右侧设备面板视觉验收数据源，只返回展示快照，不访问真实 PLC、配方或产能链路。
/// </summary>
public sealed class VisualTestEquipmentPanelService(VisualTestDataOptions options) : IEquipmentPanelService
{
    public Task<List<HardwareSnapshot>> GetHardwareStatusAsync(CancellationToken cancellationToken = default)
    {
        var snapshots = new List<HardwareSnapshot>
        {
            new(options.PrimaryDeviceName, "127.0.0.1:6000", "PLC", true),
            new(VisualTestScenario.SecondaryDeviceName, "127.0.0.1:6001", "PLC", true),
            new("扫码枪-HG-01", "COM3", "Serial", true),
            new("电子秤-HG-01", "COM5", "Serial", true)
        };

        return Task.FromResult(snapshots);
    }

    public Task<RecipeSnapshot?> GetRecipeSnapshotAsync(CancellationToken cancellationToken = default)
    {
        var snapshot = new RecipeSnapshot(
            VisualTestScenario.RecipeName,
            VisualTestScenario.RecipeVersion,
            VisualTestScenario.ProcessName,
            true,
            [
                new("CNT 目标重量", "128.0", "126.0", "130.0", "kg", "126.5", "129.5"),
                new("NMP 目标重量", "88.0", "86.0", "90.0", "kg", "86.5", "89.5"),
                new("搅拌转速", "620", "580", "660", "RPM", "590", "650"),
                new("温度", "42.5", "38.0", "46.0", "C", "39.5", "45.0"),
                new("真空度", "-88.2", "-95.0", "-80.0", "KPa", "-93.0", "-82.0"),
                new("分散时间", "45", "40", "50", "min", "41", "49")
            ]);

        return Task.FromResult<RecipeSnapshot?>(snapshot);
    }

    public Task<CapacitySnapshot> GetCapacitySnapshotAsync(CancellationToken cancellationToken = default)
    {
        var metrics = VisualTestScenario.CreateCapacityMetrics(options, DateTimeOffset.Now);

        return Task.FromResult(new CapacitySnapshot(
            metrics.Total,
            metrics.Ok,
            metrics.Ng,
            metrics.Yield,
            metrics.BatchCode,
            metrics.RecentHourTotal,
            metrics.RecentHourOk,
            metrics.RecentHourNg,
            metrics.RecentHourLabel));
    }
}
