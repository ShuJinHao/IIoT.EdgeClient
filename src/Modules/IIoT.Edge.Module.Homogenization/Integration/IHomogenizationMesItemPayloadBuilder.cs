using IIoT.Edge.Module.Homogenization.Payload;

namespace IIoT.Edge.Module.Homogenization.Integration;

/// <summary>
/// 匀浆 MES item payload 构建器接口，用于隔离 MES 通道和字段映射协作者装配。
/// </summary>
public interface IHomogenizationMesItemPayloadBuilder
{
    /// <summary>
    /// 构建实时数据 MES item 列表。
    /// </summary>
    IReadOnlyList<object> BuildRealtimeItems(HomogenizationRealtimeSnapshot snapshot);

    /// <summary>
    /// 构建配方参数 MES item 列表。
    /// </summary>
    IReadOnlyList<object> BuildRecipeItems(HomogenizationRecipeSnapshot snapshot);

    /// <summary>
    /// 构建出料上传 produce 字段。
    /// </summary>
    IReadOnlyList<object> BuildOutboundProduce(
        HomogenizationCellData cellData,
        Func<DateTime, string> formatTimestamp);
}
