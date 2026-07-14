using IIoT.Edge.Application.Abstractions.DataPipeline;
using IIoT.Edge.Application.Common.DataPipeline;
using IIoT.Edge.SharedKernel.DataPipeline.CellData;

namespace IIoT.Edge.Tests.NonUiRegression;

public sealed class DataPipelineUploadHelperBehaviorTests
{
    [Fact]
    public void UploadTargetPolicy_ShouldResolveMesCloudSwitches()
    {
        Assert.Equal(DataPipelineUploadTargets.None, DataPipelineUploadTargetPolicy.Resolve(false, false));
        Assert.Equal(DataPipelineUploadTargets.Mes, DataPipelineUploadTargetPolicy.Resolve(true, false));
        Assert.Equal(DataPipelineUploadTargets.Cloud, DataPipelineUploadTargetPolicy.Resolve(false, true));
        Assert.Equal(DataPipelineUploadTargets.All, DataPipelineUploadTargetPolicy.Resolve(true, true));
    }

    [Theory]
    [InlineData("TestPlugin.DeviceStatus", null, null, "设备状态上传")]
    [InlineData("Homogenization.EquipmentStatus", null, null, "设备状态上传")]
    [InlineData("TestPlugin.RealtimeSample", null, null, "生产上传")]
    [InlineData(null, "RealtimeOutbound", null, "生产上传")]
    [InlineData(null, "Inbound", null, "进站上传")]
    [InlineData(null, "Outbound", null, "出站上传")]
    [InlineData(null, "Recipe", null, "配方上传")]
    public void UploadScenarioResolver_ShouldMapKnownTaskAndRecordKinds(
        string? taskKey,
        string? recordKind,
        string? processType,
        string expected)
    {
        Assert.Equal(expected, DataPipelineUploadScenarioResolver.Resolve(taskKey, recordKind, processType));
    }
}
