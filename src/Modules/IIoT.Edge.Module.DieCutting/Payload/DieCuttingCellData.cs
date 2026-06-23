using IIoT.Edge.SharedKernel.DataPipeline.CellData;

namespace IIoT.Edge.Module.DieCutting.Payload;

/// <summary>
/// 模切数据类型占位。首版不走 DataPipeline，但模块基类需要注册一个 CellData 类型。
/// </summary>
public sealed class DieCuttingCellData : CellDataBase
{
    /// <summary>
    /// 工序类型固定为模切模块。
    /// </summary>
    public override string ProcessType => DependencyInjection.ModuleKey;

    /// <summary>
    /// 模切记录展示名，优先显示弹夹号。
    /// </summary>
    public override string DisplayLabel => string.IsNullOrWhiteSpace(ClipNo) ? DeviceName : ClipNo;

    /// <summary>
    /// 弹夹号，占位字段。
    /// </summary>
    public string ClipNo { get; set; } = string.Empty;
}
