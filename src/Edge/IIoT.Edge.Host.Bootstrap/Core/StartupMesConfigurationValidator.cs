using IIoT.Edge.Module.Contracts.Config;
using IIoT.Edge.Module.Contracts.Diagnostics;

namespace IIoT.Edge.Shell.Core;

internal sealed class StartupMesConfigurationValidator(
    IModuleParamRoleProvider roleProvider)
    : IStartupAsyncDiagnosticValidator
{
    private static readonly (ModuleParamRole Role, string DisplayName)[] RequiredRoles =
    [
        (ModuleParamRole.MesBaseUrl, "服务地址"),
        (ModuleParamRole.MesSignToken, "签名令牌"),
        (ModuleParamRole.StationNo, "工站编号"),
        (ModuleParamRole.MesUpperComputerNo, "上位机编码"),
        (ModuleParamRole.MesOperationCode, "工序编码")
    ];

    public async Task ValidateAsync(
        StartupValidationContext context,
        List<StartupDiagnosticIssue> issues,
        CancellationToken cancellationToken)
    {
        foreach (var module in context.ModulesById.Values
                     .Where(static module => module.RequiresMesUploader)
                     .OrderBy(static module => module.ModuleId, StringComparer.OrdinalIgnoreCase))
        {
            var enabled = await roleProvider
                .GetMesBoolAsync(
                    module.ModuleId,
                    ModuleParamRole.MesEnabled,
                    defaultValue: false,
                    cancellationToken)
                .ConfigureAwait(false);
            if (!enabled)
            {
                continue;
            }

            var missing = new List<string>();
            foreach (var required in RequiredRoles)
            {
                var value = await roleProvider
                    .GetAsync(
                        module.ModuleId,
                        ModuleParamCategory.Mes,
                        required.Role,
                        cancellationToken)
                    .ConfigureAwait(false);
                if (string.IsNullOrWhiteSpace(value?.Value))
                {
                    missing.Add(required.DisplayName);
                }
            }

            if (missing.Count == 0)
            {
                continue;
            }

            issues.Add(StartupDiagnosticIssueFactory.Create(
                "MES_CONFIGURATION_INCOMPLETE",
                $"MES 已启用但缺少：{string.Join('、', missing)}；仅 MES 任务被阻断，Shell 与 PLC 基础运行继续。",
                module.ModuleId));
        }
    }
}
