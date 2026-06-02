using IIoT.Edge.Application.Features.Production.Equipment;

namespace IIoT.Edge.Presentation.VisualTestData;

/// <summary>
/// 右侧设备面板视觉验收数据源，只返回展示快照，不访问真实 PLC、配方或产能链路。
/// </summary>
public sealed class VisualTestEquipmentPanelService(VisualTestDataOptions options) : IEquipmentPanelService
{
    public Task<List<HardwareSnapshot>> GetHardwareStatusAsync(CancellationToken cancellationToken = default)
    {
        var tick = DateTimeOffset.Now.Second;
        var snapshots = new List<HardwareSnapshot>
        {
            new(options.PrimaryDeviceName, "127.0.0.1:6000", "PLC", true),
            new("PLC-Homogenization-02", "127.0.0.1:6001", "PLC", tick % 10 < 8),
            new("扫码枪-HG-01", "COM3", "Serial", true)
        };

        return Task.FromResult(snapshots);
    }

    public Task<RecipeSnapshot?> GetRecipeSnapshotAsync(CancellationToken cancellationToken = default)
    {
        var snapshot = new RecipeSnapshot(
            "匀浆视觉验收配方 (VisualTest)",
            "V2.3",
            "匀浆",
            true,
            [
                new("搅拌转速", "620", "580", "660", "RPM", "590", "650"),
                new("温度", "42.5", "38.0", "46.0", "C", "39.5", "45.0"),
                new("真空度", "-88.2", "-95.0", "-80.0", "KPa", "-93.0", "-82.0"),
                new("分散时间", "45", "40", "50", "min", "41", "49")
            ]);

        return Task.FromResult<RecipeSnapshot?>(snapshot);
    }

    public Task<CapacitySnapshot> GetCapacitySnapshotAsync(CancellationToken cancellationToken = default)
    {
        var minuteOffset = DateTimeOffset.Now.Minute % 12;
        var ok = 12860 + minuteOffset * 8;
        var ng = 12 + minuteOffset % 3;
        var total = ok + ng;
        var yield = $"{ok * 100.0 / total:F1}%";

        return Task.FromResult(new CapacitySnapshot(total, ng, yield, options.BatchCode));
    }
}
