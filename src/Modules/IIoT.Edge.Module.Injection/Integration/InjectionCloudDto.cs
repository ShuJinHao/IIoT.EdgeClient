namespace IIoT.Edge.Module.Injection.Integration;

/// <summary>
/// 注液云端批量过站 DTO，字段形态贴合云端注液接口，不作为插件内部长期实体使用。
/// </summary>
public class InjectionCloudDto
{
    /// <summary>
    /// 电芯条码，云端注液过站的业务主键。
    /// </summary>
    public string Barcode { get; set; } = string.Empty;

    /// <summary>
    /// 电芯结果，上传时按云端约定转换为 OK/NG。
    /// </summary>
    public string CellResult { get; set; } = string.Empty;

    /// <summary>
    /// 注液过站完成时间，缺省时由映射层使用当前时间兜底。
    /// </summary>
    public DateTime CompletedTime { get; set; }

    /// <summary>
    /// 注液前称重或扫码时间。
    /// </summary>
    public DateTime PreInjectionTime { get; set; }

    /// <summary>
    /// 注液前重量。
    /// </summary>
    public double PreInjectionWeight { get; set; }

    /// <summary>
    /// 注液后称重时间。
    /// </summary>
    public DateTime PostInjectionTime { get; set; }

    /// <summary>
    /// 注液后重量。
    /// </summary>
    public double PostInjectionWeight { get; set; }

    /// <summary>
    /// 注液量，来自插件电芯数据并原样交给云端接口。
    /// </summary>
    public double InjectionVolume { get; set; }
}
