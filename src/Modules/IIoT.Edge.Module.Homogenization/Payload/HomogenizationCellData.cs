using IIoT.Edge.SharedKernel.DataPipeline.CellData;

namespace IIoT.Edge.Module.Homogenization.Payload;

/// <summary>
/// 一次匀浆出料事件的数据消息，进入数据管道并参与后续上传。
/// </summary>
public sealed class HomogenizationCellData : CellDataBase
{
    /// <summary>
    /// 工序类型固定为匀浆模块，用于 DataPipeline 路由和补偿反序列化。
    /// </summary>
    public override string ProcessType => DependencyInjection.ModuleKey;

    /// <summary>
    /// UI、日志和补偿诊断中展示的记录名，匀浆以托盘码作为主标识。
    /// </summary>
    public override string DisplayLabel => TrayCode;

    /// <summary>
    /// 托盘码，来自 PLC 扫码地址，是 MES 进站和出料上传的主业务标识。
    /// </summary>
    public string TrayCode { get; set; } = string.Empty;

    /// <summary>
    /// 最近一次进站校验时间，用于出料记录追溯。
    /// </summary>
    public DateTime? InboundTime { get; set; }

    /// <summary>
    /// 匀浆运行态状态文案，用于 UI 展示当前记录处理状态。
    /// </summary>
    public string RuntimeStatus { get; set; } = "待处理";

    /// <summary>
    /// 出料时关联的实时数据快照，来自 PLC 单点实时信号。
    /// </summary>
    public HomogenizationRealtimeSnapshot? RealtimeSnapshot { get; set; }

    /// <summary>
    /// 出料时关联的配方快照，来自最近一次配方上传任务采集。
    /// </summary>
    public HomogenizationRecipeSnapshot? RecipeSnapshot { get; set; }

    /// <summary>
    /// 出料时关联的设备状态快照，来自最近一次设备状态上传任务或出料时补采。
    /// </summary>
    public HomogenizationEquipmentStatusSnapshot? EquipmentStatusSnapshot { get; set; }

    /// <summary>
    /// CNT 实际投料重量，单位 kg，来自出料单点信号。
    /// </summary>
    public double? CntActualKg { get; set; }

    /// <summary>
    /// CNT 目标重量，单位 kg，来自出料单点信号。
    /// </summary>
    public double? CntTargetKg { get; set; }

    /// <summary>
    /// CNT A 罐重量，单位 kg。
    /// </summary>
    public double? CntTankAWeightKg { get; set; }

    /// <summary>
    /// CNT B 罐重量，单位 kg。
    /// </summary>
    public double? CntTankBWeightKg { get; set; }

    /// <summary>
    /// NMP 实际投料重量，单位 kg。
    /// </summary>
    public double? NmpActualKg { get; set; }

    /// <summary>
    /// NMP 目标重量，单位 kg。
    /// </summary>
    public double? NmpTargetKg { get; set; }

    /// <summary>
    /// 胶液实际重量，单位 kg。
    /// </summary>
    public double? GlueActualKg { get; set; }

    /// <summary>
    /// 设定搅拌时间，单位分钟。
    /// </summary>
    public int? SetStirringTimeMinutes { get; set; }

    /// <summary>
    /// 剩余搅拌时间，单位分钟。
    /// </summary>
    public int? RemainingStirringTimeMinutes { get; set; }

    /// <summary>
    /// 设定分散时间，单位分钟。
    /// </summary>
    public int? SetDispersionTimeMinutes { get; set; }

    /// <summary>
    /// 剩余分散时间，单位分钟。
    /// </summary>
    public int? RemainingDispersionTimeMinutes { get; set; }

    /// <summary>
    /// 预留批次号字段，后续与现场批次规则确认后再写入。
    /// </summary>
    public string? BatchNumber { get; set; }

    /// <summary>
    /// 预留主批次计划字段，后续与现场计划规则确认后再写入。
    /// </summary>
    public string? MainBatchPlan { get; set; }
}
