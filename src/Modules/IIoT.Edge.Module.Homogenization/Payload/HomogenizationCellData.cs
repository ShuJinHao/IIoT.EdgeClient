using IIoT.Edge.SharedKernel.DataPipeline.CellData;

namespace IIoT.Edge.Module.Homogenization.Payload;

/// <summary>
/// 一次匀浆出料事件的数据消息，进入数据管道并参与后续上传。
/// </summary>
public sealed class HomogenizationCellData : CellDataBase
{
    public override string ProcessType => HomogenizationModuleConstants.ProcessType;

    public override string DisplayLabel => TrayCode;

    public string TrayCode { get; set; } = string.Empty;

    public DateTime? InboundTime { get; set; }

    public string RuntimeStatus { get; set; } = "待处理";

    public HomogenizationRealtimeSnapshot? RealtimeSnapshot { get; set; }

    public HomogenizationRecipeSnapshot? RecipeSnapshot { get; set; }

    public HomogenizationEquipmentStatusSnapshot? EquipmentStatusSnapshot { get; set; }

    public double? CntActualKg { get; set; }

    public double? CntTargetKg { get; set; }

    public double? CntTankAWeightKg { get; set; }

    public double? CntTankBWeightKg { get; set; }

    public double? NmpActualKg { get; set; }

    public double? NmpTargetKg { get; set; }

    public double? GlueActualKg { get; set; }

    public int? SetStirringTimeMinutes { get; set; }

    public int? RemainingStirringTimeMinutes { get; set; }

    public int? SetDispersionTimeMinutes { get; set; }

    public int? RemainingDispersionTimeMinutes { get; set; }

    public string? BatchNumber { get; set; }

    public string? MainBatchPlan { get; set; }
}
