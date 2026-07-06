using IIoT.Edge.Application.Modules.Diagnostics;
using IIoT.Edge.SharedKernel.DataPipeline.Capacity;
using Microsoft.Extensions.Configuration;

namespace IIoT.Edge.Shell.Core;

internal sealed class StartupAppSettingsValidator(
    IConfiguration configuration,
    ShiftConfig shiftConfig)
    : IStartupDiagnosticValidator
{
    private static readonly string[] CloudApiPathKeys =
    [
        "CloudApi:Paths:DeviceInstance",
        "CloudApi:Paths:BootstrapRefresh",
        "CloudApi:Paths:IdentityDeviceLogin",
        "CloudApi:Paths:HumanIdentityRefresh",
        "CloudApi:Paths:DeviceLog",
        "CloudApi:Paths:ProcessUpload",
        "CloudApi:Paths:PassStationBatchTemplate",
        "CloudApi:Paths:CapacityHourly",
        "CloudApi:Paths:CapacitySummary",
        "CloudApi:Paths:CapacitySummaryRange",
        "CloudApi:Paths:RecipeByDeviceTemplate",
        "CloudApi:Paths:ClientReleaseCatalogTemplate",
        "CloudApi:Paths:ClientVersionReport",
        "CloudApi:Paths:RuntimeHeartbeat",
        "CloudApi:Paths:EdgeHostPlcRuntimeStates"
    ];

    public void Validate(StartupValidationContext context, List<StartupDiagnosticIssue> issues)
    {
        if (!context.SystemCloudEnabled)
        {
            return;
        }

        var baseUrl = configuration["CloudApi:BaseUrl"]?.Trim();
        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            issues.Add(StartupDiagnosticIssueFactory.Create("CONFIG_INVALID", "CloudApi:BaseUrl 未配置。"));
        }
        else if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out _))
        {
            issues.Add(StartupDiagnosticIssueFactory.Create("CONFIG_INVALID", $"CloudApi:BaseUrl 无效：{baseUrl}。"));
        }

        ValidateRequiredValue(issues, "CloudApi:ClientCode", "CloudApi:ClientCode 未配置。");
        ValidateRequiredValue(issues, "CloudApi:BootstrapSecret", "CloudApi:BootstrapSecret 未配置。");

        foreach (var key in CloudApiPathKeys)
        {
            ValidateRequiredCloudPath(issues, key);
        }

        var recipePath = configuration["CloudApi:Paths:RecipeByDeviceTemplate"]?.Trim();
        if (!string.IsNullOrWhiteSpace(recipePath)
            && !recipePath.Contains("{deviceId}", StringComparison.OrdinalIgnoreCase))
        {
            issues.Add(StartupDiagnosticIssueFactory.Create(
                "CONFIG_INVALID",
                "CloudApi:Paths:RecipeByDeviceTemplate 必须包含 {deviceId} 占位符。"));
        }

        var passStationPath = configuration["CloudApi:Paths:PassStationBatchTemplate"]?.Trim();
        if (!string.IsNullOrWhiteSpace(passStationPath)
            && !passStationPath.Contains("{typeKey}", StringComparison.OrdinalIgnoreCase))
        {
            issues.Add(StartupDiagnosticIssueFactory.Create(
                "CONFIG_INVALID",
                "CloudApi:Paths:PassStationBatchTemplate 必须包含 {typeKey} 占位符。"));
        }

        var clientReleaseCatalogPath = configuration["CloudApi:Paths:ClientReleaseCatalogTemplate"]?.Trim();
        if (!string.IsNullOrWhiteSpace(clientReleaseCatalogPath)
            && !clientReleaseCatalogPath.Contains("{deviceId}", StringComparison.OrdinalIgnoreCase))
        {
            issues.Add(StartupDiagnosticIssueFactory.Create(
                "CONFIG_INVALID",
                "CloudApi:Paths:ClientReleaseCatalogTemplate 必须包含 {deviceId} 占位符。"));
        }

        ValidateShiftWindow(issues);
        ValidateMachineProfile(context, issues);
    }

    private void ValidateRequiredValue(List<StartupDiagnosticIssue> issues, string key, string message)
    {
        if (string.IsNullOrWhiteSpace(configuration[key]?.Trim()))
        {
            issues.Add(StartupDiagnosticIssueFactory.Create("CONFIG_INVALID", message));
        }
    }

    private void ValidateRequiredCloudPath(List<StartupDiagnosticIssue> issues, string key)
    {
        var configured = configuration[key]?.Trim();
        if (string.IsNullOrWhiteSpace(configured))
        {
            issues.Add(StartupDiagnosticIssueFactory.Create("CONFIG_INVALID", $"{key} 未配置。"));
            return;
        }

        if (HasExplicitScheme(configured))
        {
            issues.Add(StartupDiagnosticIssueFactory.Create("CONFIG_INVALID", $"{key} 只能填写相对 API 路径，不能填写完整地址。"));
            return;
        }

        if (configured.StartsWith("//", StringComparison.Ordinal))
        {
            issues.Add(StartupDiagnosticIssueFactory.Create("CONFIG_INVALID", $"{key} 必须是以单个 / 开头的相对 API 路径。"));
            return;
        }

        if (!configured.StartsWith('/'))
        {
            issues.Add(StartupDiagnosticIssueFactory.Create("CONFIG_INVALID", $"{key} 必须以 / 开头。"));
        }
    }

    private void ValidateShiftWindow(List<StartupDiagnosticIssue> issues)
    {
        if (!TimeSpan.TryParse(shiftConfig.DayStart, out var dayStart))
        {
            issues.Add(StartupDiagnosticIssueFactory.Create("CONFIG_INVALID", $"Shift:DayStart 无效：{shiftConfig.DayStart}。"));
        }

        if (!TimeSpan.TryParse(shiftConfig.DayEnd, out var dayEnd))
        {
            issues.Add(StartupDiagnosticIssueFactory.Create("CONFIG_INVALID", $"Shift:DayEnd 无效：{shiftConfig.DayEnd}。"));
        }

        if (TimeSpan.TryParse(shiftConfig.DayStart, out dayStart)
            && TimeSpan.TryParse(shiftConfig.DayEnd, out dayEnd)
            && dayStart == dayEnd)
        {
            issues.Add(StartupDiagnosticIssueFactory.Create("CONFIG_INVALID", "Shift:DayStart 和 Shift:DayEnd 不能相同。"));
        }
    }

    private static void ValidateMachineProfile(StartupValidationContext context, List<StartupDiagnosticIssue> issues)
    {
        var configurationProfile = context.ConfigurationProfile;
        if (!string.IsNullOrWhiteSpace(configurationProfile.MachineProfile)
            && !configurationProfile.IsMachineProfileLoaded)
        {
            issues.Add(StartupDiagnosticIssueFactory.Create(
                "MACHINE_PROFILE_MISSING",
                $"已请求机型配置“{configurationProfile.MachineProfile}”，但文件“{configurationProfile.MachineProfileFileName}”未加载。"));
        }
    }

    private static bool HasExplicitScheme(string value)
    {
        var colonIndex = value.IndexOf(':');
        if (colonIndex <= 0)
        {
            return false;
        }

        var boundaryIndex = value.IndexOfAny(['/', '\\', '?', '#']);
        return (boundaryIndex < 0 || colonIndex < boundaryIndex)
            && Uri.CheckSchemeName(value[..colonIndex]);
    }
}
