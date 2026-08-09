using IIoT.Edge.Module.Contracts.Diagnostics;
using IIoT.Edge.Module.Contracts.DataPipeline.Capacity;
using IIoT.Edge.SharedKernel.Configuration;
using Microsoft.Extensions.Configuration;

namespace IIoT.Edge.Shell.Core;

internal sealed class StartupAppSettingsValidator(
    IConfiguration configuration,
    ShiftConfig shiftConfig)
    : IStartupDiagnosticValidator
{
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

        foreach (var descriptor in EdgeBindingRouteCatalog.All)
        {
            ValidateRequiredCloudPath(issues, descriptor);
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

    private void ValidateRequiredCloudPath(
        List<StartupDiagnosticIssue> issues,
        EdgeBindingRouteDescriptor descriptor)
    {
        var key = $"CloudApi:Paths:{descriptor.MachineConfigKey}";
        var configured = configuration[key]?.Trim();
        if (string.IsNullOrWhiteSpace(configured))
        {
            issues.Add(StartupDiagnosticIssueFactory.Create("CONFIG_INVALID", $"{key} 未配置。"));
            return;
        }

        try
        {
            _ = EdgeBindingRouteCatalog.ValidateAndNormalize(descriptor.Key, configured);
        }
        catch (InvalidDataException exception)
        {
            issues.Add(StartupDiagnosticIssueFactory.Create(
                "CONFIG_INVALID",
                $"{key} 无效：{exception.Message}"));
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

}
