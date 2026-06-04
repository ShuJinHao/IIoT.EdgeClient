using IIoT.Edge.Application.Modules.Mes;
using IIoT.Edge.Module.Homogenization.Payload;

namespace IIoT.Edge.Module.Homogenization.Integration;

/// <summary>
/// 匀浆 MES 场景通道契约。泛型实参只在插件边界声明一次，运行任务和测试依赖本插件强类型接口。
/// </summary>
public interface IHomogenizationMesScenarioChannel
    : IMesScenarioChannel<
        HomogenizationCellData,
        string,
        HomogenizationRealtimeSnapshot,
        HomogenizationRecipeSnapshot,
        HomogenizationEquipmentStatusSnapshot,
        HomogenizationMainPlanRequest,
        HomogenizationMainPlan,
        HomogenizationTraceBatchRequest,
        HomogenizationTraceBatchResult>
{
}
