namespace IIoT.Edge.Application.Abstractions.Time;

/// <summary>
/// 生产业务时间配置。Cloud/MES 上传、Excel 导出等对外业务时间必须使用同一个时区。
/// </summary>
public sealed class ProductionTimeOptions
{
    public const string SectionName = "ProductionTime";

    /// <summary>
    /// 业务时区标识。默认使用中国时区，支持 Asia/Shanghai 与 China Standard Time。
    /// </summary>
    public string TimeZoneId { get; set; } = "Asia/Shanghai";

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(TimeZoneId))
        {
            throw new InvalidOperationException("ProductionTime:TimeZoneId 不能为空。");
        }
    }
}
