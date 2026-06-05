using IIoT.Edge.Module.Homogenization.Config;
using IIoT.Edge.Module.Homogenization.Config.Io;
using IIoT.Edge.Module.Homogenization.Config.Parameters;
using IIoT.Edge.Module.Homogenization.Payload;
using Microsoft.Extensions.Options;

namespace IIoT.Edge.Module.Homogenization.Integration.Mes;

/// <summary>
/// 匀浆 MES 字段 payload 构建器，负责把实时、配方和出料快照映射为 MES item 数组。
/// </summary>
public sealed class HomogenizationMesPayloadBuilder
{
    private readonly HomogenizationMesCodeOptions _mesCodes;

    public HomogenizationMesPayloadBuilder(IOptions<HomogenizationCodeOptions> codeOptions)
    {
        _mesCodes = codeOptions.Value.Mes;
    }

    /// <summary>
    /// 构建实时数据 MES item 列表。
    /// </summary>
    public IReadOnlyList<object> BuildRealtimeItems(HomogenizationRealtimeSnapshot snapshot)
        =>
        [
            CreateItem(_mesCodes.GetRealtimeItem(nameof(HomogenizationRealtimeSnapshot.StirringSpeed)), snapshot.StirringSpeed),
            CreateItem(_mesCodes.GetRealtimeItem(nameof(HomogenizationRealtimeSnapshot.StirringCurrent)), snapshot.StirringCurrent),
            CreateItem(_mesCodes.GetRealtimeItem(nameof(HomogenizationRealtimeSnapshot.DispersionSpeed)), snapshot.DispersionSpeed),
            CreateItem(_mesCodes.GetRealtimeItem(nameof(HomogenizationRealtimeSnapshot.DispersionCurrent)), snapshot.DispersionCurrent),
            CreateItem(_mesCodes.GetRealtimeItem(nameof(HomogenizationRealtimeSnapshot.Temperature)), snapshot.Temperature),
            CreateItem(_mesCodes.GetRealtimeItem(nameof(HomogenizationRealtimeSnapshot.Vacuum)), snapshot.Vacuum)
        ];

    /// <summary>
    /// 构建配方参数 MES item 列表，数组字段会展开为带序号的 MES 字段。
    /// </summary>
    public IReadOnlyList<object> BuildRecipeItems(HomogenizationRecipeSnapshot snapshot)
    {
        var items = new List<object>();

        AddIndexedRecipeItems(items, _mesCodes.GetRecipeItem(nameof(HomogenizationRecipeSnapshot.StirringSpeed)), snapshot.StirringSpeed);
        AddIndexedRecipeItems(items, _mesCodes.GetRecipeItem(nameof(HomogenizationRecipeSnapshot.DispersionSpeed)), snapshot.DispersionSpeed);
        AddIndexedRecipeItems(items, _mesCodes.GetRecipeItem(nameof(HomogenizationRecipeSnapshot.Ncm)), snapshot.Ncm);
        AddIndexedRecipeItems(items, _mesCodes.GetRecipeItem(nameof(HomogenizationRecipeSnapshot.Sp1)), snapshot.Sp1);
        AddIndexedRecipeItems(items, _mesCodes.GetRecipeItem(nameof(HomogenizationRecipeSnapshot.Nmp)), snapshot.Nmp);
        AddIndexedRecipeItems(items, _mesCodes.GetRecipeItem(nameof(HomogenizationRecipeSnapshot.GlueSolution)), snapshot.GlueSolution);
        AddIndexedRecipeItems(items, _mesCodes.GetRecipeItem(nameof(HomogenizationRecipeSnapshot.Cnt)), snapshot.Cnt);
        AddIndexedRecipeItems(items, _mesCodes.GetRecipeItem(nameof(HomogenizationRecipeSnapshot.Vacuum)), snapshot.Vacuum.Select(static value => value ? 1 : 0).ToArray());
        AddIndexedRecipeItems(items, _mesCodes.GetRecipeItem(nameof(HomogenizationRecipeSnapshot.Time)), snapshot.Time);
        AddIndexedRecipeItems(items, _mesCodes.GetRecipeItem(nameof(HomogenizationRecipeSnapshot.Temperature)), snapshot.Temperature);
        AddIndexedRecipeItems(items, _mesCodes.GetRecipeItem(nameof(HomogenizationRecipeSnapshot.StopStep)), snapshot.StopStep.Select(static value => value ? 1 : 0).ToArray());

        return items;
    }

    /// <summary>
    /// 构建出料上传 produce 字段，时间格式继续由 MES 通道基类提供。
    /// </summary>
    public IReadOnlyList<object> BuildOutboundProduce(
        HomogenizationCellData cellData,
        Func<DateTime, string> formatTimestamp)
    {
        var produce = new List<object>();

        AddProduceItem(produce, _mesCodes.GetOutboundItem("DeviceCode"), cellData.DeviceCode, formatTimestamp);
        AddProduceItem(produce, _mesCodes.GetOutboundItem("DeviceName"), cellData.DeviceName, formatTimestamp);
        AddProduceItem(produce, _mesCodes.GetOutboundItem("StartTime"), cellData.InboundTime, formatTimestamp);
        AddProduceItem(produce, _mesCodes.GetOutboundItem("CompleteTime"), cellData.CompletedTime, formatTimestamp);
        AddProduceItem(produce, _mesCodes.GetOutboundItem("StirringSpeed"), cellData.RealtimeSnapshot?.StirringSpeed, formatTimestamp);
        AddProduceItem(produce, _mesCodes.GetOutboundItem("Temperature"), cellData.RealtimeSnapshot?.Temperature, formatTimestamp);
        AddProduceItem(produce, _mesCodes.GetOutboundItem("Vacuum"), cellData.RealtimeSnapshot?.Vacuum, formatTimestamp);
        AddProduceItem(produce, _mesCodes.GetOutboundItem("CntActual"), cellData.CntActualKg, formatTimestamp);
        AddProduceItem(produce, _mesCodes.GetOutboundItem("CntTarget"), cellData.CntTargetKg, formatTimestamp);
        AddProduceItem(produce, _mesCodes.GetOutboundItem("CntTankAWeight"), cellData.CntTankAWeightKg, formatTimestamp);
        AddProduceItem(produce, _mesCodes.GetOutboundItem("CntTankBWeight"), cellData.CntTankBWeightKg, formatTimestamp);
        AddProduceItem(produce, _mesCodes.GetOutboundItem("NmpActual"), cellData.NmpActualKg, formatTimestamp);
        AddProduceItem(produce, _mesCodes.GetOutboundItem("NmpTarget"), cellData.NmpTargetKg, formatTimestamp);
        AddProduceItem(produce, _mesCodes.GetOutboundItem("GlueActual"), cellData.GlueActualKg, formatTimestamp);
        AddProduceItem(produce, _mesCodes.GetOutboundItem("SetStirringTime"), cellData.SetStirringTimeMinutes, formatTimestamp);
        AddProduceItem(produce, _mesCodes.GetOutboundItem("RemainingStirringTime"), cellData.RemainingStirringTimeMinutes, formatTimestamp);
        AddProduceItem(produce, _mesCodes.GetOutboundItem("SetDispersionTime"), cellData.SetDispersionTimeMinutes, formatTimestamp);
        AddProduceItem(produce, _mesCodes.GetOutboundItem("RemainingDispersionTime"), cellData.RemainingDispersionTimeMinutes, formatTimestamp);

        return produce;
    }

    private static object CreateItem(HomogenizationMesItemCodeOptions item, object? value)
        => new
        {
            code = item.Code,
            name = item.Name,
            type = item.Type,
            unit = item.Unit,
            val = value?.ToString() ?? string.Empty
        };

    private static void AddIndexedRecipeItems<T>(
        ICollection<object> items,
        HomogenizationMesItemCodeOptions item,
        IReadOnlyList<T> values)
    {
        for (var index = 0; index < values.Count; index++)
        {
            items.Add(new
            {
                code = $"{item.Code}_{index + 1:D2}",
                name = $"{item.Name}_{index + 1:D2}",
                type = item.Type,
                unit = item.Unit,
                val = values[index]?.ToString() ?? string.Empty
            });
        }
    }

    private static void AddProduceItem(
        ICollection<object> produce,
        HomogenizationMesItemCodeOptions item,
        object? value,
        Func<DateTime, string> formatTimestamp)
    {
        if (value is null)
        {
            return;
        }

        var text = value switch
        {
            DateTime time => formatTimestamp(time),
            DateTimeOffset timeOffset => formatTimestamp(timeOffset.DateTime),
            _ => value.ToString()
        };

        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        produce.Add(new
        {
            code = item.Code,
            name = item.Name,
            val = text
        });
    }
}
