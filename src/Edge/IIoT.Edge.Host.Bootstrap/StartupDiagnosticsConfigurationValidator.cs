using IIoT.Edge.Application.Modules.Diagnostics;
using IIoT.Edge.SharedKernel.DataPipeline.Capacity;
using Microsoft.Extensions.Configuration;

namespace IIoT.Edge.Host.Bootstrap;

public interface IStartupDiagnosticsConfigurationValidator
{
    void Validate(
        List<StartupDiagnosticIssue> issues,
        bool cloudUploadEnabled,
        ConfigurationProfileSnapshot configurationProfile);
}

internal sealed class StartupDiagnosticsConfigurationValidator(
    IConfiguration configuration,
    ShiftConfig shiftConfig) : IStartupDiagnosticsConfigurationValidator
{
    public void Validate(List<StartupDiagnosticIssue> issues, bool cloudUploadEnabled, ConfigurationProfileSnapshot configurationProfile)
    {
        if (!cloudUploadEnabled)
        {
            return;
        }

        var baseUrl = configuration["CloudApi:BaseUrl"]?.Trim();
        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            issues.Add(CreateIssue("CONFIG_INVALID", "CloudApi:BaseUrl 未配置。"));
        }
        else if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out _))
        {
            issues.Add(CreateIssue("CONFIG_INVALID", $"CloudApi:BaseUrl 无效：{baseUrl}。"));
        }

        var clientCode = configuration["CloudApi:ClientCode"]?.Trim();
        if (string.IsNullOrWhiteSpace(clientCode))
        {
            issues.Add(CreateIssue("CONFIG_INVALID", "CloudApi:ClientCode 未配置。"));
        }

        var bootstrapSecret = configuration["CloudApi:BootstrapSecret"]?.Trim();
        if (string.IsNullOrWhiteSpace(bootstrapSecret))
        {
            issues.Add(CreateIssue("CONFIG_INVALID", "CloudApi:BootstrapSecret 未配置。"));
        }

        foreach (var key in CloudApiPathKeys)
        {
            ValidateRequiredCloudPath(issues, key);
        }

        var recipePath = configuration["CloudApi:Paths:RecipeByDeviceTemplate"]?.Trim();
        if (!string.IsNullOrWhiteSpace(recipePath)
            && !recipePath.Contains("{deviceId}", StringComparison.OrdinalIgnoreCase))
        {
            issues.Add(CreateIssue(
                "CONFIG_INVALID",
                "CloudApi:Paths:RecipeByDeviceTemplate 必须包含 {deviceId} 占位符。"));
        }

        if (!TimeSpan.TryParse(shiftConfig.DayStart, out var dayStart))
        {
            issues.Add(CreateIssue("CONFIG_INVALID", $"Shift:DayStart 无效：{shiftConfig.DayStart}。"));
        }

        if (!TimeSpan.TryParse(shiftConfig.DayEnd, out var dayEnd))
        {
            issues.Add(CreateIssue("CONFIG_INVALID", $"Shift:DayEnd 无效：{shiftConfig.DayEnd}。"));
        }

        if (TimeSpan.TryParse(shiftConfig.DayStart, out dayStart)
            && TimeSpan.TryParse(shiftConfig.DayEnd, out dayEnd)
            && dayStart == dayEnd)
        {
            issues.Add(CreateIssue("CONFIG_INVALID", "Shift:DayStart 和 Shift:DayEnd 不能相同。"));
        }

        if (!string.IsNullOrWhiteSpace(configurationProfile.MachineProfile)
            && !configurationProfile.IsMachineProfileLoaded)
        {
            issues.Add(CreateIssue(
                "MACHINE_PROFILE_MISSING",
                $"已请求机型配置“{configurationProfile.MachineProfile}”，但文件“{configurationProfile.MachineProfileFileName}”未加载。"));
        }
    }

    private static readonly string[] CloudApiPathKeys =
    [
        "CloudApi:Paths:DeviceInstance",
        "CloudApi:Paths:BootstrapRefresh",
        "CloudApi:Paths:IdentityDeviceLogin",
        "CloudApi:Paths:HumanIdentityRefresh",
        "CloudApi:Paths:DeviceLog",
        "CloudApi:Paths:ProcessUpload",
        "CloudApi:Paths:CapacityHourly",
        "CloudApi:Paths:CapacitySummary",
        "CloudApi:Paths:CapacitySummaryRange",
        "CloudApi:Paths:RecipeByDeviceTemplate"
    ];

    private void ValidateRequiredCloudPath(
        List<StartupDiagnosticIssue> issues,
        string key)
    {
        var configured = configuration[key]?.Trim();
        if (string.IsNullOrWhiteSpace(configured))
        {
            issues.Add(CreateIssue("CONFIG_INVALID", $"{key} 未配置。"));
            return;
        }

        if (Uri.TryCreate(configured, UriKind.Absolute, out _))
        {
            issues.Add(CreateIssue("CONFIG_INVALID", $"{key} 只能填写相对 API 路径，不能填写完整地址。"));
            return;
        }

        if (!configured.StartsWith('/'))
        {
            issues.Add(CreateIssue("CONFIG_INVALID", $"{key} 必须以 / 开头。"));
        }
    }



    private static StartupDiagnosticIssue CreateIssue(
        string code,
        string message,
        string? moduleId = null,
        string? deviceName = null)
        => new(code, message, moduleId, deviceName);
}
