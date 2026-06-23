using IIoT.Edge.Application.Modules.Hardware;

namespace IIoT.Edge.Module.DieCutting.Config.Io;

/// <summary>
/// 模切 PLC 信号枚举容器。首版只声明单点读和连续读，写点枚举保持空实现。
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
        /// 实际产量，PLC 两个 word 合成 32 位整数。
        /// </summary>
        [PlcReadSignal("DieCutting.PunchingQuantity", "R2450", 2, "Int32", 10, "单点读数据", "MES采样", "实际产量", DisplayName = "实际产量")]
        实际产量,

        /// <summary>
        /// 冲切速度，PLC 两个 word 合成 32 位整数后除以 100000。
        /// </summary>
        [PlcReadSignal("DieCutting.PunchingSpeed", "R4002", 2, "Int32", 20, "单点读数据", "MES采样", "冲切速度", DisplayName = "冲切速度")]
        冲切速度
    }

    /// <summary>
    /// 模切连续读数据，当前只读取两个弹夹位的 ASCII 编号。
    /// </summary>
    public enum ContinuousRead
    {
        /// <summary>
        /// MG#1 弹夹号，ASCII 连续区。
        /// </summary>
        [PlcReadSignal("DieCutting.ClipNo.Mg1", "R3535", 11, "Ascii", 30, "连续读数据", "MES采样", "MG#1 弹夹号", DisplayName = "MG#1 弹夹号")]
        弹夹号MG1,

        /// <summary>
        /// MG#2 弹夹号，ASCII 连续区。
        /// </summary>
        [PlcReadSignal("DieCutting.ClipNo.Mg2", "R3635", 11, "Ascii", 40, "连续读数据", "MES采样", "MG#2 弹夹号", DisplayName = "MG#2 弹夹号")]
        弹夹号MG2
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
