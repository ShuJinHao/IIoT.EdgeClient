namespace IIoT.Edge.Module.DieCutting.Production;

/// <summary>
/// 模切插件本地生产记录，数据只来自真实 PLC 采样快照。
/// </summary>
public sealed class DieCuttingProductionRecord
{
    public long Id { get; set; }

    public string ModuleId { get; set; } = string.Empty;

    public string DeviceName { get; set; } = string.Empty;

    public string BatchNo { get; set; } = string.Empty;

    public long Quantity { get; set; }

    public DateTime WindowStartAt { get; set; }

    public DateTime WindowCompleteAt { get; set; }

    public decimal PunchingSpeed { get; set; }

    public decimal? PlateLengthMm { get; set; }

    public decimal? PlateWidthMm { get; set; }

    public DateTime CreatedAtUtc { get; set; }
}
