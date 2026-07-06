namespace IIoT.Edge.Application.Abstractions.Config;

/// <summary>
/// 云端 API 配置快照，供应用层展示和对账使用，不依赖基础设施配置类型。
/// </summary>
public sealed record CloudApiConfigSnapshot(
    string BaseUrl,
    string ClientCode,
    string BootstrapSecret,
    string DeviceInstancePath,
    string BootstrapRefreshPath,
    string IdentityDeviceLoginPath,
    string HumanIdentityRefreshPath,
    string DeviceLogPath,
    string ProcessUploadPath,
    string PassStationBatchTemplatePath,
    string CapacityHourlyPath,
    string CapacitySummaryPath,
    string CapacitySummaryRangePath,
    string RecipeByDeviceTemplatePath,
    string ClientReleaseCatalogTemplatePath,
    string ClientVersionReportPath,
    bool Enabled = true,
    string RuntimeHeartbeatPath = "",
    string EdgeHostPlcRuntimeStatesPath = "");

/// <summary>
/// 云端 API 配置快照读取入口，由基础设施层从 appsettings/options 映射。
/// </summary>
public interface ICloudApiConfigSnapshotProvider
{
    CloudApiConfigSnapshot GetCurrent();
}
