using IIoT.Edge.Module.Stacking.Constants;
using IIoT.Edge.SharedKernel.DataPipeline.CellData;

namespace IIoT.Edge.Module.Stacking.Payload;

/// <summary>
/// 叠片工序的一次采集/过站数据，进入 DataPipeline 后用于 Cloud 上传和本地补偿序列化。
/// </summary>
public class StackingCellData : CellDataBase
{
    /// <summary>
    /// 工序类型固定为叠片模块，用于 DataPipeline 路由和反序列化。
    /// </summary>
    public override string ProcessType => StackingModuleConstants.ProcessType;

    /// <summary>
    /// 叠片电芯条码，当前开发样本由设备名和序号生成。
    /// </summary>
    public string Barcode { get; set; } = string.Empty;

    /// <summary>
    /// 托盘码，用于表达当前叠片数据绑定的托盘。
    /// </summary>
    public string TrayCode { get; set; } = string.Empty;

    /// <summary>
    /// 叠片层数，来自 PLC 层数信号。
    /// </summary>
    public int LayerCount { get; set; }

    /// <summary>
    /// PLC 上报的工序序号，用于判断是否产生新的叠片过站记录。
    /// </summary>
    public int SequenceNo { get; set; }

    /// <summary>
    /// 运行态状态文案，供 UI 和诊断查看当前记录来源。
    /// </summary>
    public string RuntimeStatus { get; set; } = string.Empty;

    /// <summary>
    /// UI、日志和补偿诊断中展示的记录名，优先使用电芯条码。
    /// </summary>
    public override string DisplayLabel => string.IsNullOrWhiteSpace(Barcode) ? ProcessType : Barcode;
}
