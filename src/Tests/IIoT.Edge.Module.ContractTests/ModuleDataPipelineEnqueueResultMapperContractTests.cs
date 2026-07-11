using IIoT.Edge.Application.Abstractions.DataPipeline;
using IIoT.Edge.Application.Abstractions.Mes;
using IIoT.Edge.Module.Sdk.DataPipeline;
using IIoT.Edge.SharedKernel.DataPipeline.CellData;

namespace IIoT.Edge.Module.ContractTests;

public sealed class ModuleDataPipelineEnqueueResultMapperContractTests
{
    [Fact]
    public void ToQueuedUploadResult_ShouldPreserveAcceptedOverflowAndRejectedSemantics()
    {
        AssertResult(
            ModuleDataPipelineEnqueueResultMapper.ToQueuedUploadResult(
                DataPipelineEnqueueResult.Accepted(),
                "实时数据",
                DataPipelineUploadTargets.All),
            MesCallOutcome.Success,
            "实时数据已进入 MES/Cloud 上传队列。");
        AssertResult(
            ModuleDataPipelineEnqueueResultMapper.ToQueuedUploadResult(
                DataPipelineEnqueueResult.OverflowPersisted(1, 0),
                "实时数据",
                DataPipelineUploadTargets.All),
            MesCallOutcome.Success,
            "实时数据已接收，数据已进入溢出持久化。");
        AssertResult(
            ModuleDataPipelineEnqueueResultMapper.ToQueuedUploadResult(
                DataPipelineEnqueueResult.Rejected("queue_full"),
                "实时数据",
                DataPipelineUploadTargets.All),
            MesCallOutcome.TransportFailure,
            "实时数据未接收，数据管道拒绝入队（queue_full）。");
        AssertResult(
            ModuleDataPipelineEnqueueResultMapper.ToQueuedUploadResult(
                DataPipelineEnqueueResult.Rejected(" "),
                "实时数据",
                DataPipelineUploadTargets.All),
            MesCallOutcome.TransportFailure,
            "实时数据未接收，数据管道拒绝入队（unknown）。");
    }

    [Fact]
    public void ToPendingBackgroundUploadResult_ShouldPreserveAcceptedOverflowAndRejectedSemantics()
    {
        AssertResult(
            ModuleDataPipelineEnqueueResultMapper.ToPendingBackgroundUploadResult(
                DataPipelineEnqueueResult.Accepted(),
                "模切采样",
                DataPipelineUploadTargets.Cloud),
            MesCallOutcome.Success,
            "模切采样已进入 Cloud 上传队列，等待后台上传。");
        AssertResult(
            ModuleDataPipelineEnqueueResultMapper.ToPendingBackgroundUploadResult(
                DataPipelineEnqueueResult.OverflowPersisted(1, 0),
                "模切采样",
                DataPipelineUploadTargets.Cloud),
            MesCallOutcome.Success,
            "模切采样已进入溢出补偿，等待后台上传。");
        AssertResult(
            ModuleDataPipelineEnqueueResultMapper.ToPendingBackgroundUploadResult(
                DataPipelineEnqueueResult.Rejected("queue_full"),
                "模切采样",
                DataPipelineUploadTargets.Cloud),
            MesCallOutcome.TransportFailure,
            "模切采样未进入上传队列，原因=queue_full。");
        AssertResult(
            ModuleDataPipelineEnqueueResultMapper.ToPendingBackgroundUploadResult(
                DataPipelineEnqueueResult.Rejected(string.Empty),
                "模切采样",
                DataPipelineUploadTargets.Cloud),
            MesCallOutcome.TransportFailure,
            "模切采样未进入上传队列，原因=unknown。");
    }

    private static void AssertResult(MesCallResult result, MesCallOutcome outcome, string message)
    {
        Assert.Equal(outcome, result.Outcome);
        Assert.Equal(message, result.Message);
    }
}
