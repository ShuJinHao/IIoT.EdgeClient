namespace IIoT.Edge.Module.Homogenization.Config.Hardware;

/// <summary>
/// 匀浆 PLC 信号枚举容器。这里是业务信号唯一白名单；特性只提供标准播种默认值，不决定是否允许新增。
/// </summary>
public static class HomogenizationPlcSignals
{
    /// <summary>
    /// 匀浆信号交互业务动作枚举。每个成员都是一套完整动作，固定包含 PLC->PC 读点和 PC->PLC 写点。
    /// </summary>
    public enum Interaction
    {
        /// <summary>心跳交互，PLC 写入心跳值，上位机把处理后的心跳值写回 PLC。</summary>
        [HomogenizationInteractionSignal("Homogenization.Interaction.Heartbeat", "心跳", "D700", "D600", 1, 101, ReadSignalName = "PLC 心跳", WriteSignalName = "上位机心跳")]
        心跳,

        /// <summary>扫码进站交互，PLC 触发进站校验，上位机写回校验应答。</summary>
        [HomogenizationInteractionSignal("Homogenization.Interaction.Inbound", "扫码进站", "D701", "D601", 2, 102)]
        扫码进站,

        /// <summary>出料上传交互，PLC 触发出料业务，上位机写回上传处理结果。</summary>
        [HomogenizationInteractionSignal("Homogenization.Interaction.Outbound", "出料上传", "D702", "D602", 3, 103)]
        出料上传,

        /// <summary>工艺参数上传交互，PLC 触发配方/工艺参数读取，上位机写回处理结果。</summary>
        [HomogenizationInteractionSignal("Homogenization.Interaction.Recipe", "工艺参数上传", "D703", "D603", 4, 104)]
        工艺参数上传,

        /// <summary>设备状态上传交互，PLC 触发设备状态读取，上位机写回处理结果。</summary>
        [HomogenizationInteractionSignal("Homogenization.Interaction.EquipmentStatus", "设备状态上传", "D707", "D607", 5, 105)]
        设备状态上传,

        /// <summary>调试验证用交互动作；未声明默认地址，新增时必须手工填写读地址和写地址。</summary>
        test1
    }

    /// <summary>
    /// 匀浆单点读数据枚举。该类点位不进入实时信号交互循环，由业务任务按需要读取当前快照。
    /// </summary>
    public enum SingleRead
    {
        /// <summary>实时真空度，单个 PLC word。</summary>
        [HomogenizationReadSignal("Homogenization.RealtimeVacuum", "D300", 1, "Int16", 8, "单点读数据", "实时数据", "真空度", DisplayName = "实时真空度")]
        实时真空度,

        /// <summary>实时温度，单个 PLC word。</summary>
        [HomogenizationReadSignal("Homogenization.RealtimeTemperature", "D301", 1, "Int16", 9, "单点读数据", "实时数据", "温度", DisplayName = "实时温度")]
        实时温度,

        /// <summary>实时搅拌电流，单个 PLC word。</summary>
        [HomogenizationReadSignal("Homogenization.RealtimeStirringCurrent", "D1616", 1, "Int16", 10, "单点读数据", "实时数据", "搅拌电流", DisplayName = "实时搅拌电流")]
        实时搅拌电流,

        /// <summary>实时搅拌转速，单个 PLC word。</summary>
        [HomogenizationReadSignal("Homogenization.RealtimeStirringSpeed", "D1618", 1, "Int16", 11, "单点读数据", "实时数据", "搅拌转速", DisplayName = "实时搅拌转速")]
        实时搅拌转速,

        /// <summary>实时分散电流，单个 PLC word。</summary>
        [HomogenizationReadSignal("Homogenization.RealtimeDispersionCurrent", "D1636", 1, "Int16", 12, "单点读数据", "实时数据", "分散电流", DisplayName = "实时分散电流")]
        实时分散电流,

        /// <summary>实时分散转速，单个 PLC word。</summary>
        [HomogenizationReadSignal("Homogenization.RealtimeDispersionSpeed", "D1638", 1, "Int16", 13, "单点读数据", "实时数据", "分散转速", DisplayName = "实时分散转速")]
        实时分散转速,

        /// <summary>出料 CNT 实际值，单个 PLC word。</summary>
        [HomogenizationReadSignal("Homogenization.Outbound.CntActual", "D3030", 1, "UInt16", 25, "单点读数据", "出料数据", "CNT 实际值", DisplayName = "出料 CNT 实际值")]
        出料CNT实际值,

        /// <summary>出料 CNT 目标值，单个 PLC word。</summary>
        [HomogenizationReadSignal("Homogenization.Outbound.CntTarget", "D8000", 1, "UInt16", 26, "单点读数据", "出料数据", "CNT 目标值", DisplayName = "出料 CNT 目标值")]
        出料CNT目标值,

        /// <summary>出料 CNT A 罐重量，单个 PLC word。</summary>
        [HomogenizationReadSignal("Homogenization.Outbound.CntTankAWeight", "D7000", 1, "UInt16", 27, "单点读数据", "出料数据", "CNT A 罐重量", DisplayName = "出料 CNT A 罐重量")]
        出料CNTA罐重量,

        /// <summary>出料 CNT B 罐重量，单个 PLC word。</summary>
        [HomogenizationReadSignal("Homogenization.Outbound.CntTankBWeight", "D7002", 1, "UInt16", 28, "单点读数据", "出料数据", "CNT B 罐重量", DisplayName = "出料 CNT B 罐重量")]
        出料CNTB罐重量,

        /// <summary>出料 NMP 实际值，单个 PLC word。</summary>
        [HomogenizationReadSignal("Homogenization.Outbound.NmpActual", "D812", 1, "UInt16", 29, "单点读数据", "出料数据", "NMP 实际值", DisplayName = "出料 NMP 实际值")]
        出料NMP实际值,

        /// <summary>出料 NMP 目标值，单个 PLC word。</summary>
        [HomogenizationReadSignal("Homogenization.Outbound.NmpTarget", "D810", 1, "UInt16", 30, "单点读数据", "出料数据", "NMP 目标值", DisplayName = "出料 NMP 目标值")]
        出料NMP目标值,

        /// <summary>出料胶液实际值，单个 PLC word。</summary>
        [HomogenizationReadSignal("Homogenization.Outbound.GlueActual", "D822", 1, "UInt16", 31, "单点读数据", "出料数据", "胶液实际值", DisplayName = "出料胶液实际值")]
        出料胶液实际值,

        /// <summary>出料设定搅拌时间，单个 PLC word。</summary>
        [HomogenizationReadSignal("Homogenization.Outbound.SetStirringTime", "D2054", 1, "UInt16", 32, "单点读数据", "出料数据", "设定搅拌时间", DisplayName = "出料设定搅拌时间")]
        出料设定搅拌时间,

        /// <summary>出料剩余搅拌时间，单个 PLC word。</summary>
        [HomogenizationReadSignal("Homogenization.Outbound.RemainingStirringTime", "D2056", 1, "UInt16", 33, "单点读数据", "出料数据", "剩余搅拌时间", DisplayName = "出料剩余搅拌时间")]
        出料剩余搅拌时间,

        /// <summary>出料设定分散时间，单个 PLC word。</summary>
        [HomogenizationReadSignal("Homogenization.Outbound.SetDispersionTime", "D2044", 1, "UInt16", 34, "单点读数据", "出料数据", "设定分散时间", DisplayName = "出料设定分散时间")]
        出料设定分散时间,

        /// <summary>出料剩余分散时间，单个 PLC word。</summary>
        [HomogenizationReadSignal("Homogenization.Outbound.RemainingDispersionTime", "D2046", 1, "UInt16", 35, "单点读数据", "出料数据", "剩余分散时间", DisplayName = "出料剩余分散时间")]
        出料剩余分散时间,

        /// <summary>设备状态值，单个 PLC word。</summary>
        [HomogenizationReadSignal("Homogenization.EquipmentStatusValue", "D711", 1, "Int16", 6, "单点读数据", "设备状态", "状态值", DisplayName = "设备状态值")]
        设备状态值
    }

    /// <summary>
    /// 匀浆连续读数据枚举。该类点位由业务任务按场景读取数组或字符串，读取长度属于插件业务定义。
    /// </summary>
    public enum ContinuousRead
    {
        /// <summary>托盘码，连续 ASCII 字符区。</summary>
        [HomogenizationReadSignal("Homogenization.TrayCode", "D24500", 30, "Ascii", 7, "连续读数据", "托盘数据", "托盘码")]
        托盘码,

        /// <summary>配方搅拌转速，连续数组。</summary>
        [HomogenizationReadSignal("Homogenization.Recipe.StirringSpeed", "ZR400", 30, "UInt16", 14, "连续读数据", "配方数组", "搅拌转速", DisplayName = "配方搅拌转速")]
        配方搅拌转速,

        /// <summary>配方分散转速，连续数组。</summary>
        [HomogenizationReadSignal("Homogenization.Recipe.DispersionSpeed", "ZR500", 30, "UInt16", 15, "连续读数据", "配方数组", "分散转速", DisplayName = "配方分散转速")]
        配方分散转速,

        /// <summary>配方 NCM，连续浮点数组。</summary>
        [HomogenizationReadSignal("Homogenization.Recipe.Ncm", "ZR1000", 60, "Float", 16, "连续读数据", "配方数组", "NCM", DisplayName = "配方 NCM")]
        配方NCM,

        /// <summary>配方 SP1，连续浮点数组。</summary>
        [HomogenizationReadSignal("Homogenization.Recipe.Sp1", "ZR1800", 60, "Float", 17, "连续读数据", "配方数组", "SP1", DisplayName = "配方 SP1")]
        配方SP1,

        /// <summary>配方 NMP，连续浮点数组。</summary>
        [HomogenizationReadSignal("Homogenization.Recipe.Nmp", "ZR1200", 60, "Float", 18, "连续读数据", "配方数组", "NMP", DisplayName = "配方 NMP")]
        配方NMP,

        /// <summary>配方胶液，连续浮点数组。</summary>
        [HomogenizationReadSignal("Homogenization.Recipe.GlueSolution", "ZR1400", 60, "Float", 19, "连续读数据", "配方数组", "胶液", DisplayName = "配方胶液")]
        配方胶液,

        /// <summary>配方 CNT，连续浮点数组。</summary>
        [HomogenizationReadSignal("Homogenization.Recipe.Cnt", "ZR1600", 60, "Float", 20, "连续读数据", "配方数组", "CNT", DisplayName = "配方 CNT")]
        配方CNT,

        /// <summary>配方真空，连续布尔数组。</summary>
        [HomogenizationReadSignal("Homogenization.Recipe.Vacuum", "R300", 30, "Bool", 21, "连续读数据", "配方数组", "真空", DisplayName = "配方真空")]
        配方真空,

        /// <summary>配方时间，连续数组。</summary>
        [HomogenizationReadSignal("Homogenization.Recipe.Time", "ZR0", 30, "UInt16", 22, "连续读数据", "配方数组", "时间", DisplayName = "配方时间")]
        配方时间,

        /// <summary>配方温度，连续数组。</summary>
        [HomogenizationReadSignal("Homogenization.Recipe.Temperature", "ZR100", 30, "Int16", 23, "连续读数据", "配方数组", "温度", DisplayName = "配方温度")]
        配方温度,

        /// <summary>配方停机步，连续布尔数组。</summary>
        [HomogenizationReadSignal("Homogenization.Recipe.StopStep", "ZR200", 30, "Bool", 24, "连续读数据", "配方数组", "停机步", DisplayName = "配方停机步")]
        配方停机步
    }

    /// <summary>
    /// 匀浆单点写数据枚举。该类点位不参与信号交互循环，后续业务任务需要写单个值时在这里声明。
    /// </summary>
    public enum SingleWrite
    {
    }

    /// <summary>
    /// 匀浆连续写数据枚举。该类点位不参与信号交互循环，后续业务任务需要写连续数组时在这里声明。
    /// </summary>
    public enum ContinuousWrite
    {
    }
}
