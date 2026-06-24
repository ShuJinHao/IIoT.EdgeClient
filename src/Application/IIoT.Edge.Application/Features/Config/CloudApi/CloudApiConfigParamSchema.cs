using IIoT.Edge.Application.Abstractions.Config;
using System.Reflection;

namespace IIoT.Edge.Application.Features.Config.CloudApi;

/// <summary>
/// 宿主级云端 API 参数定义，参数页只允许编辑这个枚举声明出的白名单 key。
/// </summary>
public static class CloudApiConfigParamSchema
{
    public const string ModuleId = "CloudApi";
    public const string GroupDisplayNameResourceKey = "Navigation_Param_CloudApi_GroupTitle";
    public const string GroupDisplayNameFallback = "云端接口配置";
    public const string KeyPrefix = "CloudApi:";

    public const string BaseUrl = "CloudApi:BaseUrl";
    public const string ClientCode = "CloudApi:ClientCode";
    public const string BootstrapSecret = "CloudApi:BootstrapSecret";
    public const string DeviceInstancePath = "CloudApi:Paths:DeviceInstance";
    public const string BootstrapRefreshPath = "CloudApi:Paths:BootstrapRefresh";
    public const string IdentityDeviceLoginPath = "CloudApi:Paths:IdentityDeviceLogin";
    public const string HumanIdentityRefreshPath = "CloudApi:Paths:HumanIdentityRefresh";
    public const string DeviceLogPath = "CloudApi:Paths:DeviceLog";
    public const string ProcessUploadPath = "CloudApi:Paths:ProcessUpload";
    public const string PassStationBatchTemplatePath = "CloudApi:Paths:PassStationBatchTemplate";
    public const string CapacityHourlyPath = "CloudApi:Paths:CapacityHourly";
    public const string CapacitySummaryPath = "CloudApi:Paths:CapacitySummary";
    public const string CapacitySummaryRangePath = "CloudApi:Paths:CapacitySummaryRange";
    public const string RecipeByDeviceTemplatePath = "CloudApi:Paths:RecipeByDeviceTemplate";
    public const string ClientReleaseCatalogTemplatePath = "CloudApi:Paths:ClientReleaseCatalogTemplate";
    public const string ClientVersionReportPath = "CloudApi:Paths:ClientVersionReport";

    public static IReadOnlyList<CloudApiConfigParamDescriptor> Descriptors { get; } = Enum
        .GetValues<CloudApiConfigParam>()
        .Select(static (param, index) => Create(param, index + 1))
        .ToArray();

    public static bool IsCloudApiConfigKey(string key)
        => !string.IsNullOrWhiteSpace(key)
           && Descriptors.Any(descriptor => string.Equals(descriptor.Key, key.Trim(), StringComparison.OrdinalIgnoreCase));

    public static bool IsParamViewEditableKey(string key)
    {
        var normalizedKey = key?.Trim();
        return IsCloudApiConfigKey(normalizedKey ?? string.Empty)
               && !string.Equals(normalizedKey, ClientCode, StringComparison.OrdinalIgnoreCase)
               && !string.Equals(normalizedKey, BootstrapSecret, StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsCloudApiConfigPrefix(string key)
        => !string.IsNullOrWhiteSpace(key)
           && key.Trim().StartsWith(KeyPrefix, StringComparison.OrdinalIgnoreCase);

    public static CloudApiConfigParamDescriptor? Find(string key)
        => Descriptors.FirstOrDefault(descriptor => string.Equals(descriptor.Key, key.Trim(), StringComparison.OrdinalIgnoreCase));

    public static string GetDefaultValue(string key, CloudApiConfigSnapshot snapshot)
        => key.Trim() switch
        {
            BaseUrl => snapshot.BaseUrl,
            ClientCode => snapshot.ClientCode,
            BootstrapSecret => snapshot.BootstrapSecret,
            DeviceInstancePath => snapshot.DeviceInstancePath,
            BootstrapRefreshPath => snapshot.BootstrapRefreshPath,
            IdentityDeviceLoginPath => snapshot.IdentityDeviceLoginPath,
            HumanIdentityRefreshPath => snapshot.HumanIdentityRefreshPath,
            DeviceLogPath => snapshot.DeviceLogPath,
            ProcessUploadPath => snapshot.ProcessUploadPath,
            PassStationBatchTemplatePath => snapshot.PassStationBatchTemplatePath,
            CapacityHourlyPath => snapshot.CapacityHourlyPath,
            CapacitySummaryPath => snapshot.CapacitySummaryPath,
            CapacitySummaryRangePath => snapshot.CapacitySummaryRangePath,
            RecipeByDeviceTemplatePath => snapshot.RecipeByDeviceTemplatePath,
            ClientReleaseCatalogTemplatePath => snapshot.ClientReleaseCatalogTemplatePath,
            ClientVersionReportPath => snapshot.ClientVersionReportPath,
            _ => string.Empty
        };

    private static CloudApiConfigParamDescriptor Create(CloudApiConfigParam param, int sortOrder)
    {
        var name = param.ToString();
        var attribute = typeof(CloudApiConfigParam)
            .GetField(name, BindingFlags.Public | BindingFlags.Static)
            ?.GetCustomAttribute<ModuleParamAttribute>()
            ?? throw new InvalidOperationException($"云端 API 参数枚举成员 '{name}' 缺少 ModuleParamAttribute。");

        return new CloudApiConfigParamDescriptor(
            GetKey(param),
            name,
            attribute.DisplayNameResourceKey ?? $"Navigation_Param_CloudApi_{name}_DisplayName",
            attribute.DisplayNameFallback ?? name,
            attribute.DescriptionResourceKey ?? $"Navigation_Param_CloudApi_{name}_Description",
            attribute.DescriptionFallback ?? string.Empty,
            attribute.ValueKind,
            sortOrder);
    }

    private static string GetKey(CloudApiConfigParam param)
        => param switch
        {
            CloudApiConfigParam.BaseUrl => BaseUrl,
            CloudApiConfigParam.ClientCode => ClientCode,
            CloudApiConfigParam.BootstrapSecret => BootstrapSecret,
            CloudApiConfigParam.DeviceInstancePath => DeviceInstancePath,
            CloudApiConfigParam.BootstrapRefreshPath => BootstrapRefreshPath,
            CloudApiConfigParam.IdentityDeviceLoginPath => IdentityDeviceLoginPath,
            CloudApiConfigParam.HumanIdentityRefreshPath => HumanIdentityRefreshPath,
            CloudApiConfigParam.DeviceLogPath => DeviceLogPath,
            CloudApiConfigParam.ProcessUploadPath => ProcessUploadPath,
            CloudApiConfigParam.PassStationBatchTemplatePath => PassStationBatchTemplatePath,
            CloudApiConfigParam.CapacityHourlyPath => CapacityHourlyPath,
            CloudApiConfigParam.CapacitySummaryPath => CapacitySummaryPath,
            CloudApiConfigParam.CapacitySummaryRangePath => CapacitySummaryRangePath,
            CloudApiConfigParam.RecipeByDeviceTemplatePath => RecipeByDeviceTemplatePath,
            CloudApiConfigParam.ClientReleaseCatalogTemplatePath => ClientReleaseCatalogTemplatePath,
            CloudApiConfigParam.ClientVersionReportPath => ClientVersionReportPath,
            _ => throw new ArgumentOutOfRangeException(nameof(param), param, null)
        };
}

public enum CloudApiConfigParam
{
    [ModuleParam(
        ParamValueKind.String,
        DisplayNameResourceKey = "Navigation_Param_CloudApi_BaseUrl_DisplayName",
        DisplayNameFallback = "BaseUrl",
        DescriptionResourceKey = "Navigation_Param_CloudApi_BaseUrl_Description")]
    BaseUrl,

    [ModuleParam(
        ParamValueKind.String,
        DisplayNameResourceKey = "Navigation_Param_CloudApi_ClientCode_DisplayName",
        DisplayNameFallback = "ClientCode",
        DescriptionResourceKey = "Navigation_Param_CloudApi_ClientCode_Description")]
    ClientCode,

    [ModuleParam(
        ParamValueKind.String,
        DisplayNameResourceKey = "Navigation_Param_CloudApi_BootstrapSecret_DisplayName",
        DisplayNameFallback = "BootstrapSecret",
        DescriptionResourceKey = "Navigation_Param_CloudApi_BootstrapSecret_Description")]
    BootstrapSecret,

    [ModuleParam(
        ParamValueKind.String,
        DisplayNameResourceKey = "Navigation_Param_CloudApi_DeviceInstancePath_DisplayName",
        DisplayNameFallback = "DeviceInstancePath",
        DescriptionResourceKey = "Navigation_Param_CloudApi_DeviceInstancePath_Description")]
    DeviceInstancePath,

    [ModuleParam(
        ParamValueKind.String,
        DisplayNameResourceKey = "Navigation_Param_CloudApi_BootstrapRefreshPath_DisplayName",
        DisplayNameFallback = "BootstrapRefreshPath",
        DescriptionResourceKey = "Navigation_Param_CloudApi_BootstrapRefreshPath_Description")]
    BootstrapRefreshPath,

    [ModuleParam(
        ParamValueKind.String,
        DisplayNameResourceKey = "Navigation_Param_CloudApi_IdentityDeviceLoginPath_DisplayName",
        DisplayNameFallback = "IdentityDeviceLoginPath",
        DescriptionResourceKey = "Navigation_Param_CloudApi_IdentityDeviceLoginPath_Description")]
    IdentityDeviceLoginPath,

    [ModuleParam(
        ParamValueKind.String,
        DisplayNameResourceKey = "Navigation_Param_CloudApi_HumanIdentityRefreshPath_DisplayName",
        DisplayNameFallback = "HumanIdentityRefreshPath",
        DescriptionResourceKey = "Navigation_Param_CloudApi_HumanIdentityRefreshPath_Description")]
    HumanIdentityRefreshPath,

    [ModuleParam(
        ParamValueKind.String,
        DisplayNameResourceKey = "Navigation_Param_CloudApi_DeviceLogPath_DisplayName",
        DisplayNameFallback = "DeviceLogPath",
        DescriptionResourceKey = "Navigation_Param_CloudApi_DeviceLogPath_Description")]
    DeviceLogPath,

    [ModuleParam(
        ParamValueKind.String,
        DisplayNameResourceKey = "Navigation_Param_CloudApi_ProcessUploadPath_DisplayName",
        DisplayNameFallback = "ProcessUploadPath",
        DescriptionResourceKey = "Navigation_Param_CloudApi_ProcessUploadPath_Description")]
    ProcessUploadPath,

    [ModuleParam(
        ParamValueKind.String,
        DisplayNameResourceKey = "Navigation_Param_CloudApi_PassStationBatchTemplatePath_DisplayName",
        DisplayNameFallback = "PassStationBatchTemplatePath",
        DescriptionResourceKey = "Navigation_Param_CloudApi_PassStationBatchTemplatePath_Description")]
    PassStationBatchTemplatePath,

    [ModuleParam(
        ParamValueKind.String,
        DisplayNameResourceKey = "Navigation_Param_CloudApi_CapacityHourlyPath_DisplayName",
        DisplayNameFallback = "CapacityHourlyPath",
        DescriptionResourceKey = "Navigation_Param_CloudApi_CapacityHourlyPath_Description")]
    CapacityHourlyPath,

    [ModuleParam(
        ParamValueKind.String,
        DisplayNameResourceKey = "Navigation_Param_CloudApi_CapacitySummaryPath_DisplayName",
        DisplayNameFallback = "CapacitySummaryPath",
        DescriptionResourceKey = "Navigation_Param_CloudApi_CapacitySummaryPath_Description")]
    CapacitySummaryPath,

    [ModuleParam(
        ParamValueKind.String,
        DisplayNameResourceKey = "Navigation_Param_CloudApi_CapacitySummaryRangePath_DisplayName",
        DisplayNameFallback = "CapacitySummaryRangePath",
        DescriptionResourceKey = "Navigation_Param_CloudApi_CapacitySummaryRangePath_Description")]
    CapacitySummaryRangePath,

    [ModuleParam(
        ParamValueKind.String,
        DisplayNameResourceKey = "Navigation_Param_CloudApi_RecipeByDeviceTemplatePath_DisplayName",
        DisplayNameFallback = "RecipeByDeviceTemplatePath",
        DescriptionResourceKey = "Navigation_Param_CloudApi_RecipeByDeviceTemplatePath_Description")]
    RecipeByDeviceTemplatePath,

    [ModuleParam(
        ParamValueKind.String,
        DisplayNameResourceKey = "Navigation_Param_CloudApi_ClientReleaseCatalogTemplatePath_DisplayName",
        DisplayNameFallback = "ClientReleaseCatalogTemplatePath",
        DescriptionResourceKey = "Navigation_Param_CloudApi_ClientReleaseCatalogTemplatePath_Description")]
    ClientReleaseCatalogTemplatePath,

    [ModuleParam(
        ParamValueKind.String,
        DisplayNameResourceKey = "Navigation_Param_CloudApi_ClientVersionReportPath_DisplayName",
        DisplayNameFallback = "ClientVersionReportPath",
        DescriptionResourceKey = "Navigation_Param_CloudApi_ClientVersionReportPath_Description")]
    ClientVersionReportPath
}

public sealed record CloudApiConfigParamDescriptor(
    string Key,
    string Name,
    string DisplayNameResourceKey,
    string DisplayNameFallback,
    string DescriptionResourceKey,
    string DescriptionFallback,
    ParamValueKind ValueKind,
    int SortOrder);
