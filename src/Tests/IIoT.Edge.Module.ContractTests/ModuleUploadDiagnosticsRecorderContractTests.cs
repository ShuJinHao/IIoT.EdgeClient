using IIoT.Edge.Application.Abstractions.Cloud;
using IIoT.Edge.Application.Abstractions.Mes;
using IIoT.Edge.Application.Abstractions.Shared;
using IIoT.Edge.Module.Sdk.Diagnostics;
using IIoT.Edge.SharedKernel.DataPipeline.CellData;

namespace IIoT.Edge.Module.ContractTests;

public sealed class ModuleUploadDiagnosticsRecorderContractTests
{
    private static readonly ModuleUploadDiagnosticsRoute Route = new(
        MesChannel: "MES.Channel",
        CloudProcessType: "Cloud.Process",
        CloudBlockedReasonCode: "cloud_blocked",
        CloudFailureReasonCode: "cloud_failed");

    private static readonly ModuleUploadDiagnosticsIdentity Identity = new(
        DeviceName: "PLC-01",
        ModuleId: "Module-A",
        TaskKey: "Module-A.Upload",
        Scenario: "生产上传");

    [Theory]
    [InlineData(MesCallOutcome.BusinessRejected, DataPipelineUploadTargets.None)]
    [InlineData(MesCallOutcome.BusinessRejected, DataPipelineUploadTargets.Mes)]
    [InlineData(MesCallOutcome.BusinessRejected, DataPipelineUploadTargets.Cloud)]
    [InlineData(MesCallOutcome.BusinessRejected, DataPipelineUploadTargets.All)]
    [InlineData(MesCallOutcome.InvalidContext, DataPipelineUploadTargets.None)]
    [InlineData(MesCallOutcome.InvalidContext, DataPipelineUploadTargets.Mes)]
    [InlineData(MesCallOutcome.InvalidContext, DataPipelineUploadTargets.Cloud)]
    [InlineData(MesCallOutcome.InvalidContext, DataPipelineUploadTargets.All)]
    public void RecordResult_WhenOutcomeIsBlocked_ShouldRouteOnlyToSelectedTargets(
        MesCallOutcome outcome,
        DataPipelineUploadTargets uploadTargets)
    {
        var mes = new CapturingMesDiagnosticsStore();
        var cloud = new CapturingCloudDiagnosticsStore();
        var result = new MesCallResult(outcome, "业务前置条件未满足");

        ModuleUploadDiagnosticsRecorder.RecordResult(
            result,
            uploadTargets,
            mes,
            cloud,
            Route,
            Identity);

        Assert.Equal(uploadTargets.HasFlag(DataPipelineUploadTargets.Mes) ? 1 : 0, mes.Calls.Count);
        Assert.Equal(uploadTargets.HasFlag(DataPipelineUploadTargets.Cloud) ? 1 : 0, cloud.Calls.Count);
        if (uploadTargets.HasFlag(DataPipelineUploadTargets.Mes))
        {
            var call = Assert.Single(mes.Calls);
            Assert.Equal("Blocked", call.Kind);
            Assert.Equal(Route.MesChannel, call.Channel);
            Assert.Equal("业务前置条件未满足", call.Message);
            AssertMesContext(call.Context);
        }

        if (uploadTargets.HasFlag(DataPipelineUploadTargets.Cloud))
        {
            var call = Assert.Single(cloud.Calls);
            Assert.Equal("Blocked", call.Kind);
            Assert.Equal(Route.CloudProcessType, call.ProcessType);
            Assert.Equal(Route.CloudBlockedReasonCode, call.ReasonCode);
            Assert.Equal("业务前置条件未满足", call.Message);
            AssertCloudContext(call.Context);
        }
    }

    [Theory]
    [InlineData(DataPipelineUploadTargets.None)]
    [InlineData(DataPipelineUploadTargets.Mes)]
    [InlineData(DataPipelineUploadTargets.Cloud)]
    [InlineData(DataPipelineUploadTargets.All)]
    public void RecordResult_WhenTransportFails_ShouldRouteFailureOnlyToSelectedTargets(
        DataPipelineUploadTargets uploadTargets)
    {
        var mes = new CapturingMesDiagnosticsStore();
        var cloud = new CapturingCloudDiagnosticsStore();

        ModuleUploadDiagnosticsRecorder.RecordResult(
            MesCallResult.TransportFailure("队列异常"),
            uploadTargets,
            mes,
            cloud,
            Route,
            Identity);

        Assert.Equal(uploadTargets.HasFlag(DataPipelineUploadTargets.Mes) ? 1 : 0, mes.Calls.Count);
        Assert.Equal(uploadTargets.HasFlag(DataPipelineUploadTargets.Cloud) ? 1 : 0, cloud.Calls.Count);
        if (uploadTargets.HasFlag(DataPipelineUploadTargets.Mes))
        {
            var call = Assert.Single(mes.Calls);
            Assert.Equal("Failed", call.Kind);
            Assert.Equal(Route.MesChannel, call.Channel);
            Assert.Equal("队列异常", call.Message);
            AssertMesContext(call.Context);
        }

        if (uploadTargets.HasFlag(DataPipelineUploadTargets.Cloud))
        {
            var call = Assert.Single(cloud.Calls);
            Assert.Equal("Failed", call.Kind);
            Assert.Equal(Route.CloudProcessType, call.ProcessType);
            Assert.Equal(Route.CloudFailureReasonCode, call.ReasonCode);
            Assert.Equal(CloudCallOutcome.Exception, call.Outcome);
            AssertCloudContext(call.Context);
        }
    }

    [Theory]
    [InlineData(MesCallOutcome.Success)]
    [InlineData(MesCallOutcome.Disabled)]
    public void RecordResult_WhenSuccessfulOrDisabled_ShouldNotWriteAnyDiagnostics(MesCallOutcome outcome)
    {
        var mes = new CapturingMesDiagnosticsStore();
        var cloud = new CapturingCloudDiagnosticsStore();

        ModuleUploadDiagnosticsRecorder.RecordResult(
            new MesCallResult(outcome, "无需记录"),
            DataPipelineUploadTargets.All,
            mes,
            cloud,
            Route,
            Identity);

        Assert.Empty(mes.Calls);
        Assert.Empty(cloud.Calls);
    }

    [Fact]
    public void ExplicitRecordMethods_ShouldNotRequireOrTouchUnselectedStore()
    {
        var mes = new CapturingMesDiagnosticsStore();
        var cloud = new CapturingCloudDiagnosticsStore();

        ModuleUploadDiagnosticsRecorder.RecordFailure(
            "MES 失败",
            DataPipelineUploadTargets.Mes,
            mes,
            cloudDiagnosticsStore: null,
            Route,
            Identity);
        ModuleUploadDiagnosticsRecorder.RecordBlocked(
            "Cloud 阻断",
            DataPipelineUploadTargets.Cloud,
            mesDiagnosticsStore: null,
            cloud,
            Route,
            Identity);
        ModuleUploadDiagnosticsRecorder.RecordFailure(
            "无目标",
            DataPipelineUploadTargets.None,
            mesDiagnosticsStore: null,
            cloudDiagnosticsStore: null,
            route: default,
            identity: default);

        Assert.Equal("Failed", Assert.Single(mes.Calls).Kind);
        Assert.Equal("Blocked", Assert.Single(cloud.Calls).Kind);
    }

    private static void AssertMesContext(MesUploadDiagnosticsContext? context)
    {
        Assert.NotNull(context);
        Assert.Equal(Identity.DeviceName, context.DeviceName);
        Assert.Equal(Identity.ModuleId, context.ModuleId);
        Assert.Equal(Identity.TaskKey, context.TaskKey);
        Assert.Equal(Identity.Scenario, context.Scenario);
    }

    private static void AssertCloudContext(CloudUploadDiagnosticsContext? context)
    {
        Assert.NotNull(context);
        Assert.Equal(Identity.DeviceName, context.DeviceName);
        Assert.Equal(Identity.ModuleId, context.ModuleId);
        Assert.Equal(Identity.TaskKey, context.TaskKey);
        Assert.Equal(Identity.Scenario, context.Scenario);
    }
}

file sealed record MesDiagnosticsCall(
    string Kind,
    string Channel,
    string? Message,
    MesUploadDiagnosticsContext? Context);

file sealed class CapturingMesDiagnosticsStore : IMesUploadDiagnosticsStore
{
    public List<MesDiagnosticsCall> Calls { get; } = [];

    public IReadOnlyList<MesChannelDiagnostics> GetAll() => [];

    public MesChannelDiagnostics? Get(string processType) => null;

    public void RecordSuccess(string processType, MesUploadDiagnosticsContext? context = null)
        => Calls.Add(new MesDiagnosticsCall("Success", processType, null, context));

    public void RecordFailure(
        string processType,
        string failureReason,
        MesUploadDiagnosticsContext? context = null)
        => Calls.Add(new MesDiagnosticsCall("Failed", processType, failureReason, context));

    public void RecordBlocked(
        string processType,
        string blockedReason,
        MesUploadDiagnosticsContext? context = null)
        => Calls.Add(new MesDiagnosticsCall("Blocked", processType, blockedReason, context));
}

file sealed record CloudDiagnosticsCall(
    string Kind,
    string? ProcessType,
    string ReasonCode,
    string? Message,
    CloudCallOutcome Outcome,
    CloudUploadDiagnosticsContext? Context);

file sealed class CapturingCloudDiagnosticsStore : ICloudUploadDiagnosticsStore
{
    public List<CloudDiagnosticsCall> Calls { get; } = [];

    public CloudUploadDiagnosticsSnapshot Snapshot { get; } = new(
        LastAttemptAt: null,
        LastSuccessAt: null,
        LastFailureAt: null,
        LastBlockedAt: null,
        LastOutcome: CloudCallOutcome.Success,
        LastReasonCode: "none",
        LastBlockedReason: null,
        LastProcessType: null,
        RuntimeState: CloudRetryRuntimeState.Idle,
        IsCapacityBlocked: false,
        BlockedChannel: null,
        BlockedReason: "none",
        LastCapacityBlockAt: null);

    public void RecordResult(
        string? processType,
        CloudCallResult result,
        CloudUploadDiagnosticsContext? context = null)
        => Calls.Add(new CloudDiagnosticsCall(
            "Failed",
            processType,
            result.ReasonCode,
            null,
            result.Outcome,
            context));

    public void RecordBlocked(
        string? processType,
        string reasonCode,
        string? blockedReason = null,
        CloudUploadDiagnosticsContext? context = null)
        => Calls.Add(new CloudDiagnosticsCall(
            "Blocked",
            processType,
            reasonCode,
            blockedReason,
            CloudCallOutcome.SkippedUploadNotReady,
            context));

    public void SetRuntimeState(CloudRetryRuntimeState state) { }

    public void MarkCapacityBlocked(
        CapacityBlockedChannel channel,
        string blockedReason,
        string? processType = null,
        DateTime? occurredAt = null) { }

    public void ClearCapacityBlocked() { }
}
