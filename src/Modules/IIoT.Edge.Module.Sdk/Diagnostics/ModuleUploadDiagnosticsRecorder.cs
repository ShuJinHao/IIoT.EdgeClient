using IIoT.Edge.Application.Abstractions.Cloud;
using IIoT.Edge.Application.Abstractions.Mes;
using IIoT.Edge.SharedKernel.DataPipeline.CellData;

namespace IIoT.Edge.Module.Sdk.Diagnostics;

/// <summary>
/// 模块上传诊断所需的稳定业务上下文。
/// </summary>
public readonly record struct ModuleUploadDiagnosticsIdentity(
    string DeviceName,
    string ModuleId,
    string TaskKey,
    string Scenario);

/// <summary>
/// MES 与 Cloud 各自独立的诊断路由和原因码。
/// </summary>
public readonly record struct ModuleUploadDiagnosticsRoute(
    string MesChannel,
    string CloudProcessType,
    string CloudBlockedReasonCode,
    string CloudFailureReasonCode);

/// <summary>
/// 按显式上传目标记录模块 MES/Cloud 诊断，不持有 store、重试或业务状态。
/// </summary>
public static class ModuleUploadDiagnosticsRecorder
{
    /// <summary>
    /// 按 MES 结果分类记录 blocked 或 failure；成功和禁用结果不产生失败诊断。
    /// </summary>
    public static void RecordResult(
        MesCallResult result,
        DataPipelineUploadTargets uploadTargets,
        IMesUploadDiagnosticsStore? mesDiagnosticsStore,
        ICloudUploadDiagnosticsStore? cloudDiagnosticsStore,
        ModuleUploadDiagnosticsRoute route,
        ModuleUploadDiagnosticsIdentity identity)
    {
        ArgumentNullException.ThrowIfNull(result);

        if (result.IsSuccess)
        {
            return;
        }

        if (result.Outcome is MesCallOutcome.BusinessRejected or MesCallOutcome.InvalidContext)
        {
            RecordBlocked(
                result.Message,
                uploadTargets,
                mesDiagnosticsStore,
                cloudDiagnosticsStore,
                route,
                identity);
            return;
        }

        RecordFailure(
            result.Message,
            uploadTargets,
            mesDiagnosticsStore,
            cloudDiagnosticsStore,
            route,
            identity);
    }

    /// <summary>
    /// 对显式目标记录失败；未选择的目标不会被写入。
    /// </summary>
    public static void RecordFailure(
        string message,
        DataPipelineUploadTargets uploadTargets,
        IMesUploadDiagnosticsStore? mesDiagnosticsStore,
        ICloudUploadDiagnosticsStore? cloudDiagnosticsStore,
        ModuleUploadDiagnosticsRoute route,
        ModuleUploadDiagnosticsIdentity identity)
    {
        if (uploadTargets == DataPipelineUploadTargets.None)
        {
            return;
        }

        if (uploadTargets.HasFlag(DataPipelineUploadTargets.Mes))
        {
            ArgumentNullException.ThrowIfNull(mesDiagnosticsStore);
            mesDiagnosticsStore.RecordFailure(
                route.MesChannel,
                message,
                CreateMesContext(identity));
        }

        if (uploadTargets.HasFlag(DataPipelineUploadTargets.Cloud))
        {
            ArgumentNullException.ThrowIfNull(cloudDiagnosticsStore);
            cloudDiagnosticsStore.RecordResult(
                route.CloudProcessType,
                CloudCallResult.Failure(CloudCallOutcome.Exception, route.CloudFailureReasonCode),
                CreateCloudContext(identity));
        }
    }

    /// <summary>
    /// 对显式目标记录阻断；未选择的目标不会被写入。
    /// </summary>
    public static void RecordBlocked(
        string message,
        DataPipelineUploadTargets uploadTargets,
        IMesUploadDiagnosticsStore? mesDiagnosticsStore,
        ICloudUploadDiagnosticsStore? cloudDiagnosticsStore,
        ModuleUploadDiagnosticsRoute route,
        ModuleUploadDiagnosticsIdentity identity)
    {
        if (uploadTargets == DataPipelineUploadTargets.None)
        {
            return;
        }

        if (uploadTargets.HasFlag(DataPipelineUploadTargets.Mes))
        {
            ArgumentNullException.ThrowIfNull(mesDiagnosticsStore);
            mesDiagnosticsStore.RecordBlocked(
                route.MesChannel,
                message,
                CreateMesContext(identity));
        }

        if (uploadTargets.HasFlag(DataPipelineUploadTargets.Cloud))
        {
            ArgumentNullException.ThrowIfNull(cloudDiagnosticsStore);
            cloudDiagnosticsStore.RecordBlocked(
                route.CloudProcessType,
                route.CloudBlockedReasonCode,
                message,
                CreateCloudContext(identity));
        }
    }

    private static MesUploadDiagnosticsContext CreateMesContext(ModuleUploadDiagnosticsIdentity identity)
        => new(
            DeviceName: identity.DeviceName,
            ModuleId: identity.ModuleId,
            TaskKey: identity.TaskKey,
            Scenario: identity.Scenario);

    private static CloudUploadDiagnosticsContext CreateCloudContext(ModuleUploadDiagnosticsIdentity identity)
        => new(
            DeviceName: identity.DeviceName,
            ModuleId: identity.ModuleId,
            TaskKey: identity.TaskKey,
            Scenario: identity.Scenario);
}
