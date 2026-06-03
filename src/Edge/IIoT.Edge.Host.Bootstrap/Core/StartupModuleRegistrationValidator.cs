using IIoT.Edge.Application.Abstractions.Modules;
using IIoT.Edge.Application.Modules.Diagnostics;

namespace IIoT.Edge.Shell.Core;

internal sealed class StartupModuleRegistrationValidator(
    ICellDataRegistry cellDataRegistry,
    IStationRuntimeRegistry runtimeRegistry,
    IProcessIntegrationRegistry integrationRegistry)
    : IStartupDiagnosticValidator
{
    public void Validate(StartupValidationContext context, List<StartupDiagnosticIssue> issues)
    {
        foreach (var module in context.ModulesById.Values)
        {
            if (!cellDataRegistry.IsRegistered(module.ProcessType))
            {
                issues.Add(StartupDiagnosticIssueFactory.Create(
                    "CELLDATA_REGISTRATION_MISSING",
                    $"模块“{module.ModuleId}”缺少工序类型“{module.ProcessType}”的 CellData 注册。",
                    module.ModuleId));
            }

            if (!runtimeRegistry.HasFactory(module.ModuleId))
            {
                issues.Add(StartupDiagnosticIssueFactory.Create(
                    "RUNTIME_FACTORY_MISSING",
                    $"模块“{module.ModuleId}”缺少 PLC 运行时工厂注册。",
                    module.ModuleId));
            }

            if (!integrationRegistry.HasCloudUploader(module.ProcessType))
            {
                issues.Add(StartupDiagnosticIssueFactory.Create(
                    "CLOUD_UPLOADER_MISSING",
                    $"模块“{module.ModuleId}”缺少工序类型“{module.ProcessType}”的云端上传器注册。",
                    module.ModuleId));
            }
        }
    }
}
