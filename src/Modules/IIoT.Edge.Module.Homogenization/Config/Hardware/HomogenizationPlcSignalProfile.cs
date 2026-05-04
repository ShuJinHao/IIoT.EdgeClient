using IIoT.Edge.Application.Abstractions.Plc.Signals;
using IIoT.Edge.Application.Modules.Hardware;

namespace IIoT.Edge.Module.Homogenization.Config.Hardware;

/// <summary>
/// 匀浆 PLC 信号键。运行任务只能使用该枚举访问 IO，禁止直接写字符串 Label。
/// </summary>
public enum HomogenizationSignal
{
    心跳输入,
    进站触发,
    出料触发,
    配方上传触发,
    设备状态上传触发,
    心跳输出,
    进站应答,
    出料应答,
    配方应答,
    设备状态应答,
    托盘码,
    实时真空度,
    实时温度,
    实时搅拌电流,
    实时搅拌转速,
    实时分散电流,
    实时分散转速,
    配方搅拌转速,
    配方分散转速,
    配方NCM,
    配方SP1,
    配方NMP,
    配方胶液,
    配方CNT,
    配方真空,
    配方时间,
    配方温度,
    配方停机步,
    出料CNT实际值,
    出料CNT目标值,
    出料CNTA罐重量,
    出料CNTB罐重量,
    出料NMP实际值,
    出料NMP目标值,
    出料胶液实际值,
    出料设定搅拌时间,
    出料剩余搅拌时间,
    出料设定分散时间,
    出料剩余分散时间,
    设备状态值
}

/// <summary>
/// 匀浆 PLC 信号 profile，按业务分组提供默认点位模板。
/// </summary>
public sealed class HomogenizationPlcSignalProfile : ModulePlcSignalProfileBase<HomogenizationSignal>
{
    private const string Interaction = "信号交互";
    private const string ContinuousData = "连续读数据";
    private const string SinglePointData = "单点读数据";

    /// <summary>
    /// 匀浆信号 profile 所属模块。
    /// </summary>
    public override string ModuleId => DependencyInjection.ModuleKey;

    protected override IEnumerable<ModuleSignalGroup<HomogenizationSignal>> BuildGroups()
    {
        yield return BuildIoInteraction();
        yield return BuildTrayInfo();
        yield return BuildRealtimeData();
        yield return BuildRecipeData();
        yield return BuildOutboundData();
        yield return BuildEquipmentStatus();
    }

    private ModuleSignalGroup<HomogenizationSignal> BuildIoInteraction()
        => Group(
            "IO 交互",
            Signal(HomogenizationSignal.心跳输入, "Homogenization.HeartbeatIn", "D700", ModuleSignalDirection.Read, 1, "Int16", 1, "心跳输入", Interaction, "心跳交互", "PLC 心跳"),
            Signal(HomogenizationSignal.心跳输出, "Homogenization.HeartbeatOut", "D600", ModuleSignalDirection.Write, 1, "Int16", 101, "心跳输出", Interaction, "心跳交互", "上位机心跳"),
            Signal(HomogenizationSignal.进站触发, "Homogenization.InboundTrigger", "D701", ModuleSignalDirection.Read, 1, "Int16", 2, "进站触发", Interaction, "扫码进站", "PLC 触发"),
            Signal(HomogenizationSignal.进站应答, "Homogenization.InboundAck", "D601", ModuleSignalDirection.Write, 1, "Int16", 102, "进站应答", Interaction, "扫码进站", "上位机应答"),
            Signal(HomogenizationSignal.出料触发, "Homogenization.OutboundTrigger", "D702", ModuleSignalDirection.Read, 1, "Int16", 3, "出料触发", Interaction, "出料上传", "PLC 触发"),
            Signal(HomogenizationSignal.出料应答, "Homogenization.OutboundAck", "D602", ModuleSignalDirection.Write, 1, "Int16", 103, "出料应答", Interaction, "出料上传", "上位机应答"),
            Signal(HomogenizationSignal.配方上传触发, "Homogenization.RecipeTrigger", "D703", ModuleSignalDirection.Read, 1, "Int16", 4, "配方上传触发", Interaction, "工艺参数上传", "PLC 触发"),
            Signal(HomogenizationSignal.配方应答, "Homogenization.RecipeAck", "D603", ModuleSignalDirection.Write, 1, "Int16", 104, "配方应答", Interaction, "工艺参数上传", "上位机应答"),
            Signal(HomogenizationSignal.设备状态上传触发, "Homogenization.EquipmentStatusTrigger", "D707", ModuleSignalDirection.Read, 1, "Int16", 5, "设备状态上传触发", Interaction, "设备状态上传", "PLC 触发"),
            Signal(HomogenizationSignal.设备状态应答, "Homogenization.EquipmentStatusAck", "D607", ModuleSignalDirection.Write, 1, "Int16", 105, "设备状态应答", Interaction, "设备状态上传", "上位机应答"));

    private ModuleSignalGroup<HomogenizationSignal> BuildTrayInfo()
        => Group(
            "托盘信息",
            Signal(HomogenizationSignal.托盘码, "Homogenization.TrayCode", "D24500", ModuleSignalDirection.Read, 30, "Ascii", 7, "托盘码", ContinuousData, "托盘数据", "托盘码"));

    private ModuleSignalGroup<HomogenizationSignal> BuildRealtimeData()
        => Group(
            "实时数据",
            Signal(HomogenizationSignal.实时真空度, "Homogenization.RealtimeVacuum", "D300", ModuleSignalDirection.Read, 1, "Int16", 8, "实时真空度", SinglePointData, "实时数据", "真空度"),
            Signal(HomogenizationSignal.实时温度, "Homogenization.RealtimeTemperature", "D301", ModuleSignalDirection.Read, 1, "Int16", 9, "实时温度", SinglePointData, "实时数据", "温度"),
            Signal(HomogenizationSignal.实时搅拌电流, "Homogenization.RealtimeStirringCurrent", "D1616", ModuleSignalDirection.Read, 1, "Int16", 10, "实时搅拌电流", SinglePointData, "实时数据", "搅拌电流"),
            Signal(HomogenizationSignal.实时搅拌转速, "Homogenization.RealtimeStirringSpeed", "D1618", ModuleSignalDirection.Read, 1, "Int16", 11, "实时搅拌转速", SinglePointData, "实时数据", "搅拌转速"),
            Signal(HomogenizationSignal.实时分散电流, "Homogenization.RealtimeDispersionCurrent", "D1636", ModuleSignalDirection.Read, 1, "Int16", 12, "实时分散电流", SinglePointData, "实时数据", "分散电流"),
            Signal(HomogenizationSignal.实时分散转速, "Homogenization.RealtimeDispersionSpeed", "D1638", ModuleSignalDirection.Read, 1, "Int16", 13, "实时分散转速", SinglePointData, "实时数据", "分散转速"));

    private ModuleSignalGroup<HomogenizationSignal> BuildRecipeData()
        => Group(
            "配方数据",
            Signal(HomogenizationSignal.配方搅拌转速, "Homogenization.Recipe.StirringSpeed", "ZR400", ModuleSignalDirection.Read, 30, "UInt16", 14, "配方搅拌转速", ContinuousData, "配方数组", "搅拌转速"),
            Signal(HomogenizationSignal.配方分散转速, "Homogenization.Recipe.DispersionSpeed", "ZR500", ModuleSignalDirection.Read, 30, "UInt16", 15, "配方分散转速", ContinuousData, "配方数组", "分散转速"),
            Signal(HomogenizationSignal.配方NCM, "Homogenization.Recipe.Ncm", "ZR1000", ModuleSignalDirection.Read, 60, "Float", 16, "配方 NCM", ContinuousData, "配方数组", "NCM"),
            Signal(HomogenizationSignal.配方SP1, "Homogenization.Recipe.Sp1", "ZR1800", ModuleSignalDirection.Read, 60, "Float", 17, "配方 SP1", ContinuousData, "配方数组", "SP1"),
            Signal(HomogenizationSignal.配方NMP, "Homogenization.Recipe.Nmp", "ZR1200", ModuleSignalDirection.Read, 60, "Float", 18, "配方 NMP", ContinuousData, "配方数组", "NMP"),
            Signal(HomogenizationSignal.配方胶液, "Homogenization.Recipe.GlueSolution", "ZR1400", ModuleSignalDirection.Read, 60, "Float", 19, "配方胶液", ContinuousData, "配方数组", "胶液"),
            Signal(HomogenizationSignal.配方CNT, "Homogenization.Recipe.Cnt", "ZR1600", ModuleSignalDirection.Read, 60, "Float", 20, "配方 CNT", ContinuousData, "配方数组", "CNT"),
            Signal(HomogenizationSignal.配方真空, "Homogenization.Recipe.Vacuum", "R300", ModuleSignalDirection.Read, 30, "Bool", 21, "配方真空", ContinuousData, "配方数组", "真空"),
            Signal(HomogenizationSignal.配方时间, "Homogenization.Recipe.Time", "ZR0", ModuleSignalDirection.Read, 30, "UInt16", 22, "配方时间", ContinuousData, "配方数组", "时间"),
            Signal(HomogenizationSignal.配方温度, "Homogenization.Recipe.Temperature", "ZR100", ModuleSignalDirection.Read, 30, "Int16", 23, "配方温度", ContinuousData, "配方数组", "温度"),
            Signal(HomogenizationSignal.配方停机步, "Homogenization.Recipe.StopStep", "ZR200", ModuleSignalDirection.Read, 30, "Bool", 24, "配方停机步", ContinuousData, "配方数组", "停机步"));

    private ModuleSignalGroup<HomogenizationSignal> BuildOutboundData()
        => Group(
            "出料数据",
            Signal(HomogenizationSignal.出料CNT实际值, "Homogenization.Outbound.CntActual", "D3030", ModuleSignalDirection.Read, 1, "UInt16", 25, "出料 CNT 实际值", SinglePointData, "出料数据", "CNT 实际值"),
            Signal(HomogenizationSignal.出料CNT目标值, "Homogenization.Outbound.CntTarget", "D8000", ModuleSignalDirection.Read, 1, "UInt16", 26, "出料 CNT 目标值", SinglePointData, "出料数据", "CNT 目标值"),
            Signal(HomogenizationSignal.出料CNTA罐重量, "Homogenization.Outbound.CntTankAWeight", "D7000", ModuleSignalDirection.Read, 1, "UInt16", 27, "出料 CNT A 罐重量", SinglePointData, "出料数据", "CNT A 罐重量"),
            Signal(HomogenizationSignal.出料CNTB罐重量, "Homogenization.Outbound.CntTankBWeight", "D7002", ModuleSignalDirection.Read, 1, "UInt16", 28, "出料 CNT B 罐重量", SinglePointData, "出料数据", "CNT B 罐重量"),
            Signal(HomogenizationSignal.出料NMP实际值, "Homogenization.Outbound.NmpActual", "D812", ModuleSignalDirection.Read, 1, "UInt16", 29, "出料 NMP 实际值", SinglePointData, "出料数据", "NMP 实际值"),
            Signal(HomogenizationSignal.出料NMP目标值, "Homogenization.Outbound.NmpTarget", "D810", ModuleSignalDirection.Read, 1, "UInt16", 30, "出料 NMP 目标值", SinglePointData, "出料数据", "NMP 目标值"),
            Signal(HomogenizationSignal.出料胶液实际值, "Homogenization.Outbound.GlueActual", "D822", ModuleSignalDirection.Read, 1, "UInt16", 31, "出料胶液实际值", SinglePointData, "出料数据", "胶液实际值"),
            Signal(HomogenizationSignal.出料设定搅拌时间, "Homogenization.Outbound.SetStirringTime", "D2054", ModuleSignalDirection.Read, 1, "UInt16", 32, "出料设定搅拌时间", SinglePointData, "出料数据", "设定搅拌时间"),
            Signal(HomogenizationSignal.出料剩余搅拌时间, "Homogenization.Outbound.RemainingStirringTime", "D2056", ModuleSignalDirection.Read, 1, "UInt16", 33, "出料剩余搅拌时间", SinglePointData, "出料数据", "剩余搅拌时间"),
            Signal(HomogenizationSignal.出料设定分散时间, "Homogenization.Outbound.SetDispersionTime", "D2044", ModuleSignalDirection.Read, 1, "UInt16", 34, "出料设定分散时间", SinglePointData, "出料数据", "设定分散时间"),
            Signal(HomogenizationSignal.出料剩余分散时间, "Homogenization.Outbound.RemainingDispersionTime", "D2046", ModuleSignalDirection.Read, 1, "UInt16", 35, "出料剩余分散时间", SinglePointData, "出料数据", "剩余分散时间"));

    private ModuleSignalGroup<HomogenizationSignal> BuildEquipmentStatus()
        => Group(
            "设备状态/报警",
            Signal(HomogenizationSignal.设备状态值, "Homogenization.EquipmentStatusValue", "D711", ModuleSignalDirection.Read, 1, "Int16", 6, "设备状态值", SinglePointData, "设备状态", "状态值"));
}
