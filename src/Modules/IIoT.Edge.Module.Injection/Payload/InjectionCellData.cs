using IIoT.Edge.SharedKernel.DataPipeline.CellData;

namespace IIoT.Edge.Module.Injection.Payload;

/// <summary>
/// 注液工序的一次电芯过站数据，进入 DataPipeline 后用于云端批量上传和本地补偿序列化。
/// </summary>
public class InjectionCellData : CellDataBase
{
    /// <summary>
    /// 工序类型固定为注液模块，用于 DataPipeline 反序列化和 uploader 路由。
    /// </summary>
    public override string ProcessType => DependencyInjection.ModuleKey;

    /// <summary>
    /// 生产工单号，来自注液扫码或现场工单绑定。
    /// </summary>
    public string WorkOrderNo { get; set; } = string.Empty;

    /// <summary>
    /// 电芯条码，作为注液过站数据的主展示标识和云端上传主业务标识。
    /// </summary>
    public string Barcode { get; set; } = string.Empty;

    /// <summary>
    /// 注液前扫码时间；云端前称重时间缺省时会以此时间兜底。
    /// </summary>
    public DateTime? ScanTime { get; set; }

    /// <summary>
    /// 注液前重量，单位由现场称重配置决定，当前按云端注液 DTO 原样上传。
    /// </summary>
    public double PreInjectionWeight { get; set; }

    /// <summary>
    /// 注液后重量，和注液前重量共同计算或校验注液量。
    /// </summary>
    public double PostInjectionWeight { get; set; }

    /// <summary>
    /// 注液量，上传云端用于注液过站追溯。
    /// </summary>
    public double InjectionVolume { get; set; }

    /// <summary>
    /// UI、日志和补偿诊断中展示的记录名，优先使用电芯条码。
    /// </summary>
    public override string DisplayLabel => string.IsNullOrEmpty(Barcode) ? ProcessType : Barcode;
}
