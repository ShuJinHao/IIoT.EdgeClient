using IIoT.Edge.Application.Modules.Hardware;

namespace IIoT.Edge.Module.Homogenization.Config.Hardware;

public static class HomogenizationPlcSignalProfile
{
    private const string Interaction = "信号交互";
    private const string ContinuousData = "连续读数据";
    private const string SinglePointData = "单点读数据";

    public static readonly HomogenizationSignalDefinition HeartbeatIn = Signal("Homogenization.HeartbeatIn", "Read", 1, "Int16", 1, "心跳输入", Interaction, "心跳交互", "PLC 心跳");
    public static readonly HomogenizationSignalDefinition InboundTrigger = Signal("Homogenization.InboundTrigger", "Read", 1, "Int16", 2, "进站触发", Interaction, "扫码进站", "PLC 触发");
    public static readonly HomogenizationSignalDefinition OutboundTrigger = Signal("Homogenization.OutboundTrigger", "Read", 1, "Int16", 3, "出料触发", Interaction, "出料上传", "PLC 触发");
    public static readonly HomogenizationSignalDefinition RecipeTrigger = Signal("Homogenization.RecipeTrigger", "Read", 1, "Int16", 4, "配方上传触发", Interaction, "工艺参数上传", "PLC 触发");
    public static readonly HomogenizationSignalDefinition EquipmentStatusTrigger = Signal("Homogenization.EquipmentStatusTrigger", "Read", 1, "Int16", 5, "设备状态上传触发", Interaction, "设备状态上传", "PLC 触发");
    public static readonly HomogenizationSignalDefinition EquipmentStatusValue = Signal("Homogenization.EquipmentStatusValue", "Read", 1, "Int16", 6, "设备状态值", SinglePointData, "设备状态", "状态值");
    public static readonly HomogenizationSignalDefinition TrayCode = Signal("Homogenization.TrayCode", "Read", 30, "Ascii", 7, "托盘码", ContinuousData, "托盘数据", "托盘码");
    public static readonly HomogenizationSignalDefinition RealtimeVacuum = Signal("Homogenization.RealtimeVacuum", "Read", 1, "Int16", 8, "实时真空度", SinglePointData, "实时数据", "真空度");
    public static readonly HomogenizationSignalDefinition RealtimeTemperature = Signal("Homogenization.RealtimeTemperature", "Read", 1, "Int16", 9, "实时温度", SinglePointData, "实时数据", "温度");
    public static readonly HomogenizationSignalDefinition RealtimeStirringCurrent = Signal("Homogenization.RealtimeStirringCurrent", "Read", 1, "Int16", 10, "实时搅拌电流", SinglePointData, "实时数据", "搅拌电流");
    public static readonly HomogenizationSignalDefinition RealtimeStirringSpeed = Signal("Homogenization.RealtimeStirringSpeed", "Read", 1, "Int16", 11, "实时搅拌转速", SinglePointData, "实时数据", "搅拌转速");
    public static readonly HomogenizationSignalDefinition RealtimeDispersionCurrent = Signal("Homogenization.RealtimeDispersionCurrent", "Read", 1, "Int16", 12, "实时分散电流", SinglePointData, "实时数据", "分散电流");
    public static readonly HomogenizationSignalDefinition RealtimeDispersionSpeed = Signal("Homogenization.RealtimeDispersionSpeed", "Read", 1, "Int16", 13, "实时分散转速", SinglePointData, "实时数据", "分散转速");
    public static readonly HomogenizationSignalDefinition RecipeStirringSpeed = Signal("Homogenization.Recipe.StirringSpeed", "Read", 30, "UInt16", 14, "配方搅拌转速", ContinuousData, "配方数组", "搅拌转速");
    public static readonly HomogenizationSignalDefinition RecipeDispersionSpeed = Signal("Homogenization.Recipe.DispersionSpeed", "Read", 30, "UInt16", 15, "配方分散转速", ContinuousData, "配方数组", "分散转速");
    public static readonly HomogenizationSignalDefinition RecipeNcm = Signal("Homogenization.Recipe.Ncm", "Read", 60, "Float", 16, "配方 NCM", ContinuousData, "配方数组", "NCM");
    public static readonly HomogenizationSignalDefinition RecipeSp1 = Signal("Homogenization.Recipe.Sp1", "Read", 60, "Float", 17, "配方 SP1", ContinuousData, "配方数组", "SP1");
    public static readonly HomogenizationSignalDefinition RecipeNmp = Signal("Homogenization.Recipe.Nmp", "Read", 60, "Float", 18, "配方 NMP", ContinuousData, "配方数组", "NMP");
    public static readonly HomogenizationSignalDefinition RecipeGlueSolution = Signal("Homogenization.Recipe.GlueSolution", "Read", 60, "Float", 19, "配方胶液", ContinuousData, "配方数组", "胶液");
    public static readonly HomogenizationSignalDefinition RecipeCnt = Signal("Homogenization.Recipe.Cnt", "Read", 60, "Float", 20, "配方 CNT", ContinuousData, "配方数组", "CNT");
    public static readonly HomogenizationSignalDefinition RecipeVacuum = Signal("Homogenization.Recipe.Vacuum", "Read", 30, "Bool", 21, "配方真空", ContinuousData, "配方数组", "真空");
    public static readonly HomogenizationSignalDefinition RecipeTime = Signal("Homogenization.Recipe.Time", "Read", 30, "UInt16", 22, "配方时间", ContinuousData, "配方数组", "时间");
    public static readonly HomogenizationSignalDefinition RecipeTemperature = Signal("Homogenization.Recipe.Temperature", "Read", 30, "Int16", 23, "配方温度", ContinuousData, "配方数组", "温度");
    public static readonly HomogenizationSignalDefinition RecipeStopStep = Signal("Homogenization.Recipe.StopStep", "Read", 30, "Bool", 24, "配方停机步", ContinuousData, "配方数组", "停机步");
    public static readonly HomogenizationSignalDefinition OutboundCntActual = Signal("Homogenization.Outbound.CntActual", "Read", 1, "UInt16", 25, "出料 CNT 实际值", SinglePointData, "出料数据", "CNT 实际值");
    public static readonly HomogenizationSignalDefinition OutboundCntTarget = Signal("Homogenization.Outbound.CntTarget", "Read", 1, "UInt16", 26, "出料 CNT 目标值", SinglePointData, "出料数据", "CNT 目标值");
    public static readonly HomogenizationSignalDefinition OutboundCntTankAWeight = Signal("Homogenization.Outbound.CntTankAWeight", "Read", 1, "UInt16", 27, "出料 CNT A 罐重量", SinglePointData, "出料数据", "CNT A 罐重量");
    public static readonly HomogenizationSignalDefinition OutboundCntTankBWeight = Signal("Homogenization.Outbound.CntTankBWeight", "Read", 1, "UInt16", 28, "出料 CNT B 罐重量", SinglePointData, "出料数据", "CNT B 罐重量");
    public static readonly HomogenizationSignalDefinition OutboundNmpActual = Signal("Homogenization.Outbound.NmpActual", "Read", 1, "UInt16", 29, "出料 NMP 实际值", SinglePointData, "出料数据", "NMP 实际值");
    public static readonly HomogenizationSignalDefinition OutboundNmpTarget = Signal("Homogenization.Outbound.NmpTarget", "Read", 1, "UInt16", 30, "出料 NMP 目标值", SinglePointData, "出料数据", "NMP 目标值");
    public static readonly HomogenizationSignalDefinition OutboundGlueActual = Signal("Homogenization.Outbound.GlueActual", "Read", 1, "UInt16", 31, "出料胶液实际值", SinglePointData, "出料数据", "胶液实际值");
    public static readonly HomogenizationSignalDefinition OutboundSetStirringTime = Signal("Homogenization.Outbound.SetStirringTime", "Read", 1, "UInt16", 32, "出料设定搅拌时间", SinglePointData, "出料数据", "设定搅拌时间");
    public static readonly HomogenizationSignalDefinition OutboundRemainingStirringTime = Signal("Homogenization.Outbound.RemainingStirringTime", "Read", 1, "UInt16", 33, "出料剩余搅拌时间", SinglePointData, "出料数据", "剩余搅拌时间");
    public static readonly HomogenizationSignalDefinition OutboundSetDispersionTime = Signal("Homogenization.Outbound.SetDispersionTime", "Read", 1, "UInt16", 34, "出料设定分散时间", SinglePointData, "出料数据", "设定分散时间");
    public static readonly HomogenizationSignalDefinition OutboundRemainingDispersionTime = Signal("Homogenization.Outbound.RemainingDispersionTime", "Read", 1, "UInt16", 35, "出料剩余分散时间", SinglePointData, "出料数据", "剩余分散时间");
    public static readonly HomogenizationSignalDefinition HeartbeatOut = Signal("Homogenization.HeartbeatOut", "Write", 1, "Int16", 101, "心跳输出", Interaction, "心跳交互", "上位机心跳");
    public static readonly HomogenizationSignalDefinition InboundAck = Signal("Homogenization.InboundAck", "Write", 1, "Int16", 102, "进站应答", Interaction, "扫码进站", "上位机应答");
    public static readonly HomogenizationSignalDefinition OutboundAck = Signal("Homogenization.OutboundAck", "Write", 1, "Int16", 103, "出料应答", Interaction, "出料上传", "上位机应答");
    public static readonly HomogenizationSignalDefinition RecipeAck = Signal("Homogenization.RecipeAck", "Write", 1, "Int16", 104, "配方应答", Interaction, "工艺参数上传", "上位机应答");
    public static readonly HomogenizationSignalDefinition EquipmentStatusAck = Signal("Homogenization.EquipmentStatusAck", "Write", 1, "Int16", 105, "设备状态应答", Interaction, "设备状态上传", "上位机应答");

    public static IReadOnlyList<HomogenizationSignalDefinition> Signals { get; } =
    [
        HeartbeatIn,
        InboundTrigger,
        OutboundTrigger,
        RecipeTrigger,
        EquipmentStatusTrigger,
        EquipmentStatusValue,
        TrayCode,
        RealtimeVacuum,
        RealtimeTemperature,
        RealtimeStirringCurrent,
        RealtimeStirringSpeed,
        RealtimeDispersionCurrent,
        RealtimeDispersionSpeed,
        RecipeStirringSpeed,
        RecipeDispersionSpeed,
        RecipeNcm,
        RecipeSp1,
        RecipeNmp,
        RecipeGlueSolution,
        RecipeCnt,
        RecipeVacuum,
        RecipeTime,
        RecipeTemperature,
        RecipeStopStep,
        OutboundCntActual,
        OutboundCntTarget,
        OutboundCntTankAWeight,
        OutboundCntTankBWeight,
        OutboundNmpActual,
        OutboundNmpTarget,
        OutboundGlueActual,
        OutboundSetStirringTime,
        OutboundRemainingStirringTime,
        OutboundSetDispersionTime,
        OutboundRemainingDispersionTime,
        HeartbeatOut,
        InboundAck,
        OutboundAck,
        RecipeAck,
        EquipmentStatusAck
    ];

    public static IReadOnlyList<HomogenizationSignalDefinition> ReadSignals { get; } =
        Signals.Where(static x => string.Equals(x.Direction, "Read", StringComparison.OrdinalIgnoreCase))
            .OrderBy(static x => x.SortOrder)
            .ToArray();

    public static IReadOnlyList<HomogenizationSignalDefinition> WriteSignals { get; } =
        Signals.Where(static x => string.Equals(x.Direction, "Write", StringComparison.OrdinalIgnoreCase))
            .OrderBy(static x => x.SortOrder)
            .ToArray();

    private static HomogenizationSignalDefinition Signal(
        string label,
        string direction,
        int addressCount,
        string dataType,
        int sortOrder,
        string displayName,
        string category,
        string groupName,
        string displayRole)
        => new(label, direction, addressCount, dataType, sortOrder, displayName, category, groupName, displayRole);
}

public sealed record HomogenizationSignalDefinition(
    string Label,
    string Direction,
    int AddressCount,
    string DataType,
    int SortOrder,
    string DisplayName,
    string Category,
    string GroupName,
    string DisplayRole) : IModulePlcSignalDefinition
{
    public string DefaultAddress => string.Empty;
}
