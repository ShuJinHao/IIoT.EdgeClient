using System.IO;
using Microsoft.Extensions.Configuration;

namespace IIoT.Edge.Module.Homogenization.Config;

public sealed class HomogenizationModuleConfiguration
{
    private const string HostOverrideSection = "Modules:Homogenization:ModuleConfig";

    public HomogenizationModuleOptions Module { get; set; } = new();

    public HomogenizationMesOptions Mes { get; set; } = new();

    public HomogenizationCodeOptions Codes { get; set; } = new();

    public static HomogenizationModuleConfiguration Load(IConfiguration? hostConfiguration = null)
    {
        var result = new HomogenizationModuleConfiguration();

        // 优先加载机型覆盖，再加载插件随包配置；同名 key 以插件配置为准，避免每个插件都要改宿主。
        hostConfiguration?.GetSection(HostOverrideSection).Bind(result);

        var pluginConfiguration = new ConfigurationBuilder()
            .AddJsonFile(ResolveConfigPath("homogenization.module.json"), optional: false, reloadOnChange: true)
            .Build();
        pluginConfiguration.Bind(result);

        result.Validate();
        return result;
    }

    public static string ResolveConfigPath(string fileName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);

        var assemblyDirectory = Path.GetDirectoryName(typeof(HomogenizationModule).Assembly.Location);
        if (!string.IsNullOrWhiteSpace(assemblyDirectory))
        {
            var outputPath = Path.Combine(assemblyDirectory, "Config", fileName);
            if (File.Exists(outputPath))
            {
                return outputPath;
            }
        }

        throw new FileNotFoundException($"未找到匀浆模块配置文件：{fileName}。");
    }

    private void Validate()
    {
        if (string.IsNullOrWhiteSpace(Mes.SignToken))
        {
            throw new InvalidOperationException("匀浆 MES 签名令牌不能为空。");
        }

        Mes.Paths.Validate();
        Codes.Validate();
    }

    internal static void Require(string value, string name)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException($"匀浆配置 {name} 不能为空。");
        }
    }
}

public sealed class HomogenizationModuleOptions
{
    public HomogenizationPresentationOptions Presentation { get; set; } = new();

    public HomogenizationRuntimeOptions Runtime { get; set; } = new();
}

public sealed class HomogenizationPresentationOptions
{
    public int DataViewRefreshIntervalMs { get; set; } = 1000;
}

public sealed class HomogenizationRuntimeOptions
{
    public int EventLoopIntervalMs { get; set; } = 50;

    public int RealtimeLoopIntervalMs { get; set; } = 1000;
}

public sealed class HomogenizationMesOptions
{
    public string SignToken { get; set; } = string.Empty;

    public HomogenizationMesPathOptions Paths { get; set; } = new();
}

public sealed class HomogenizationMesPathOptions
{
    public string Inbound { get; set; } = string.Empty;

    public string Outbound { get; set; } = string.Empty;

    public string Recipe { get; set; } = string.Empty;

    public string Realtime { get; set; } = string.Empty;

    public string EquipmentStatus { get; set; } = string.Empty;

    public void Validate()
    {
        HomogenizationModuleConfiguration.Require(Inbound, "MES 进站接口路径");
        HomogenizationModuleConfiguration.Require(Outbound, "MES 出料接口路径");
        HomogenizationModuleConfiguration.Require(Recipe, "MES 工艺参数接口路径");
        HomogenizationModuleConfiguration.Require(Realtime, "MES 实时数据接口路径");
        HomogenizationModuleConfiguration.Require(EquipmentStatus, "MES 设备状态接口路径");
    }
}

public sealed class HomogenizationCodeOptions
{
    public HomogenizationPlcCodeOptions Plc { get; set; } = new();

    public HomogenizationMesCodeOptions Mes { get; set; } = new();

    public void Validate()
    {
        Plc.Validate();
        Mes.Validate();
    }
}

public sealed class HomogenizationPlcCodeOptions
{
    public ushort SignalReset { get; set; }

    public ushort SignalTrigger { get; set; }

    public ushort AckOk { get; set; }

    public ushort AckException { get; set; }

    public ushort AckMesNg { get; set; }

    public void Validate()
    {
        if (SignalReset == SignalTrigger)
        {
            throw new InvalidOperationException("匀浆 PLC 复位码和触发码不能相同。");
        }
    }
}

public sealed class HomogenizationMesCodeOptions
{
    public HomogenizationMesChannelOptions Channels { get; set; } = new();

    public Dictionary<string, HomogenizationMesItemCodeOptions> RealtimeItems { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public Dictionary<string, HomogenizationMesItemCodeOptions> RecipeItems { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public Dictionary<string, HomogenizationMesItemCodeOptions> OutboundProduceItems { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public Dictionary<string, string> EquipmentStatusTexts { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public void Validate()
    {
        Channels.Validate();

        RequireItems(RealtimeItems, "实时数据", "StirringSpeed", "StirringCurrent", "DispersionSpeed", "DispersionCurrent", "Temperature", "Vacuum");
        RequireItems(RecipeItems, "配方参数", "StirringSpeed", "DispersionSpeed", "Ncm", "Sp1", "Nmp", "GlueSolution", "Cnt", "Vacuum", "Time", "Temperature", "StopStep");
        RequireItems(OutboundProduceItems, "出料数据", "DeviceCode", "DeviceName", "StartTime", "CompleteTime", "StirringSpeed", "Temperature", "Vacuum", "CntActual", "CntTarget", "CntTankAWeight", "CntTankBWeight", "NmpActual", "NmpTarget", "GlueActual", "SetStirringTime", "RemainingStirringTime", "SetDispersionTime", "RemainingDispersionTime");
    }

    public HomogenizationMesItemCodeOptions GetRealtimeItem(string key)
        => GetItem(RealtimeItems, key, "实时数据");

    public HomogenizationMesItemCodeOptions GetRecipeItem(string key)
        => GetItem(RecipeItems, key, "配方参数");

    public HomogenizationMesItemCodeOptions GetOutboundItem(string key)
        => GetItem(OutboundProduceItems, key, "出料数据");

    public string ResolveEquipmentStatusText(short statusCode)
        => EquipmentStatusTexts.TryGetValue(statusCode.ToString(), out var text) && !string.IsNullOrWhiteSpace(text)
            ? text
            : "未知";

    private static HomogenizationMesItemCodeOptions GetItem(
        IReadOnlyDictionary<string, HomogenizationMesItemCodeOptions> items,
        string key,
        string groupName)
    {
        if (!items.TryGetValue(key, out var item))
        {
            throw new InvalidOperationException($"匀浆 MES {groupName}码表缺少键：{key}。");
        }

        return item;
    }

    private static void RequireItems(
        IReadOnlyDictionary<string, HomogenizationMesItemCodeOptions> items,
        string groupName,
        params string[] keys)
    {
        foreach (var key in keys)
        {
            var item = GetItem(items, key, groupName);
            if (string.IsNullOrWhiteSpace(item.Code) || string.IsNullOrWhiteSpace(item.Name))
            {
                throw new InvalidOperationException($"匀浆 MES {groupName}码表键 {key} 的 code/name 不能为空。");
            }
        }
    }
}

public sealed class HomogenizationMesChannelOptions
{
    public string Inbound { get; set; } = string.Empty;

    public string Outbound { get; set; } = string.Empty;

    public string Realtime { get; set; } = string.Empty;

    public string Recipe { get; set; } = string.Empty;

    public string EquipmentStatus { get; set; } = string.Empty;

    public void Validate()
    {
        HomogenizationModuleConfiguration.Require(Inbound, "MES 进站诊断通道");
        HomogenizationModuleConfiguration.Require(Outbound, "MES 出料诊断通道");
        HomogenizationModuleConfiguration.Require(Realtime, "MES 实时数据诊断通道");
        HomogenizationModuleConfiguration.Require(Recipe, "MES 工艺参数诊断通道");
        HomogenizationModuleConfiguration.Require(EquipmentStatus, "MES 设备状态诊断通道");
    }
}

public sealed class HomogenizationMesItemCodeOptions
{
    public string Code { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string Type { get; set; } = string.Empty;

    public string Unit { get; set; } = string.Empty;
}
