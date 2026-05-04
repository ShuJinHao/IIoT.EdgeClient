using IIoT.Edge.Application.Abstractions.Plc.Signals;
using IIoT.Edge.Application.Modules.Hardware;
using IIoT.Edge.Module.Stacking.Constants;

namespace IIoT.Edge.Module.Stacking.Config.Hardware;

/// <summary>
/// 叠片 PLC 信号键。运行任务只能使用该枚举访问 IO，禁止直接写字符串 Label。
/// </summary>
public enum StackingSignal
{
    工序序号,
    叠片层数,
    结果码,
    采集应答
}

/// <summary>
/// 叠片 PLC 信号 profile，负责提供默认 IO 模板分组。
/// </summary>
public sealed class StackingPlcSignalProfile : ModulePlcSignalProfileBase<StackingSignal>
{
    public override string ModuleId => StackingModuleConstants.ModuleId;

    protected override IEnumerable<ModuleSignalGroup<StackingSignal>> BuildGroups()
    {
        yield return BuildProductionSignals();
    }

    private ModuleSignalGroup<StackingSignal> BuildProductionSignals()
    {
        return Group(
            "叠片采集",
            Signal(StackingSignal.工序序号, "Stacking.Sequence", "DB1.DBW0", ModuleSignalDirection.Read, 1, "Int16", 1, "工序序号", "单点读数据", "叠片采集", "工序序号"),
            Signal(StackingSignal.叠片层数, "Stacking.LayerCount", "DB1.DBW2", ModuleSignalDirection.Read, 1, "Int16", 2, "叠片层数", "单点读数据", "叠片采集", "叠片层数"),
            Signal(StackingSignal.结果码, "Stacking.ResultCode", "DB1.DBW4", ModuleSignalDirection.Read, 1, "Int16", 3, "结果码", "单点读数据", "叠片采集", "结果码"),
            Signal(StackingSignal.采集应答, "Stacking.Ack", "DB1.DBW6", ModuleSignalDirection.Write, 1, "Int16", 4, "采集应答", "信号交互", "叠片采集", "采集应答"));
    }
}
