using IIoT.Edge.Application.Abstractions.Plc.Signals;
using IIoT.Edge.Application.Modules.Hardware;

namespace IIoT.Edge.Module.Homogenization.Config.Hardware;

/// <summary>
/// 匀浆 PLC 点位枚举集中容器。枚举只表达插件业务信号含义，实际地址和长度由本文件内 profile 与本地 IO 映射共同决定。
/// </summary>
public static class HomogenizationPlcSignals
{
    /// <summary>
    /// 信号交互点位。该类点位由实时交互线程循环读写，PLC→PC 触发点与 PC→PLC 应答点必须按业务组成套维护。
    /// </summary>
    public enum Interaction
    {
        /// <summary>PLC 写入的心跳输入，方向为 PLC→PC。</summary>
        心跳输入,

        /// <summary>上位机写回 PLC 的心跳输出，方向为 PC→PLC。</summary>
        心跳输出,

        /// <summary>PLC 触发进站校验，方向为 PLC→PC。</summary>
        进站触发,

        /// <summary>上位机写回进站校验结果，方向为 PC→PLC。</summary>
        进站应答,

        /// <summary>PLC 触发出料上传，方向为 PLC→PC。</summary>
        出料触发,

        /// <summary>上位机写回出料上传结果，方向为 PC→PLC。</summary>
        出料应答,

        /// <summary>PLC 触发配方参数上传，方向为 PLC→PC。</summary>
        配方上传触发,

        /// <summary>上位机写回配方参数上传结果，方向为 PC→PLC。</summary>
        配方应答,

        /// <summary>PLC 触发设备状态上传，方向为 PLC→PC。</summary>
        设备状态上传触发,

        /// <summary>上位机写回设备状态上传结果，方向为 PC→PLC。</summary>
        设备状态应答
    }

    /// <summary>
    /// 单点读数据点位。该类点位不进入信号交互线程，业务任务按需要读取当前快照。
    /// </summary>
    public enum SingleRead
    {
        /// <summary>实时真空度，单个 PLC word。</summary>
        实时真空度,

        /// <summary>实时温度，单个 PLC word。</summary>
        实时温度,

        /// <summary>实时搅拌电流，单个 PLC word。</summary>
        实时搅拌电流,

        /// <summary>实时搅拌转速，单个 PLC word。</summary>
        实时搅拌转速,

        /// <summary>实时分散电流，单个 PLC word。</summary>
        实时分散电流,

        /// <summary>实时分散转速，单个 PLC word。</summary>
        实时分散转速,

        /// <summary>出料 CNT 实际值，单个 PLC word。</summary>
        出料CNT实际值,

        /// <summary>出料 CNT 目标值，单个 PLC word。</summary>
        出料CNT目标值,

        /// <summary>出料 CNT A 罐重量，单个 PLC word。</summary>
        出料CNTA罐重量,

        /// <summary>出料 CNT B 罐重量，单个 PLC word。</summary>
        出料CNTB罐重量,

        /// <summary>出料 NMP 实际值，单个 PLC word。</summary>
        出料NMP实际值,

        /// <summary>出料 NMP 目标值，单个 PLC word。</summary>
        出料NMP目标值,

        /// <summary>出料胶液实际值，单个 PLC word。</summary>
        出料胶液实际值,

        /// <summary>出料设定搅拌时间，单个 PLC word。</summary>
        出料设定搅拌时间,

        /// <summary>出料剩余搅拌时间，单个 PLC word。</summary>
        出料剩余搅拌时间,

        /// <summary>出料设定分散时间，单个 PLC word。</summary>
        出料设定分散时间,

        /// <summary>出料剩余分散时间，单个 PLC word。</summary>
        出料剩余分散时间,

        /// <summary>设备状态值，单个 PLC word。</summary>
        设备状态值
    }

    /// <summary>
    /// 连续读数据点位。该类点位由业务任务按场景读取数组或字符串，读取长度属于插件业务定义。
    /// </summary>
    public enum ContinuousRead
    {
        /// <summary>托盘码，连续 ASCII 字符区。</summary>
        托盘码,

        /// <summary>配方搅拌转速，连续数组。</summary>
        配方搅拌转速,

        /// <summary>配方分散转速，连续数组。</summary>
        配方分散转速,

        /// <summary>配方 NCM，连续浮点数组。</summary>
        配方NCM,

        /// <summary>配方 SP1，连续浮点数组。</summary>
        配方SP1,

        /// <summary>配方 NMP，连续浮点数组。</summary>
        配方NMP,

        /// <summary>配方胶液，连续浮点数组。</summary>
        配方胶液,

        /// <summary>配方 CNT，连续浮点数组。</summary>
        配方CNT,

        /// <summary>配方真空，连续布尔数组。</summary>
        配方真空,

        /// <summary>配方时间，连续数组。</summary>
        配方时间,

        /// <summary>配方温度，连续数组。</summary>
        配方温度,

        /// <summary>配方停机步，连续布尔数组。</summary>
        配方停机步
    }
}

/// <summary>
/// 匀浆信号交互 profile，只声明需要实时循环读写的 PLC→PC 触发点和 PC→PLC 应答点。
/// </summary>
public sealed class HomogenizationInteractionSignalProfile
    : ModulePlcSignalProfileBase<HomogenizationPlcSignals.Interaction>
{
    private const string Interaction = "信号交互";

    /// <summary>
    /// 匀浆信号交互 profile 所属模块。
    /// </summary>
    public override string ModuleId => DependencyInjection.ModuleKey;

    protected override IEnumerable<ModuleSignalGroup<HomogenizationPlcSignals.Interaction>> BuildGroups()
    {
        yield return Group(
            "IO 交互",
            Signal(HomogenizationPlcSignals.Interaction.心跳输入, "Homogenization.HeartbeatIn", "D700", ModuleSignalDirection.Read, 1, "Int16", 1, "心跳输入", Interaction, "心跳交互", "PLC 心跳"),
            Signal(HomogenizationPlcSignals.Interaction.心跳输出, "Homogenization.HeartbeatOut", "D600", ModuleSignalDirection.Write, 1, "Int16", 101, "心跳输出", Interaction, "心跳交互", "上位机心跳"),
            Signal(HomogenizationPlcSignals.Interaction.进站触发, "Homogenization.InboundTrigger", "D701", ModuleSignalDirection.Read, 1, "Int16", 2, "进站触发", Interaction, "扫码进站", "PLC 触发"),
            Signal(HomogenizationPlcSignals.Interaction.进站应答, "Homogenization.InboundAck", "D601", ModuleSignalDirection.Write, 1, "Int16", 102, "进站应答", Interaction, "扫码进站", "上位机应答"),
            Signal(HomogenizationPlcSignals.Interaction.出料触发, "Homogenization.OutboundTrigger", "D702", ModuleSignalDirection.Read, 1, "Int16", 3, "出料触发", Interaction, "出料上传", "PLC 触发"),
            Signal(HomogenizationPlcSignals.Interaction.出料应答, "Homogenization.OutboundAck", "D602", ModuleSignalDirection.Write, 1, "Int16", 103, "出料应答", Interaction, "出料上传", "上位机应答"),
            Signal(HomogenizationPlcSignals.Interaction.配方上传触发, "Homogenization.RecipeTrigger", "D703", ModuleSignalDirection.Read, 1, "Int16", 4, "配方上传触发", Interaction, "工艺参数上传", "PLC 触发"),
            Signal(HomogenizationPlcSignals.Interaction.配方应答, "Homogenization.RecipeAck", "D603", ModuleSignalDirection.Write, 1, "Int16", 104, "配方应答", Interaction, "工艺参数上传", "上位机应答"),
            Signal(HomogenizationPlcSignals.Interaction.设备状态上传触发, "Homogenization.EquipmentStatusTrigger", "D707", ModuleSignalDirection.Read, 1, "Int16", 5, "设备状态上传触发", Interaction, "设备状态上传", "PLC 触发"),
            Signal(HomogenizationPlcSignals.Interaction.设备状态应答, "Homogenization.EquipmentStatusAck", "D607", ModuleSignalDirection.Write, 1, "Int16", 105, "设备状态应答", Interaction, "设备状态上传", "上位机应答"));
    }
}

/// <summary>
/// 匀浆单点读数据 profile，只声明业务任务按需读取的单 word 数据点。
/// </summary>
public sealed class HomogenizationSingleReadSignalProfile
    : ModulePlcSignalProfileBase<HomogenizationPlcSignals.SingleRead>
{
    private const string SinglePointData = "单点读数据";

    /// <summary>
    /// 匀浆单点读数据 profile 所属模块。
    /// </summary>
    public override string ModuleId => DependencyInjection.ModuleKey;

    protected override IEnumerable<ModuleSignalGroup<HomogenizationPlcSignals.SingleRead>> BuildGroups()
    {
        yield return BuildRealtimeData();
        yield return BuildOutboundData();
        yield return BuildEquipmentStatus();
    }

    private ModuleSignalGroup<HomogenizationPlcSignals.SingleRead> BuildRealtimeData()
        => Group(
            "实时数据",
            Signal(HomogenizationPlcSignals.SingleRead.实时真空度, "Homogenization.RealtimeVacuum", "D300", ModuleSignalDirection.Read, 1, "Int16", 8, "实时真空度", SinglePointData, "实时数据", "真空度"),
            Signal(HomogenizationPlcSignals.SingleRead.实时温度, "Homogenization.RealtimeTemperature", "D301", ModuleSignalDirection.Read, 1, "Int16", 9, "实时温度", SinglePointData, "实时数据", "温度"),
            Signal(HomogenizationPlcSignals.SingleRead.实时搅拌电流, "Homogenization.RealtimeStirringCurrent", "D1616", ModuleSignalDirection.Read, 1, "Int16", 10, "实时搅拌电流", SinglePointData, "实时数据", "搅拌电流"),
            Signal(HomogenizationPlcSignals.SingleRead.实时搅拌转速, "Homogenization.RealtimeStirringSpeed", "D1618", ModuleSignalDirection.Read, 1, "Int16", 11, "实时搅拌转速", SinglePointData, "实时数据", "搅拌转速"),
            Signal(HomogenizationPlcSignals.SingleRead.实时分散电流, "Homogenization.RealtimeDispersionCurrent", "D1636", ModuleSignalDirection.Read, 1, "Int16", 12, "实时分散电流", SinglePointData, "实时数据", "分散电流"),
            Signal(HomogenizationPlcSignals.SingleRead.实时分散转速, "Homogenization.RealtimeDispersionSpeed", "D1638", ModuleSignalDirection.Read, 1, "Int16", 13, "实时分散转速", SinglePointData, "实时数据", "分散转速"));

    private ModuleSignalGroup<HomogenizationPlcSignals.SingleRead> BuildOutboundData()
        => Group(
            "出料数据",
            Signal(HomogenizationPlcSignals.SingleRead.出料CNT实际值, "Homogenization.Outbound.CntActual", "D3030", ModuleSignalDirection.Read, 1, "UInt16", 25, "出料 CNT 实际值", SinglePointData, "出料数据", "CNT 实际值"),
            Signal(HomogenizationPlcSignals.SingleRead.出料CNT目标值, "Homogenization.Outbound.CntTarget", "D8000", ModuleSignalDirection.Read, 1, "UInt16", 26, "出料 CNT 目标值", SinglePointData, "出料数据", "CNT 目标值"),
            Signal(HomogenizationPlcSignals.SingleRead.出料CNTA罐重量, "Homogenization.Outbound.CntTankAWeight", "D7000", ModuleSignalDirection.Read, 1, "UInt16", 27, "出料 CNT A 罐重量", SinglePointData, "出料数据", "CNT A 罐重量"),
            Signal(HomogenizationPlcSignals.SingleRead.出料CNTB罐重量, "Homogenization.Outbound.CntTankBWeight", "D7002", ModuleSignalDirection.Read, 1, "UInt16", 28, "出料 CNT B 罐重量", SinglePointData, "出料数据", "CNT B 罐重量"),
            Signal(HomogenizationPlcSignals.SingleRead.出料NMP实际值, "Homogenization.Outbound.NmpActual", "D812", ModuleSignalDirection.Read, 1, "UInt16", 29, "出料 NMP 实际值", SinglePointData, "出料数据", "NMP 实际值"),
            Signal(HomogenizationPlcSignals.SingleRead.出料NMP目标值, "Homogenization.Outbound.NmpTarget", "D810", ModuleSignalDirection.Read, 1, "UInt16", 30, "出料 NMP 目标值", SinglePointData, "出料数据", "NMP 目标值"),
            Signal(HomogenizationPlcSignals.SingleRead.出料胶液实际值, "Homogenization.Outbound.GlueActual", "D822", ModuleSignalDirection.Read, 1, "UInt16", 31, "出料胶液实际值", SinglePointData, "出料数据", "胶液实际值"),
            Signal(HomogenizationPlcSignals.SingleRead.出料设定搅拌时间, "Homogenization.Outbound.SetStirringTime", "D2054", ModuleSignalDirection.Read, 1, "UInt16", 32, "出料设定搅拌时间", SinglePointData, "出料数据", "设定搅拌时间"),
            Signal(HomogenizationPlcSignals.SingleRead.出料剩余搅拌时间, "Homogenization.Outbound.RemainingStirringTime", "D2056", ModuleSignalDirection.Read, 1, "UInt16", 33, "出料剩余搅拌时间", SinglePointData, "出料数据", "剩余搅拌时间"),
            Signal(HomogenizationPlcSignals.SingleRead.出料设定分散时间, "Homogenization.Outbound.SetDispersionTime", "D2044", ModuleSignalDirection.Read, 1, "UInt16", 34, "出料设定分散时间", SinglePointData, "出料数据", "设定分散时间"),
            Signal(HomogenizationPlcSignals.SingleRead.出料剩余分散时间, "Homogenization.Outbound.RemainingDispersionTime", "D2046", ModuleSignalDirection.Read, 1, "UInt16", 35, "出料剩余分散时间", SinglePointData, "出料数据", "剩余分散时间"));

    private ModuleSignalGroup<HomogenizationPlcSignals.SingleRead> BuildEquipmentStatus()
        => Group(
            "设备状态/报警",
            Signal(HomogenizationPlcSignals.SingleRead.设备状态值, "Homogenization.EquipmentStatusValue", "D711", ModuleSignalDirection.Read, 1, "Int16", 6, "设备状态值", SinglePointData, "设备状态", "状态值"));
}

/// <summary>
/// 匀浆连续读数据 profile，只声明数组和字符串类读取点位；这些点位不参与实时信号交互循环。
/// </summary>
public sealed class HomogenizationContinuousReadSignalProfile
    : ModulePlcSignalProfileBase<HomogenizationPlcSignals.ContinuousRead>
{
    private const string ContinuousData = "连续读数据";

    /// <summary>
    /// 匀浆连续读数据 profile 所属模块。
    /// </summary>
    public override string ModuleId => DependencyInjection.ModuleKey;

    protected override IEnumerable<ModuleSignalGroup<HomogenizationPlcSignals.ContinuousRead>> BuildGroups()
    {
        yield return BuildTrayInfo();
        yield return BuildRecipeData();
    }

    private ModuleSignalGroup<HomogenizationPlcSignals.ContinuousRead> BuildTrayInfo()
        => Group(
            "托盘信息",
            Signal(HomogenizationPlcSignals.ContinuousRead.托盘码, "Homogenization.TrayCode", "D24500", ModuleSignalDirection.Read, 30, "Ascii", 7, "托盘码", ContinuousData, "托盘数据", "托盘码"));

    private ModuleSignalGroup<HomogenizationPlcSignals.ContinuousRead> BuildRecipeData()
        => Group(
            "配方数据",
            Signal(HomogenizationPlcSignals.ContinuousRead.配方搅拌转速, "Homogenization.Recipe.StirringSpeed", "ZR400", ModuleSignalDirection.Read, 30, "UInt16", 14, "配方搅拌转速", ContinuousData, "配方数组", "搅拌转速"),
            Signal(HomogenizationPlcSignals.ContinuousRead.配方分散转速, "Homogenization.Recipe.DispersionSpeed", "ZR500", ModuleSignalDirection.Read, 30, "UInt16", 15, "配方分散转速", ContinuousData, "配方数组", "分散转速"),
            Signal(HomogenizationPlcSignals.ContinuousRead.配方NCM, "Homogenization.Recipe.Ncm", "ZR1000", ModuleSignalDirection.Read, 60, "Float", 16, "配方 NCM", ContinuousData, "配方数组", "NCM"),
            Signal(HomogenizationPlcSignals.ContinuousRead.配方SP1, "Homogenization.Recipe.Sp1", "ZR1800", ModuleSignalDirection.Read, 60, "Float", 17, "配方 SP1", ContinuousData, "配方数组", "SP1"),
            Signal(HomogenizationPlcSignals.ContinuousRead.配方NMP, "Homogenization.Recipe.Nmp", "ZR1200", ModuleSignalDirection.Read, 60, "Float", 18, "配方 NMP", ContinuousData, "配方数组", "NMP"),
            Signal(HomogenizationPlcSignals.ContinuousRead.配方胶液, "Homogenization.Recipe.GlueSolution", "ZR1400", ModuleSignalDirection.Read, 60, "Float", 19, "配方胶液", ContinuousData, "配方数组", "胶液"),
            Signal(HomogenizationPlcSignals.ContinuousRead.配方CNT, "Homogenization.Recipe.Cnt", "ZR1600", ModuleSignalDirection.Read, 60, "Float", 20, "配方 CNT", ContinuousData, "配方数组", "CNT"),
            Signal(HomogenizationPlcSignals.ContinuousRead.配方真空, "Homogenization.Recipe.Vacuum", "R300", ModuleSignalDirection.Read, 30, "Bool", 21, "配方真空", ContinuousData, "配方数组", "真空"),
            Signal(HomogenizationPlcSignals.ContinuousRead.配方时间, "Homogenization.Recipe.Time", "ZR0", ModuleSignalDirection.Read, 30, "UInt16", 22, "配方时间", ContinuousData, "配方数组", "时间"),
            Signal(HomogenizationPlcSignals.ContinuousRead.配方温度, "Homogenization.Recipe.Temperature", "ZR100", ModuleSignalDirection.Read, 30, "Int16", 23, "配方温度", ContinuousData, "配方数组", "温度"),
            Signal(HomogenizationPlcSignals.ContinuousRead.配方停机步, "Homogenization.Recipe.StopStep", "ZR200", ModuleSignalDirection.Read, 30, "Bool", 24, "配方停机步", ContinuousData, "配方数组", "停机步"));
}
