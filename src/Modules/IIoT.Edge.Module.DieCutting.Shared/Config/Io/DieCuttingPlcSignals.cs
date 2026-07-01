using IIoT.Edge.Application.Modules.Hardware;

namespace IIoT.Edge.Module.DieCutting.Config.Io;

/// <summary>
/// 模切 PLC 信号枚举容器。模切只读采集 PLC 点位，不做 PLC 写入或握手交互。
/// </summary>
public static class DieCuttingPlcSignals
{
    /// <summary>
    /// 模切不做 PLC 信号交互，枚举按模块契约保留为空。
    /// </summary>
    public enum Interaction
    {
    }

    /// <summary>
    /// 模切单点读数据，全部由 DeviceComm 只读扫描刷新到 buffer。
    /// </summary>
    public enum SingleRead
    {
        /// <summary>
        /// 设备状态码，按 MES 设备状态码表上传。
        /// </summary>
        [PlcReadSignal("DieCutting.DeviceStatus", "R100", 1, "Int16", 5, "单点读数据", "设备状态", "设备状态", DisplayName = "设备状态")]
        设备状态,

        /// <summary>
        /// 实际产量，PLC 两个 word 合成 32 位整数。
        /// </summary>
        [PlcReadSignal("DieCutting.PunchingQuantity", "R2450", 2, "Int32", 10, "单点读数据", "MES采样", "实际产量", DisplayName = "实际产量")]
        实际产量,

        /// <summary>
        /// 冲切速度，PLC 两个 word 合成 32 位整数后除以 100000。
        /// </summary>
        [PlcReadSignal("DieCutting.PunchingSpeed", "R4002", 2, "Int32", 20, "单点读数据", "MES采样", "冲切速度", DisplayName = "冲切速度")]
        冲切速度,

        [PlcReadSignal("DieCutting.UnwindingLength", "R2010", 2, "Int32", 30, "单点读数据", "生产信息", "放卷长度", DisplayName = "放卷长度")]
        放卷长度,

        [PlcReadSignal("DieCutting.MoldLifeSetting", "R2290", 2, "Int32", 40, "单点读数据", "生产信息", "模具寿命设定", DisplayName = "模具寿命设定")]
        模具寿命设定,

        [PlcReadSignal("DieCutting.CutterLifeSetting", "R2390", 2, "Int32", 50, "单点读数据", "生产信息", "切刀寿命设定", DisplayName = "切刀寿命设定")]
        切刀寿命设定,

        [PlcReadSignal("DieCutting.Mg1ReceivingSet", "R2600", 1, "UInt16", 60, "单点读数据", "生产信息", "MG#1 收料片数设定", DisplayName = "MG#1 收料片数设定")]
        收料片数MG1设定,

        [PlcReadSignal("DieCutting.Mg1ReceivingActual", "R2610", 1, "UInt16", 70, "单点读数据", "生产信息", "MG#1 收料片数实际", DisplayName = "MG#1 收料片数实际")]
        收料片数MG1实际,

        [PlcReadSignal("DieCutting.Mg2ReceivingSet", "R2700", 1, "UInt16", 80, "单点读数据", "生产信息", "MG#2 收料片数设定", DisplayName = "MG#2 收料片数设定")]
        收料片数MG2设定,

        [PlcReadSignal("DieCutting.Mg2ReceivingActual", "R2710", 1, "UInt16", 90, "单点读数据", "生产信息", "MG#2 收料片数实际", DisplayName = "MG#2 收料片数实际")]
        收料片数MG2实际,

        [PlcReadSignal("DieCutting.Mg2TapeSensor", "R8655", 1, "UInt16", 100, "单点读数据", "生产信息", "MG#2 胶带/标签感应", DisplayName = "MG#2 胶带/标签感应")]
        胶带标签感应MG2,

        [PlcReadSignal("DieCutting.Mg1TapeSensor", "R9655", 1, "UInt16", 110, "单点读数据", "生产信息", "MG#1 胶带/标签感应", DisplayName = "MG#1 胶带/标签感应")]
        胶带标签感应MG1,

        [PlcReadSignal("DieCutting.OkSheetQuantity", "R13030", 2, "Int32", 120, "单点读数据", "生产信息", "弹夹OK级片数量", DisplayName = "弹夹OK级片数量")]
        弹夹OK级片数量,

        [PlcReadSignal("DieCutting.RollTensionActual", "D3150", 1, "UInt16", 130, "单点读数据", "生产信息", "卷料张力实际", DisplayName = "卷料张力实际")]
        卷料张力实际,

        [PlcReadSignal("DieCutting.RollTensionSet", "D3310", 1, "UInt16", 140, "单点读数据", "生产信息", "卷料张力设定", DisplayName = "卷料张力设定")]
        卷料张力设定,

        [PlcReadSignal("DieCutting.CorrectionPositionActual", "D7200", 2, "Int32", 150, "单点读数据", "生产信息", "自动纠偏位置实际", DisplayName = "自动纠偏位置实际")]
        自动纠偏位置实际,

        [PlcReadSignal("DieCutting.CutterPositionActual", "D7300", 2, "Int32", 160, "单点读数据", "生产信息", "切刀位置实际", DisplayName = "切刀位置实际")]
        切刀位置实际,

        [PlcReadSignal("DieCutting.Heartbeat", "D9042", 1, "UInt16", 170, "单点读数据", "生产信息", "心跳", DisplayName = "心跳")]
        心跳,

        [PlcReadSignal("DieCutting.PolePieceWidth", "ZR10020", 2, "Int32", 180, "单点读数据", "生产信息", "极片宽度", DisplayName = "极片宽度")]
        极片宽度,

        [PlcReadSignal("DieCutting.CutterPositionSet", "ZR10026", 2, "Int32", 190, "单点读数据", "生产信息", "切刀位置设定", DisplayName = "切刀位置设定")]
        切刀位置设定,

        [PlcReadSignal("DieCutting.CorrectionPositionSet", "ZR10050", 2, "Int32", 200, "单点读数据", "生产信息", "自动纠偏位置设定", DisplayName = "自动纠偏位置设定")]
        自动纠偏位置设定,

        [PlcReadSignal("DieCutting.TheoreticalSheetQuantity", "D3260", 1, "UInt16", 210, "单点读数据", "MES数据处理", "理论片数", DisplayName = "理论片数")]
        理论片数,

        [PlcReadSignal("DieCutting.ActualLength", "D1200", 2, "Int32", 220, "单点读数据", "MES数据处理", "实际长度", DisplayName = "实际长度")]
        实际长度,

        [PlcReadSignal("DieCutting.MesCommunicationException", "F360", 1, "Bool", 230, "单点读数据", "MES数据处理", "MES通讯异常", DisplayName = "MES通讯异常")]
        MES通讯异常,

        [PlcReadSignal("DieCutting.LoadConfirm", "M12", 1, "Bool", 240, "单点读数据", "MES数据处理", "上料确认", DisplayName = "上料确认")]
        上料确认
    }

    /// <summary>
    /// 模切连续读数据，读取批次号、弹夹号和人员/工装编号。
    /// </summary>
    public enum ContinuousRead
    {
        /// <summary>
        /// 批次号，CP01/AP01 默认 R9660，其他设备播种时覆盖为 R9600。
        /// </summary>
        [PlcReadSignal("DieCutting.BatchNumber", "R9660", 8, "Ascii", 300, "连续读数据", "MES采样", "批次号", DisplayName = "批次号")]
        批次号,

        /// <summary>
        /// MG#1 弹夹号，ASCII 连续区。
        /// </summary>
        [PlcReadSignal("DieCutting.ClipNo.Mg1", "R3535", 11, "Ascii", 310, "连续读数据", "MES采样", "MG#1 弹夹号", DisplayName = "MG#1 弹夹号")]
        弹夹号MG1,

        /// <summary>
        /// MG#2 弹夹号，ASCII 连续区。
        /// </summary>
        [PlcReadSignal("DieCutting.ClipNo.Mg2", "R3635", 11, "Ascii", 320, "连续读数据", "MES采样", "MG#2 弹夹号", DisplayName = "MG#2 弹夹号")]
        弹夹号MG2,

        [PlcReadSignal("DieCutting.OperatorCode", "R9420", 5, "Ascii", 330, "连续读数据", "生产信息", "操作员工号", DisplayName = "操作员工号")]
        操作员工号,

        [PlcReadSignal("DieCutting.MoldCode", "R2210", 5, "Ascii", 340, "连续读数据", "生产信息", "模具编号", DisplayName = "模具编号")]
        模具编号,

        [PlcReadSignal("DieCutting.CutterCode", "R2230", 5, "Ascii", 350, "连续读数据", "生产信息", "切刀编号", DisplayName = "切刀编号")]
        切刀编号,

        [PlcReadSignal("DieCutting.ClipNo.Cache", "D1100", 20, "Ascii", 360, "连续读数据", "MES数据处理", "弹夹编号缓存", DisplayName = "弹夹编号缓存")]
        弹夹编号缓存
    }

    /// <summary>
    /// 模切首版不写 PLC，枚举按模块契约保留为空。
    /// </summary>
    public enum SingleWrite
    {
    }

    /// <summary>
    /// 模切首版不写 PLC，枚举按模块契约保留为空。
    /// </summary>
    public enum ContinuousWrite
    {
    }
}
