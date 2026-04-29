using IIoT.Edge.Module.Homogenization.Resources;
using IIoT.Edge.Application.Modules.Mes;
using Microsoft.Extensions.Options;

namespace IIoT.Edge.Module.Homogenization.Config;

/// <summary>
/// 匀浆模块运行和界面配置。
/// </summary>
public sealed class HomogenizationModuleOptions
{
    /// <summary>
    /// 界面展示相关配置。
    /// </summary>
    public HomogenizationPresentationOptions Presentation { get; set; } = new();

    /// <summary>
    /// PLC 任务循环相关配置。
    /// </summary>
    public HomogenizationRuntimeOptions Runtime { get; set; } = new();
}

/// <summary>
/// 匀浆模块界面刷新和缓存配置。
/// </summary>
public sealed class HomogenizationPresentationOptions
{
    /// <summary>
    /// 数据页面刷新间隔，单位毫秒。
    /// </summary>
    public int DataViewRefreshIntervalMs { get; set; } = 1000;

    /// <summary>
    /// UI 内存中保留的最近出料记录上限。
    /// </summary>
    public int MaxOutboundRecords { get; set; } = 500;
}

/// <summary>
/// 匀浆 PLC 任务循环配置。
/// </summary>
public sealed class HomogenizationRuntimeOptions
{
    /// <summary>
    /// 触发-应答、心跳任务循环间隔，单位毫秒。
    /// </summary>
    public int EventLoopIntervalMs { get; set; } = 50;

    /// <summary>
    /// 实时快照上传任务循环间隔，单位毫秒。
    /// </summary>
    public int RealtimeLoopIntervalMs { get; set; } = 1000;

    /// <summary>
    /// 触发-应答、心跳任务允许的最小循环间隔，单位毫秒。
    /// </summary>
    public int MinEventLoopIntervalMs { get; set; } = 20;

    /// <summary>
    /// 实时快照上传任务允许的最小循环间隔，单位毫秒。
    /// </summary>
    public int MinRealtimeLoopIntervalMs { get; set; } = 200;
}

/// <summary>
/// 匀浆 MES 接口配置。
/// </summary>
public sealed class HomogenizationMesOptions
{
    /// <summary>
    /// MES 签名令牌，用于生成接口 sign 字段。
    /// </summary>
    public string SignToken { get; set; } = string.Empty;

    /// <summary>
    /// 各 MES 接口相对路径。
    /// </summary>
    public HomogenizationMesPathOptions Paths { get; set; } = new();
}

/// <summary>
/// 匀浆 MES 接口相对路径配置。
/// </summary>
public sealed class HomogenizationMesPathOptions : IMesUploadPathOptions
{
    /// <summary>
    /// 进站校验接口路径。
    /// </summary>
    public string Inbound { get; set; } = string.Empty;

    /// <summary>
    /// 出料数据接口路径。
    /// </summary>
    public string Outbound { get; set; } = string.Empty;

    /// <summary>
    /// 配方参数接口路径。
    /// </summary>
    public string Recipe { get; set; } = string.Empty;

    /// <summary>
    /// 实时数据接口路径。
    /// </summary>
    public string Realtime { get; set; } = string.Empty;

    /// <summary>
    /// 设备状态接口路径。
    /// </summary>
    public string EquipmentStatus { get; set; } = string.Empty;

    internal void AppendValidationErrors(ICollection<string> errors)
    {
        HomogenizationOptionValidation.Require(Inbound, "MES 进站接口路径", errors);
        HomogenizationOptionValidation.Require(Outbound, "MES 出料接口路径", errors);
        HomogenizationOptionValidation.Require(Recipe, "MES 工艺参数接口路径", errors);
        HomogenizationOptionValidation.Require(Realtime, "MES 实时数据接口路径", errors);
        HomogenizationOptionValidation.Require(EquipmentStatus, "MES 设备状态接口路径", errors);
    }
}

/// <summary>
/// 匀浆 PLC 和 MES 码表配置。
/// </summary>
public sealed class HomogenizationCodeOptions
{
    /// <summary>
    /// PLC 触发码和应答码配置。
    /// </summary>
    public HomogenizationPlcCodeOptions Plc { get; set; } = new();

    /// <summary>
    /// MES 通道和字段码表配置。
    /// </summary>
    public HomogenizationMesCodeOptions Mes { get; set; } = new();
}

/// <summary>
/// 匀浆 PLC 信号码配置。
/// </summary>
public sealed class HomogenizationPlcCodeOptions
{
    /// <summary>
    /// PLC 信号复位码。
    /// </summary>
    public ushort SignalReset { get; set; }

    /// <summary>
    /// PLC 信号触发码。
    /// </summary>
    public ushort SignalTrigger { get; set; }

    /// <summary>
    /// 正常完成应答码。
    /// </summary>
    public ushort AckOk { get; set; }

    /// <summary>
    /// 异常失败应答码。
    /// </summary>
    public ushort AckException { get; set; }

    /// <summary>
    /// MES 业务拒绝应答码。
    /// </summary>
    public ushort AckMesNg { get; set; }

    internal void AppendValidationErrors(ICollection<string> errors)
    {
        if (SignalReset == SignalTrigger)
        {
            errors.Add(HomogenizationText.Get(
                "Homogenization_Validate_PlcResetAndTriggerCannotEqual",
                "PLC 复位信号不能与触发信号相同。"));
        }
    }
}

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

    public HomogenizationMesItemCodeOptions GetRealtimeItem(string key)
        => GetItem(RealtimeItems, key, "实时数据");

    public HomogenizationMesItemCodeOptions GetRecipeItem(string key)
        => GetItem(RecipeItems, key, "配方参数");

    public HomogenizationMesItemCodeOptions GetOutboundItem(string key)
        => GetItem(OutboundProduceItems, key, "出料数据");

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

public sealed class HomogenizationModuleOptionsValidator : IValidateOptions<HomogenizationModuleOptions>
{
    public ValidateOptionsResult Validate(string? name, HomogenizationModuleOptions options)
    {
        var errors = new List<string>();
        if (options.Presentation.DataViewRefreshIntervalMs <= 0)
        {
            errors.Add(HomogenizationText.Format("Homogenization_Validate_RequiredFormat", "{0}必须大于 0。", "界面刷新间隔"));
        }

        if (options.Presentation.MaxOutboundRecords <= 0)
        {
            errors.Add(HomogenizationText.Format("Homogenization_Validate_RequiredFormat", "{0}必须大于 0。", "出料记录上限"));
        }

        if (options.Runtime.EventLoopIntervalMs <= 0)
        {
            errors.Add(HomogenizationText.Format("Homogenization_Validate_RequiredFormat", "{0}必须大于 0。", "任务循环间隔"));
        }

        if (options.Runtime.RealtimeLoopIntervalMs <= 0)
        {
            errors.Add(HomogenizationText.Format("Homogenization_Validate_RequiredFormat", "{0}必须大于 0。", "实时上传循环间隔"));
        }

        if (options.Runtime.MinEventLoopIntervalMs <= 0)
        {
            errors.Add(HomogenizationText.Format("Homogenization_Validate_RequiredFormat", "{0}必须大于 0。", "任务最小循环间隔"));
        }

        if (options.Runtime.MinRealtimeLoopIntervalMs <= 0)
        {
            errors.Add(HomogenizationText.Format("Homogenization_Validate_RequiredFormat", "{0}必须大于 0。", "实时上传最小循环间隔"));
        }

        return errors.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(errors);
    }
}

public sealed class HomogenizationMesOptionsValidator : IValidateOptions<HomogenizationMesOptions>
{
    public ValidateOptionsResult Validate(string? name, HomogenizationMesOptions options)
    {
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(options.SignToken))
        {
            errors.Add(HomogenizationText.Get(
                "Homogenization_Validate_MesSignTokenRequired",
                "匀浆 MES 签名令牌不能为空。"));
        }

        options.Paths.AppendValidationErrors(errors);
        return errors.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(errors);
    }
}

public sealed class HomogenizationCodeOptionsValidator : IValidateOptions<HomogenizationCodeOptions>
{
    public ValidateOptionsResult Validate(string? name, HomogenizationCodeOptions options)
    {
        var errors = new List<string>();
        options.Plc.AppendValidationErrors(errors);
        options.Mes.AppendValidationErrors(errors);
        return errors.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(errors);
    }
}

internal static class HomogenizationOptionValidation
{
    public static void Require(string value, string name, ICollection<string> errors)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            errors.Add(HomogenizationText.Format("Homogenization_Validate_RequiredFormat", "{0}必须大于 0。", name));
        }
    }
}
