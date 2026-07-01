using IIoT.Edge.SharedKernel.DataPipeline.CellData;

namespace IIoT.Edge.Module.DieCutting.Payload;

/// <summary>
/// 模切数据类型占位。首版不走 DataPipeline，但模块基类需要注册一个 CellData 类型。
/// </summary>
public sealed class DieCuttingCellData : CellDataBase
{
    /// <summary>
    /// 工序类型由 AP/CP 插件写入；模切当前不走 DataPipeline 出料补传。
    /// </summary>
    public override string ProcessType => string.IsNullOrWhiteSpace(ModuleProcessType)
        ? "DieCutting"
        : ModuleProcessType;

    /// <summary>
    /// AP/CP 模切插件的实际工序类型。
    /// </summary>
    public string ModuleProcessType { get; set; } = string.Empty;

    /// <summary>
    /// 模切记录展示名，优先显示弹夹号。
    /// </summary>
    public override string DisplayLabel => string.IsNullOrWhiteSpace(ClipNo) ? DeviceName : ClipNo;

    /// <summary>
    /// 弹夹号，占位字段。
    /// </summary>
    public string ClipNo { get; set; } = string.Empty;
}
