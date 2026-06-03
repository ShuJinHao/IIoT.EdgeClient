using IIoT.Edge.Module.Homogenization.Resources;

namespace IIoT.Edge.Module.Homogenization.Config;

/// <summary>
/// 匀浆 MES 通道和字段码表配置。
/// </summary>
public sealed class HomogenizationMesCodeOptions
{
    /// <summary>
    /// MES 诊断通道名称。
    /// </summary>
    public HomogenizationMesChannelOptions Channels { get; set; } = new();

    /// <summary>
    /// 实时数据字段码表。
    /// </summary>
    public Dictionary<string, HomogenizationMesItemCodeOptions> RealtimeItems { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// 配方字段码表。
    /// </summary>
    public Dictionary<string, HomogenizationMesItemCodeOptions> RecipeItems { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// 出料字段码表。
    /// </summary>
    public Dictionary<string, HomogenizationMesItemCodeOptions> OutboundProduceItems { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// PLC 设备状态码到 MES 文本的映射。
    /// </summary>
    public Dictionary<string, string> EquipmentStatusTexts { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// 按匀浆实时快照字段名获取对应 MES item code。
    /// </summary>
    public HomogenizationMesItemCodeOptions GetRealtimeItem(string key)
        => GetItem(RealtimeItems, key, "实时数据");

    /// <summary>
    /// 按匀浆配方快照字段名获取对应 MES item code。
    /// </summary>
    public HomogenizationMesItemCodeOptions GetRecipeItem(string key)
        => GetItem(RecipeItems, key, "配方参数");

    /// <summary>
    /// 按匀浆出料字段名获取对应 MES produce item code。
    /// </summary>
    public HomogenizationMesItemCodeOptions GetOutboundItem(string key)
        => GetItem(OutboundProduceItems, key, "出料数据");

    /// <summary>
    /// 将 PLC 设备状态码转换为 MES 设备状态文本，未知状态返回插件默认文案。
    /// </summary>
    public string ResolveEquipmentStatusText(short statusCode)
        => EquipmentStatusTexts.TryGetValue(statusCode.ToString(), out var text) && !string.IsNullOrWhiteSpace(text)
            ? text
            : HomogenizationText.Get("Homogenization_EquipmentStatus_Unknown", "未知");

    internal void AppendValidationErrors(ICollection<string> errors)
    {
        Channels.AppendValidationErrors(errors);

        RequireItems(errors, RealtimeItems, "实时数据", "StirringSpeed", "StirringCurrent", "DispersionSpeed", "DispersionCurrent", "Temperature", "Vacuum");
        RequireItems(errors, RecipeItems, "配方参数", "StirringSpeed", "DispersionSpeed", "Ncm", "Sp1", "Nmp", "GlueSolution", "Cnt", "Vacuum", "Time", "Temperature", "StopStep");
        RequireItems(errors, OutboundProduceItems, "出料数据", "DeviceCode", "DeviceName", "StartTime", "CompleteTime", "StirringSpeed", "Temperature", "Vacuum", "CntActual", "CntTarget", "CntTankAWeight", "CntTankBWeight", "NmpActual", "NmpTarget", "GlueActual", "SetStirringTime", "RemainingStirringTime", "SetDispersionTime", "RemainingDispersionTime");
    }

    private static HomogenizationMesItemCodeOptions GetItem(
        IReadOnlyDictionary<string, HomogenizationMesItemCodeOptions> items,
        string key,
        string groupName)
    {
        if (!items.TryGetValue(key, out var item))
        {
            throw new InvalidOperationException(HomogenizationText.Format(
                "Homogenization_Validate_MesItemMissingFormat",
                "{0} 缺少 {1} 编码配置。",
                groupName,
                key));
        }

        return item;
    }

    private static void RequireItems(
        ICollection<string> errors,
        IReadOnlyDictionary<string, HomogenizationMesItemCodeOptions> items,
        string groupName,
        params string[] keys)
    {
        foreach (var key in keys)
        {
            if (!items.TryGetValue(key, out var item))
            {
                errors.Add(HomogenizationText.Format(
                    "Homogenization_Validate_MesItemMissingFormat",
                    "{0} 缺少 {1} 编码配置。",
                    groupName,
                    key));
                continue;
            }

            if (string.IsNullOrWhiteSpace(item.Code) || string.IsNullOrWhiteSpace(item.Name))
            {
                errors.Add(HomogenizationText.Format(
                    "Homogenization_Validate_MesItemCodeNameRequiredFormat",
                    "{0}.{1} 的编码和名称不能为空。",
                    groupName,
                    key));
            }
        }
    }
}

/// <summary>
/// 匀浆 MES 诊断通道配置。
/// </summary>
public sealed class HomogenizationMesChannelOptions
{
    /// <summary>
    /// 进站通道。
    /// </summary>
    public string Inbound { get; set; } = string.Empty;

    /// <summary>
    /// 出料通道。
    /// </summary>
    public string Outbound { get; set; } = string.Empty;

    /// <summary>
    /// 实时数据通道。
    /// </summary>
    public string Realtime { get; set; } = string.Empty;

    /// <summary>
    /// 配方参数通道。
    /// </summary>
    public string Recipe { get; set; } = string.Empty;

    /// <summary>
    /// 设备状态通道。
    /// </summary>
    public string EquipmentStatus { get; set; } = string.Empty;

    internal void AppendValidationErrors(ICollection<string> errors)
    {
        HomogenizationOptionValidation.Require(Inbound, "MES 进站诊断通道", errors);
        HomogenizationOptionValidation.Require(Outbound, "MES 出料诊断通道", errors);
        HomogenizationOptionValidation.Require(Realtime, "MES 实时数据诊断通道", errors);
        HomogenizationOptionValidation.Require(Recipe, "MES 工艺参数诊断通道", errors);
        HomogenizationOptionValidation.Require(EquipmentStatus, "MES 设备状态诊断通道", errors);
    }
}

/// <summary>
/// 单个 MES 字段码表项。
/// </summary>
public sealed class HomogenizationMesItemCodeOptions
{
    /// <summary>
    /// MES 字段编码。
    /// </summary>
    public string Code { get; set; } = string.Empty;

    /// <summary>
    /// MES 字段显示名称。
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// MES 字段类型。
    /// </summary>
    public string Type { get; set; } = string.Empty;

    /// <summary>
    /// MES 字段单位。
    /// </summary>
    public string Unit { get; set; } = string.Empty;
}
