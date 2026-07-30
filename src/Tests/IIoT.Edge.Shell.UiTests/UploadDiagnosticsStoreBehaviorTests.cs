using IIoT.Edge.Module.Contracts.Cloud;
using IIoT.Edge.Module.Contracts.Mes;
using IIoT.Edge.Shell.Core;

namespace IIoT.Edge.Shell.UiTests;

public sealed class UploadDiagnosticsStoreBehaviorTests
{
    [Fact]
    public void CloudStore_WhenDeviceIsRenamed_ShouldKeepStablePlcCodeAndRefreshDisplayName()
    {
        var store = new CloudUploadDiagnosticsStore();

        store.RecordResult(
            "TestPlugin",
            CloudCallResult.Success(),
            CreateCloudContext(" P1-AP01 ", "旧名称"));
        store.RecordBlocked(
            "TestPlugin",
            "upload_blocked",
            context: CreateCloudContext("P1-AP01", "新名称"));

        Assert.Equal("P1-AP01", store.Snapshot.LastPlcCode);
        Assert.Equal("新名称", store.Snapshot.LastDeviceName);
        Assert.Equal("Task.Upload", store.Snapshot.LastTaskKey);
    }

    [Fact]
    public void MesStore_WhenDeviceIsRenamed_ShouldUpdateOneStableChannel()
    {
        var store = new MesUploadDiagnosticsStore();

        store.RecordSuccess(
            "TestPlugin",
            CreateMesContext("P1-AP01", "旧名称"));
        store.RecordFailure(
            "TestPlugin",
            "timeout",
            CreateMesContext(" P1-AP01 ", "新名称"));

        var channel = Assert.Single(store.GetAll());
        Assert.Equal("P1-AP01", channel.PlcCode);
        Assert.Equal("新名称", channel.DeviceName);
        Assert.Equal("Task.Upload", channel.TaskKey);
        Assert.Equal("Failed", channel.LastResult);
    }

    [Fact]
    public void MesStore_WhenTwoPlcsUseSameProcessAndTask_ShouldKeepChannelsSeparated()
    {
        var store = new MesUploadDiagnosticsStore();

        store.RecordSuccess("TestPlugin", CreateMesContext("P1-AP01", "一号机"));
        store.RecordSuccess("TestPlugin", CreateMesContext("P1-AP02", "二号机"));

        Assert.Equal(2, store.GetAll().Count);
        Assert.Null(store.Get("TestPlugin"));
        Assert.Equal(
            ["P1-AP01", "P1-AP02"],
            store.GetAll().Select(static channel => channel.PlcCode!).Order().ToArray());
    }

    private static CloudUploadDiagnosticsContext CreateCloudContext(string plcCode, string deviceName)
        => new(
            DeviceName: deviceName,
            ModuleId: "TestPlugin",
            TaskKey: "Task.Upload",
            Scenario: "生产上传")
        {
            PlcCode = plcCode
        };

    private static MesUploadDiagnosticsContext CreateMesContext(string plcCode, string deviceName)
        => new(
            DeviceName: deviceName,
            ModuleId: "TestPlugin",
            TaskKey: "Task.Upload",
            Scenario: "生产上传")
        {
            PlcCode = plcCode
        };
}
