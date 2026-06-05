using IIoT.Edge.Application.Abstractions.Config;
using IIoT.Edge.Application.Abstractions.DataPipeline;
using IIoT.Edge.Application.Abstractions.Device;
using IIoT.Edge.Application.Abstractions.Logging;
using IIoT.Edge.Application.Abstractions.Modules;
using IIoT.Edge.Application.Abstractions.Time;
using IIoT.Edge.Application.Modules.Hardware;
using IIoT.Edge.Infrastructure.DeviceComm.Plc.Store;
using IIoT.Edge.Infrastructure.Integration.DeviceLog;
using IIoT.Edge.Module.Homogenization;
using IIoT.Edge.Module.Homogenization.Config;
using IIoT.Edge.Module.Homogenization.Integration;
using IIoT.Edge.Module.Homogenization.Payload;
using IIoT.Edge.Module.Homogenization.Runtime;
using IIoT.Edge.Module.Sdk.Signals;
using IIoT.Edge.SharedKernel.DataPipeline;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

using IIoT.Edge.Application.Abstractions.Cloud;
using IIoT.Edge.Application.Abstractions.Mes;
namespace IIoT.Edge.NonUiRegressionTests;

public sealed class HomogenizationBusinessChainBehaviorTests
{
    [Fact]
    public async Task Inbound_WhenTrayCodeIsEmpty_ShouldAckExceptionAndNotCallMes()
    {
        await using var harness = HomogenizationRuntimeHarness.Create();
        await harness.StartAsync();

        harness.SetAscii(HomogenizationSignalTestProfile.SignalKey(HomogenizationPlcSignals.ContinuousRead.托盘码), string.Empty, 30);
        harness.SetWord(HomogenizationSignalTestProfile.SignalKey(HomogenizationPlcSignals.Interaction.扫码进站), TestCodeOptions.Plc.SignalTrigger);

        await WaitUntilAsync(() => harness.Context.GetStep("Homogenization.Inbound") == 30);

        Assert.Empty(harness.Mes.InboundTrayCodes);
        Assert.Equal(TestCodeOptions.Plc.AckException, harness.ReadWriteWord(HomogenizationSignalTestProfile.SignalKey(HomogenizationPlcSignals.Interaction.扫码进站)));
        Assert.Equal(string.Empty, harness.Context.LastInboundTrayCode);
        Assert.Contains("托盘码不能为空", harness.Context.LastInboundResult, StringComparison.Ordinal);
        Assert.Equal("Failed", harness.Diagnostics.Get(TestCodeOptions.Mes.Channels.Inbound)!.LastResult);
    }

    [Fact]
    public async Task Inbound_WhenMesRejects_ShouldAckMesNgAndRecordFailure()
    {
        await using var harness = HomogenizationRuntimeHarness.Create();
        harness.Mes.InboundResult = MesCallResult.BusinessRejected("MES 拒绝进站。");
        await harness.StartAsync();

        harness.SetAscii(HomogenizationSignalTestProfile.SignalKey(HomogenizationPlcSignals.ContinuousRead.托盘码), "TRAY-MES-NG", 30);
        harness.SetWord(HomogenizationSignalTestProfile.SignalKey(HomogenizationPlcSignals.Interaction.扫码进站), TestCodeOptions.Plc.SignalTrigger);

        await WaitUntilAsync(() => harness.Context.GetStep("Homogenization.Inbound") == 30);

        Assert.Contains("TRAY-MES-NG", harness.Mes.InboundTrayCodes);
        Assert.Equal(TestCodeOptions.Plc.AckMesNg, harness.ReadWriteWord(HomogenizationSignalTestProfile.SignalKey(HomogenizationPlcSignals.Interaction.扫码进站)));
        Assert.Equal("TRAY-MES-NG", harness.Context.LastInboundTrayCode);
        Assert.Equal("MES 拒绝进站。", harness.Context.LastInboundResult);
        Assert.Equal("MES 拒绝进站。", harness.Diagnostics.Get(TestCodeOptions.Mes.Channels.Inbound)!.LastFailureReason);

        harness.SetWord(HomogenizationSignalTestProfile.SignalKey(HomogenizationPlcSignals.Interaction.扫码进站), TestCodeOptions.Plc.SignalReset);
        await WaitUntilAsync(() => harness.Context.GetStep("Homogenization.Inbound") == 0);

        Assert.Equal(TestCodeOptions.Plc.SignalReset, harness.ReadWriteWord(HomogenizationSignalTestProfile.SignalKey(HomogenizationPlcSignals.Interaction.扫码进站)));
    }

    [Fact]
    public async Task Inbound_WhenMesThrows_ShouldAckExceptionAndRecordFailure()
    {
        await using var harness = HomogenizationRuntimeHarness.Create();
        harness.Mes.InboundException = new InvalidOperationException("MES 通信异常");
        await harness.StartAsync();

        harness.SetAscii(HomogenizationSignalTestProfile.SignalKey(HomogenizationPlcSignals.ContinuousRead.托盘码), "TRAY-MES-EX", 30);
        harness.SetWord(HomogenizationSignalTestProfile.SignalKey(HomogenizationPlcSignals.Interaction.扫码进站), TestCodeOptions.Plc.SignalTrigger);

        await WaitUntilAsync(() => harness.Context.GetStep("Homogenization.Inbound") == 30);

        Assert.Equal(TestCodeOptions.Plc.AckException, harness.ReadWriteWord(HomogenizationSignalTestProfile.SignalKey(HomogenizationPlcSignals.Interaction.扫码进站)));
        Assert.Equal(string.Empty, harness.Context.LastInboundTrayCode);
        Assert.Contains("MES 通信异常", harness.Context.LastInboundResult, StringComparison.Ordinal);
        Assert.Contains("MES 通信异常", harness.Diagnostics.Get(TestCodeOptions.Mes.Channels.Inbound)!.LastFailureReason, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Inbound_WhenDuplicateCheckDisabled_ShouldAllowRepeatedTrayCode()
    {
        await using var harness = HomogenizationRuntimeHarness.Create(duplicateCheckEnabled: false);
        await harness.StartAsync();

        harness.SetAscii(HomogenizationSignalTestProfile.SignalKey(HomogenizationPlcSignals.ContinuousRead.托盘码), "TRAY-DUP-OFF", 30);
        harness.SetWord(HomogenizationSignalTestProfile.SignalKey(HomogenizationPlcSignals.Interaction.扫码进站), TestCodeOptions.Plc.SignalTrigger);
        await WaitUntilAsync(() => harness.Context.GetStep("Homogenization.Inbound") == 30);

        harness.SetWord(HomogenizationSignalTestProfile.SignalKey(HomogenizationPlcSignals.Interaction.扫码进站), TestCodeOptions.Plc.SignalReset);
        await WaitUntilAsync(() => harness.Context.GetStep("Homogenization.Inbound") == 0);

        harness.SetWord(HomogenizationSignalTestProfile.SignalKey(HomogenizationPlcSignals.Interaction.扫码进站), TestCodeOptions.Plc.SignalTrigger);
        await WaitUntilAsync(() => harness.Mes.InboundTrayCodes.Count == 2);
        await WaitUntilAsync(() => harness.ReadWriteWord(HomogenizationSignalTestProfile.SignalKey(HomogenizationPlcSignals.Interaction.扫码进站)) == TestCodeOptions.Plc.AckOk);

        Assert.Equal(["TRAY-DUP-OFF", "TRAY-DUP-OFF"], harness.Mes.InboundTrayCodes);
        Assert.Equal(TestCodeOptions.Plc.AckOk, harness.ReadWriteWord(HomogenizationSignalTestProfile.SignalKey(HomogenizationPlcSignals.Interaction.扫码进站)));
    }

    [Fact]
    public async Task Inbound_WhenDuplicateCheckEnabled_ShouldAckMesNgAndNotCallMesAgain()
    {
        await using var harness = HomogenizationRuntimeHarness.Create(duplicateCheckEnabled: true);
        await harness.StartAsync();

        harness.SetAscii(HomogenizationSignalTestProfile.SignalKey(HomogenizationPlcSignals.ContinuousRead.托盘码), "TRAY-DUP-IN", 30);
        harness.SetWord(HomogenizationSignalTestProfile.SignalKey(HomogenizationPlcSignals.Interaction.扫码进站), TestCodeOptions.Plc.SignalTrigger);
        await WaitUntilAsync(() => harness.Context.GetStep("Homogenization.Inbound") == 30);

        harness.SetWord(HomogenizationSignalTestProfile.SignalKey(HomogenizationPlcSignals.Interaction.扫码进站), TestCodeOptions.Plc.SignalReset);
        await WaitUntilAsync(() => harness.Context.GetStep("Homogenization.Inbound") == 0);

        harness.SetWord(HomogenizationSignalTestProfile.SignalKey(HomogenizationPlcSignals.Interaction.扫码进站), TestCodeOptions.Plc.SignalTrigger);
        await WaitUntilAsync(() => harness.Context.LastInboundResult?.Contains("托盘码重复", StringComparison.Ordinal) == true);

        Assert.Single(harness.Mes.InboundTrayCodes);
        Assert.Equal(TestCodeOptions.Plc.AckMesNg, harness.ReadWriteWord(HomogenizationSignalTestProfile.SignalKey(HomogenizationPlcSignals.Interaction.扫码进站)));
        Assert.Contains("TRAY-DUP-IN", harness.Context.LastInboundResult, StringComparison.Ordinal);
        Assert.Equal(harness.Context.LastInboundResult, harness.Diagnostics.Get(TestCodeOptions.Mes.Channels.Inbound)!.LastFailureReason);
    }

    [Fact]
    public async Task Outbound_WhenNoLocalInboundSuccess_ShouldStillEnterDataPipelineForMesGate()
    {
        await using var harness = HomogenizationRuntimeHarness.Create();
        harness.Context.LastInboundAt = null;
        harness.Context.LastRecipeSnapshot = new HomogenizationRecipeSnapshot
        {
            StirringSpeed = [10],
            DispersionSpeed = [20],
            Ncm = [1.1],
            Time = [30]
        };
        harness.Context.LastEquipmentStatusSnapshot = new HomogenizationEquipmentStatusSnapshot
        {
            StatusCode = 1,
            StatusText = "空闲",
            Messages = ["空闲"]
        };
        await harness.StartAsync();

        harness.SetAscii(HomogenizationSignalTestProfile.SignalKey(HomogenizationPlcSignals.ContinuousRead.托盘码), "TRAY-OUT-001", 30);
        harness.SetWord(HomogenizationSignalTestProfile.SignalKey(HomogenizationPlcSignals.SingleRead.实时搅拌转速), 120);
        harness.SetWord(HomogenizationSignalTestProfile.SignalKey(HomogenizationPlcSignals.SingleRead.实时温度), 26);
        harness.SetWord(HomogenizationSignalTestProfile.SignalKey(HomogenizationPlcSignals.SingleRead.实时真空度), unchecked((ushort)(short)-9));
        harness.SetWord(HomogenizationSignalTestProfile.SignalKey(HomogenizationPlcSignals.SingleRead.出料CNT实际值), 15);
        harness.SetWord(HomogenizationSignalTestProfile.SignalKey(HomogenizationPlcSignals.SingleRead.出料NMP实际值), 18);
        harness.SetWord(HomogenizationSignalTestProfile.SignalKey(HomogenizationPlcSignals.SingleRead.出料胶液实际值), 31);
        harness.SetWord(HomogenizationSignalTestProfile.SignalKey(HomogenizationPlcSignals.Interaction.出料上传), TestCodeOptions.Plc.SignalTrigger);

        await WaitUntilAsync(() => harness.Context.GetStep("Homogenization.Outbound") == 30);

        var record = Assert.Single(harness.Pipeline.Records);
        var cellData = Assert.IsType<HomogenizationCellData>(record.CellData);
        Assert.Equal("TRAY-OUT-001", cellData.TrayCode);
        Assert.Null(cellData.InboundTime);
        Assert.Equal(120, cellData.RealtimeSnapshot!.StirringSpeed);
        Assert.Equal(26, cellData.RealtimeSnapshot.Temperature);
        Assert.Equal(-9, cellData.RealtimeSnapshot.Vacuum);
        Assert.Equal(15d, cellData.CntActualKg);
        Assert.Equal(18d, cellData.NmpActualKg);
        Assert.Equal(31d, cellData.GlueActualKg);
        Assert.Same(harness.Context.LastRecipeSnapshot, cellData.RecipeSnapshot);
        Assert.Same(harness.Context.LastEquipmentStatusSnapshot, cellData.EquipmentStatusSnapshot);
        Assert.Equal(TestCodeOptions.Plc.AckOk, harness.ReadWriteWord(HomogenizationSignalTestProfile.SignalKey(HomogenizationPlcSignals.Interaction.出料上传)));
        Assert.Equal("出料已接收。", harness.Context.LastOutboundResult);
    }

    [Fact]
    public async Task Outbound_WhenDuplicateCheckEnabled_ShouldAckMesNgAndNotEnqueueAgain()
    {
        await using var harness = HomogenizationRuntimeHarness.Create(duplicateCheckEnabled: true);
        await harness.StartAsync();

        harness.SetAscii(HomogenizationSignalTestProfile.SignalKey(HomogenizationPlcSignals.ContinuousRead.托盘码), "TRAY-DUP-OUT", 30);
        harness.SetWord(HomogenizationSignalTestProfile.SignalKey(HomogenizationPlcSignals.Interaction.出料上传), TestCodeOptions.Plc.SignalTrigger);
        await WaitUntilAsync(() => harness.Context.GetStep("Homogenization.Outbound") == 30);

        harness.SetWord(HomogenizationSignalTestProfile.SignalKey(HomogenizationPlcSignals.Interaction.出料上传), TestCodeOptions.Plc.SignalReset);
        await WaitUntilAsync(() => harness.Context.GetStep("Homogenization.Outbound") == 0);

        harness.SetWord(HomogenizationSignalTestProfile.SignalKey(HomogenizationPlcSignals.Interaction.出料上传), TestCodeOptions.Plc.SignalTrigger);
        await WaitUntilAsync(() => harness.Context.LastOutboundResult?.Contains("托盘码重复", StringComparison.Ordinal) == true);
        await WaitUntilAsync(() => harness.Diagnostics.Get(TestCodeOptions.Mes.Channels.Outbound)?.LastFailureReason?.Contains("托盘码重复", StringComparison.Ordinal) == true);

        Assert.Single(harness.Pipeline.Records);
        Assert.Equal(TestCodeOptions.Plc.AckMesNg, harness.ReadWriteWord(HomogenizationSignalTestProfile.SignalKey(HomogenizationPlcSignals.Interaction.出料上传)));
        Assert.Contains("TRAY-DUP-OUT", harness.Context.LastOutboundResult, StringComparison.Ordinal);
        Assert.Equal(harness.Context.LastOutboundResult, harness.Diagnostics.Get(TestCodeOptions.Mes.Channels.Outbound)!.LastFailureReason);
    }

    [Fact]
    public async Task Outbound_WhenAccepted_ShouldUseProductionBusinessTime()
    {
        var productionTime = new FakeProductionTimeProvider
        {
            FixedUtcNow = new DateTime(2026, 5, 3, 1, 2, 3, DateTimeKind.Utc)
        };
        await using var harness = HomogenizationRuntimeHarness.Create(productionTime: productionTime);
        await harness.StartAsync();

        harness.SetAscii(HomogenizationSignalTestProfile.SignalKey(HomogenizationPlcSignals.ContinuousRead.托盘码), "TRAY-BIZ-TIME", 30);
        harness.SetWord(HomogenizationSignalTestProfile.SignalKey(HomogenizationPlcSignals.Interaction.出料上传), TestCodeOptions.Plc.SignalTrigger);

        await WaitUntilAsync(() => harness.Context.GetStep("Homogenization.Outbound") == 30);

        var expected = productionTime.BusinessNow;
        var record = Assert.Single(harness.Pipeline.Records);
        var cellData = Assert.IsType<HomogenizationCellData>(record.CellData);
        Assert.Equal(expected, cellData.CompletedTime);
        Assert.Equal(expected, cellData.RealtimeSnapshot!.CapturedAt);
        Assert.Equal(expected, cellData.EquipmentStatusSnapshot!.CapturedAt);
        Assert.Equal(expected, harness.Context.LastOutboundAt);
    }

    [Fact]
    public async Task Outbound_WhenTrayCodeIsEmpty_ShouldAckExceptionAndNotEnqueue()
    {
        await using var harness = HomogenizationRuntimeHarness.Create();
        await harness.StartAsync();

        harness.SetAscii(HomogenizationSignalTestProfile.SignalKey(HomogenizationPlcSignals.ContinuousRead.托盘码), string.Empty, 30);
        harness.SetWord(HomogenizationSignalTestProfile.SignalKey(HomogenizationPlcSignals.Interaction.出料上传), TestCodeOptions.Plc.SignalTrigger);

        await WaitUntilAsync(() => harness.Context.GetStep("Homogenization.Outbound") == 30);

        Assert.Empty(harness.Pipeline.Records);
        Assert.Equal(TestCodeOptions.Plc.AckException, harness.ReadWriteWord(HomogenizationSignalTestProfile.SignalKey(HomogenizationPlcSignals.Interaction.出料上传)));
        Assert.Contains("托盘码不能为空", harness.Context.LastOutboundResult, StringComparison.Ordinal);
        Assert.Equal("Failed", harness.Diagnostics.Get(TestCodeOptions.Mes.Channels.Outbound)!.LastResult);
        Assert.Contains("托盘码不能为空", harness.Diagnostics.Get(TestCodeOptions.Mes.Channels.Outbound)!.LastFailureReason, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Outbound_WhenDataPipelineOverflows_ShouldAckOkAndRecordOverflowStatus()
    {
        await using var harness = HomogenizationRuntimeHarness.Create(
            pipeline: new CapturingDataPipelineService
            {
                Result = DataPipelineEnqueueResult.OverflowPersisted(1, 0)
            });
        await harness.StartAsync();

        harness.SetAscii(HomogenizationSignalTestProfile.SignalKey(HomogenizationPlcSignals.ContinuousRead.托盘码), "TRAY-OVERFLOW", 30);
        harness.SetWord(HomogenizationSignalTestProfile.SignalKey(HomogenizationPlcSignals.Interaction.出料上传), TestCodeOptions.Plc.SignalTrigger);

        await WaitUntilAsync(() => harness.Context.GetStep("Homogenization.Outbound") == 30);

        Assert.Single(harness.Pipeline.Records);
        Assert.Equal(TestCodeOptions.Plc.AckOk, harness.ReadWriteWord(HomogenizationSignalTestProfile.SignalKey(HomogenizationPlcSignals.Interaction.出料上传)));
        Assert.Equal("出料已接收，数据已进入溢出持久化。", harness.Context.LastOutboundResult);
    }

    [Fact]
    public async Task Outbound_WhenDataPipelineRejects_ShouldAckExceptionAndRecordFailure()
    {
        await using var harness = HomogenizationRuntimeHarness.Create(
            pipeline: new CapturingDataPipelineService
            {
                Result = DataPipelineEnqueueResult.Rejected("capacity_blocked")
            });
        await harness.StartAsync();

        harness.SetAscii(HomogenizationSignalTestProfile.SignalKey(HomogenizationPlcSignals.ContinuousRead.托盘码), "TRAY-REJECTED", 30);
        harness.SetWord(HomogenizationSignalTestProfile.SignalKey(HomogenizationPlcSignals.Interaction.出料上传), TestCodeOptions.Plc.SignalTrigger);

        await WaitUntilAsync(() => harness.Context.GetStep("Homogenization.Outbound") == 30);

        Assert.Single(harness.Pipeline.Records);
        Assert.Equal(TestCodeOptions.Plc.AckException, harness.ReadWriteWord(HomogenizationSignalTestProfile.SignalKey(HomogenizationPlcSignals.Interaction.出料上传)));
        Assert.Contains("数据管道拒绝入队", harness.Context.LastOutboundResult, StringComparison.Ordinal);
        Assert.Contains("capacity_blocked", harness.Context.LastOutboundResult, StringComparison.Ordinal);
        Assert.Equal("Failed", harness.Diagnostics.Get(TestCodeOptions.Mes.Channels.Outbound)!.LastResult);
        Assert.Equal(harness.Context.LastOutboundResult, harness.Diagnostics.Get(TestCodeOptions.Mes.Channels.Outbound)!.LastFailureReason);
    }

    [Fact]
    public async Task Outbound_WhenOverflowDoesNotPersistDurableTarget_ShouldAckExceptionAndRecordFailure()
    {
        await using var harness = HomogenizationRuntimeHarness.Create(
            pipeline: new CapturingDataPipelineService
            {
                Result = DataPipelineEnqueueResult.OverflowPersisted(0, 1)
            });
        await harness.StartAsync();

        harness.SetAscii(HomogenizationSignalTestProfile.SignalKey(HomogenizationPlcSignals.ContinuousRead.托盘码), "TRAY-OVERFLOW-FAILED", 30);
        harness.SetWord(HomogenizationSignalTestProfile.SignalKey(HomogenizationPlcSignals.Interaction.出料上传), TestCodeOptions.Plc.SignalTrigger);

        await WaitUntilAsync(() => harness.Context.GetStep("Homogenization.Outbound") == 30);

        Assert.Single(harness.Pipeline.Records);
        Assert.Equal(TestCodeOptions.Plc.AckException, harness.ReadWriteWord(HomogenizationSignalTestProfile.SignalKey(HomogenizationPlcSignals.Interaction.出料上传)));
        Assert.Contains("溢出持久化未写入任何补偿目标", harness.Context.LastOutboundResult, StringComparison.Ordinal);
        Assert.Contains("overflow_skipped_best_effort", harness.Context.LastOutboundResult, StringComparison.Ordinal);
        Assert.Equal("Failed", harness.Diagnostics.Get(TestCodeOptions.Mes.Channels.Outbound)!.LastResult);
        Assert.Equal(harness.Context.LastOutboundResult, harness.Diagnostics.Get(TestCodeOptions.Mes.Channels.Outbound)!.LastFailureReason);
    }

    [Fact]
    public async Task Outbound_WhenDataPipelineThrows_ShouldAckExceptionAndRecordFailure()
    {
        await using var harness = HomogenizationRuntimeHarness.Create(
            pipeline: new CapturingDataPipelineService
            {
                ExceptionToThrow = new InvalidOperationException("本地队列异常")
            });
        await harness.StartAsync();

        harness.SetAscii(HomogenizationSignalTestProfile.SignalKey(HomogenizationPlcSignals.ContinuousRead.托盘码), "TRAY-PIPELINE-EX", 30);
        harness.SetWord(HomogenizationSignalTestProfile.SignalKey(HomogenizationPlcSignals.Interaction.出料上传), TestCodeOptions.Plc.SignalTrigger);

        await WaitUntilAsync(() => harness.Context.GetStep("Homogenization.Outbound") == 30);

        Assert.Empty(harness.Pipeline.Records);
        Assert.Equal(TestCodeOptions.Plc.AckException, harness.ReadWriteWord(HomogenizationSignalTestProfile.SignalKey(HomogenizationPlcSignals.Interaction.出料上传)));
        Assert.Contains("本地队列异常", harness.Context.LastOutboundResult, StringComparison.Ordinal);
        Assert.Equal("Failed", harness.Diagnostics.Get(TestCodeOptions.Mes.Channels.Outbound)!.LastResult);
        Assert.Contains("本地队列异常", harness.Diagnostics.Get(TestCodeOptions.Mes.Channels.Outbound)!.LastFailureReason, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RecipeAndEquipmentStatus_WhenMesRejects_ShouldAckMesNgAndRecordDiagnostics()
    {
        await using var harness = HomogenizationRuntimeHarness.Create();
        harness.Mes.RecipeResult = MesCallResult.BusinessRejected("MES 拒绝配方。");
        harness.Mes.EquipmentStatusResult = MesCallResult.BusinessRejected("MES 拒绝设备状态。");
        await harness.StartAsync();

        harness.SetWord(HomogenizationSignalTestProfile.SignalKey(HomogenizationPlcSignals.ContinuousRead.配方搅拌转速), 55);
        harness.SetWord(HomogenizationSignalTestProfile.SignalKey(HomogenizationPlcSignals.Interaction.工艺参数上传), TestCodeOptions.Plc.SignalTrigger);
        await WaitUntilAsync(() => harness.Context.GetStep("Homogenization.Recipe") == 30);

        Assert.NotNull(harness.Context.LastRecipeSnapshot);
        Assert.Equal(TestCodeOptions.Plc.AckMesNg, harness.ReadWriteWord(HomogenizationSignalTestProfile.SignalKey(HomogenizationPlcSignals.Interaction.工艺参数上传)));
        Assert.Equal("MES 拒绝配方。", harness.Diagnostics.Get(TestCodeOptions.Mes.Channels.Recipe)!.LastFailureReason);

        harness.SetWord(HomogenizationSignalTestProfile.SignalKey(HomogenizationPlcSignals.SingleRead.设备状态值), 1);
        harness.SetWord(HomogenizationSignalTestProfile.SignalKey(HomogenizationPlcSignals.Interaction.设备状态上传), TestCodeOptions.Plc.SignalTrigger);
        await WaitUntilAsync(() => harness.Context.GetStep("Homogenization.EquipmentStatus") == 30);

        Assert.NotNull(harness.Context.LastEquipmentStatusSnapshot);
        Assert.Equal(TestCodeOptions.Plc.AckMesNg, harness.ReadWriteWord(HomogenizationSignalTestProfile.SignalKey(HomogenizationPlcSignals.Interaction.设备状态上传)));
        Assert.Equal("MES 拒绝设备状态。", harness.Diagnostics.Get(TestCodeOptions.Mes.Channels.EquipmentStatus)!.LastFailureReason);
    }

    [Fact]
    public async Task EquipmentStatus_ShouldWriteCloudDeviceLogWithMappedLevelBeforeMesResult()
    {
        await using var normalHarness = HomogenizationRuntimeHarness.Create();
        normalHarness.Mes.EquipmentStatusResult = MesCallResult.TransportFailure("MES 状态上传失败。");
        await normalHarness.StartAsync();

        normalHarness.SetWord(HomogenizationSignalTestProfile.SignalKey(HomogenizationPlcSignals.SingleRead.设备状态值), 1);
        normalHarness.SetWord(HomogenizationSignalTestProfile.SignalKey(HomogenizationPlcSignals.Interaction.设备状态上传), TestCodeOptions.Plc.SignalTrigger);
        await WaitUntilAsync(() => normalHarness.Context.GetStep("Homogenization.EquipmentStatus") == 30);

        Assert.Contains(
            normalHarness.Logger.Entries,
            entry => entry.Level == "Info"
                     && entry.Message.Contains("设备状态采集", StringComparison.Ordinal)
                     && entry.Message.Contains("状态码=1", StringComparison.Ordinal)
                     && entry.Message.Contains("PLC/设备=PLC-H", StringComparison.Ordinal));

        await using var alarmHarness = HomogenizationRuntimeHarness.Create();
        await alarmHarness.StartAsync();

        alarmHarness.SetWord(HomogenizationSignalTestProfile.SignalKey(HomogenizationPlcSignals.SingleRead.设备状态值), unchecked((ushort)-1));
        alarmHarness.SetWord(HomogenizationSignalTestProfile.SignalKey(HomogenizationPlcSignals.Interaction.设备状态上传), TestCodeOptions.Plc.SignalTrigger);
        await WaitUntilAsync(() => alarmHarness.Context.GetStep("Homogenization.EquipmentStatus") == 30);

        Assert.Contains(
            alarmHarness.Logger.Entries,
            entry => entry.Level == "Error"
                     && entry.Message.Contains("设备状态采集", StringComparison.Ordinal)
                     && entry.Message.Contains("状态码=-1", StringComparison.Ordinal)
                     && entry.Message.Contains("PLC 返回报警状态", StringComparison.Ordinal));
    }

    [Fact]
    public async Task EquipmentStatusCloudLog_WhenCloudGateBlocked_ShouldBufferAndRetryAfterRecovery()
    {
        await using var harness = HomogenizationRuntimeHarness.Create();
        harness.Mes.EquipmentStatusResult = MesCallResult.TransportFailure("MES 状态上传失败。");
        harness.DeviceService.MarkUploadGateBlocked(EdgeUploadBlockReason.MissingUploadToken, DateTimeOffset.UtcNow);

        var cloudHttp = new FakeCloudHttpClient();
        var bufferStore = new FakeDeviceLogBufferStore();
        var logSyncTask = new DeviceLogSyncTask(
            cloudHttp,
            new FakeCloudApiEndpointProvider(),
            harness.DeviceService,
            new FakeLocalSystemRuntimeConfigService
            {
                Current = SystemRuntimeConfigSnapshot.Default with
                {
                    CloudSyncInterval = TimeSpan.FromMilliseconds(50)
                }
            },
            bufferStore,
            harness.Logger,
            new FakeCloudDiagnosticsStore());

        using var logSyncCancellation = new CancellationTokenSource();
        await logSyncTask.StartAsync(logSyncCancellation.Token);
        await harness.StartAsync();

        harness.SetWord(HomogenizationSignalTestProfile.SignalKey(HomogenizationPlcSignals.SingleRead.设备状态值), 1);
        harness.SetWord(HomogenizationSignalTestProfile.SignalKey(HomogenizationPlcSignals.Interaction.设备状态上传), TestCodeOptions.Plc.SignalTrigger);
        await WaitUntilAsync(() => harness.Context.GetStep("Homogenization.EquipmentStatus") == 30);
        await logSyncTask.StopAsync();

        Assert.Equal("Failed", harness.Diagnostics.Get(TestCodeOptions.Mes.Channels.EquipmentStatus)!.LastResult);
        Assert.Equal(0, cloudHttp.PostCallCount);
        Assert.Contains(
            bufferStore.Records,
            record => record.Level == "Info"
                      && record.Message.Contains("设备状态采集", StringComparison.Ordinal)
                      && record.Message.Contains("状态码=1", StringComparison.Ordinal));

        cloudHttp.EnqueuePostResult(true);
        harness.DeviceService.SetOnline(harness.DeviceService.CurrentDevice!);

        var retryResult = await logSyncTask.RetryBufferAsync();

        Assert.True(retryResult);
        Assert.Equal(1, cloudHttp.PostCallCount);
        Assert.Empty(bufferStore.Records);

        var json = JsonSerializer.SerializeToElement(cloudHttp.LastPayload);
        Assert.Equal(harness.DeviceService.CurrentDevice!.DeviceId, json.GetProperty("deviceId").GetGuid());
        Assert.Contains(
            json.GetProperty("logs").EnumerateArray(),
            log => log.GetProperty("level").GetString() == "Info"
                   && log.GetProperty("message").GetString()!.Contains("设备状态采集", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Realtime_WhenMesFails_ShouldRecordFailureWithoutStoppingRuntime()
    {
        await using var harness = HomogenizationRuntimeHarness.Create();
        harness.Mes.RealtimeResult = MesCallResult.TransportFailure("MES 实时数据上传失败。");

        harness.SetWord(HomogenizationSignalTestProfile.SignalKey(HomogenizationPlcSignals.SingleRead.实时搅拌转速), 101);
        harness.SetWord(HomogenizationSignalTestProfile.SignalKey(HomogenizationPlcSignals.SingleRead.实时温度), 27);
        await harness.StartAsync();

        await WaitUntilAsync(() => harness.Context.LastRealtimeResult == "MES 实时数据上传失败。");

        Assert.NotNull(harness.Context.LastRealtimeSnapshot);
        Assert.Equal(101, harness.Context.LastRealtimeSnapshot!.StirringSpeed);
        Assert.Equal(27, harness.Context.LastRealtimeSnapshot.Temperature);
        Assert.Equal("Failed", harness.Diagnostics.Get(TestCodeOptions.Mes.Channels.Realtime)!.LastResult);
    }

    private static HomogenizationCodeOptions TestCodeOptions => new()
    {
        Plc = new HomogenizationPlcCodeOptions
        {
            SignalReset = 10,
            SignalTrigger = 11,
            AckOk = 11,
            AckException = 12,
            AckMesNg = 13
        },
        Mes = new HomogenizationMesCodeOptions
        {
            Channels = new HomogenizationMesChannelOptions
            {
                Inbound = "Homogenization.Inbound",
                Outbound = "Homogenization",
                Realtime = "Homogenization.Realtime",
                Recipe = "Homogenization.Recipe",
                EquipmentStatus = "Homogenization.EquipmentStatus"
            },
            EquipmentStatusTexts = new(StringComparer.OrdinalIgnoreCase)
            {
                ["-1"] = "报警",
                ["0"] = "运行中",
                ["1"] = "空闲",
                ["2"] = "离线"
            }
        }
    };

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (DateTime.UtcNow < deadline)
        {
            if (condition())
            {
                return;
            }

            await Task.Yield();
        }

        Assert.True(condition());
    }

    private sealed class HomogenizationRuntimeHarness : IAsyncDisposable
    {
        private readonly ServiceProvider _provider;
        private readonly CancellationTokenSource _cancellation = new();
        private Task[] _runningTasks = [];

        private HomogenizationRuntimeHarness(
            ServiceProvider provider,
            PlcBuffer buffer,
            ushort[] readValues,
            IReadOnlyDictionary<string, int> readOffsets,
            IReadOnlyDictionary<string, int> writeOffsets,
            HomogenizationContext context,
            CapturingHomogenizationMesChannel mes,
            CapturingDataPipelineService pipeline,
            FakeMesUploadDiagnosticsStore diagnostics,
            FakeHomogenizationModuleParamProvider parameters,
            FakeProductionTimeProvider productionTime,
            FakeDeviceService deviceService,
            FakeLogService logger)
        {
            _provider = provider;
            Buffer = buffer;
            ReadValues = readValues;
            ReadOffsets = readOffsets;
            WriteOffsets = writeOffsets;
            Context = context;
            Mes = mes;
            Pipeline = pipeline;
            Diagnostics = diagnostics;
            Parameters = parameters;
            ProductionTime = productionTime;
            DeviceService = deviceService;
            Logger = logger;
        }

        public PlcBuffer Buffer { get; }

        public ushort[] ReadValues { get; }

        public IReadOnlyDictionary<string, int> ReadOffsets { get; }

        public IReadOnlyDictionary<string, int> WriteOffsets { get; }

        public HomogenizationContext Context { get; }

        public CapturingHomogenizationMesChannel Mes { get; }

        public CapturingDataPipelineService Pipeline { get; }

        public FakeMesUploadDiagnosticsStore Diagnostics { get; }

        public FakeHomogenizationModuleParamProvider Parameters { get; }

        public FakeProductionTimeProvider ProductionTime { get; }

        public FakeDeviceService DeviceService { get; }

        public FakeLogService Logger { get; }

        public static HomogenizationRuntimeHarness Create(
            CapturingHomogenizationMesChannel? mes = null,
            CapturingDataPipelineService? pipeline = null,
            bool duplicateCheckEnabled = false,
            FakeProductionTimeProvider? productionTime = null)
        {
            mes ??= new CapturingHomogenizationMesChannel();
            pipeline ??= new CapturingDataPipelineService();
            productionTime ??= new FakeProductionTimeProvider();

            var bindings = BuildBindings();
            var readOffsets = BuildOffsets(bindings, "Read");
            var writeOffsets = BuildOffsets(bindings, "Write");
            var buffer = new PlcBuffer(GetBufferSize(bindings, "Read"), GetBufferSize(bindings, "Write"));
            var readValues = new ushort[GetBufferSize(bindings, "Read")];
            var context = new HomogenizationContext
            {
                DeviceName = "PLC-H",
            NetworkDeviceId = 7
            };
            var diagnostics = new FakeMesUploadDiagnosticsStore();
            var logger = new FakeLogService();
            var parameters = new FakeHomogenizationModuleParamProvider
            {
                DuplicateCheckEnabled = duplicateCheckEnabled
            };
            var deviceService = new FakeDeviceService();
            deviceService.SetOnline(new DeviceSession
            {
                DeviceId = Guid.NewGuid(),
                DeviceName = "PLC-H",
                ClientCode = "CLIENT-H",
                ProcessId = Guid.NewGuid()
            });

            var signalBindingStore = new ProductionContextSignalBindingStore();
            signalBindingStore.Set(context, bindings);
            SetWord(readValues, readOffsets, HomogenizationSignalTestProfile.SignalKey(HomogenizationPlcSignals.Interaction.心跳), 1);
            SetWord(readValues, readOffsets, HomogenizationSignalTestProfile.SignalKey(HomogenizationPlcSignals.Interaction.扫码进站), TestCodeOptions.Plc.SignalReset);
            SetWord(readValues, readOffsets, HomogenizationSignalTestProfile.SignalKey(HomogenizationPlcSignals.Interaction.出料上传), TestCodeOptions.Plc.SignalReset);
            SetWord(readValues, readOffsets, HomogenizationSignalTestProfile.SignalKey(HomogenizationPlcSignals.Interaction.工艺参数上传), TestCodeOptions.Plc.SignalReset);
            SetWord(readValues, readOffsets, HomogenizationSignalTestProfile.SignalKey(HomogenizationPlcSignals.Interaction.设备状态上传), TestCodeOptions.Plc.SignalReset);
            buffer.UpdateReadBuffer(readValues);

            var services = new ServiceCollection();
                    services.AddSingleton<IModulePlcSignalProfile<HomogenizationPlcSignals.Interaction>>(HomogenizationSignalTestProfile.InteractionProfileInstance);
        services.AddSingleton<IModulePlcSignalProfile<HomogenizationPlcSignals.SingleRead>>(HomogenizationSignalTestProfile.SingleReadProfileInstance);
        services.AddSingleton<IModulePlcSignalProfile<HomogenizationPlcSignals.ContinuousRead>>(HomogenizationSignalTestProfile.ContinuousReadProfileInstance);
            services.AddSingleton<ILogService>(logger);
            services.AddSingleton<IDeviceService>(deviceService);
            services.AddSingleton<IMesUploadDiagnosticsStore>(diagnostics);
            services.AddSingleton<IHomogenizationMesScenarioChannel>(mes);
            services.AddSingleton<IDataPipelineService>(pipeline);
            services.AddSingleton<IProductionTimeProvider>(productionTime);
            services.AddSingleton<IProductionContextSignalBindingStore>(signalBindingStore);
            services.AddSingleton<IModuleParamProvider<HomogenizationParams.Mes, HomogenizationParams.Cloud, HomogenizationParams.Business>>(parameters);
            services.AddSingleton<IHomogenizationProductionGate, AllowAllHomogenizationProductionGate>();
            services.AddSingleton(new HomogenizationCellDataValidator());
            services.AddSingleton(Options.Create(new HomogenizationModuleOptions
            {
                Runtime = new HomogenizationRuntimeOptions
                {
                    EventLoopIntervalMs = 20,
                    MinEventLoopIntervalMs = 10,
                    RealtimeLoopIntervalMs = 10_000,
                    MinRealtimeLoopIntervalMs = 200
                }
            }));
            services.AddSingleton(Options.Create(TestCodeOptions));

            return new HomogenizationRuntimeHarness(
                services.BuildServiceProvider(),
                buffer,
                readValues,
                readOffsets,
                writeOffsets,
                context,
                mes,
                pipeline,
                diagnostics,
                parameters,
                productionTime,
                deviceService,
                logger);
        }

        public Task StartAsync()
        {
        var factory = new HomogenizationStationRuntimeFactory();
        var tasks = factory.CreateTasks(
            _provider,
            Buffer,
            Context,
            factory.GetTaskCandidates().Select(static x => x.Key).ToHashSet(StringComparer.OrdinalIgnoreCase));
            _runningTasks = tasks.Select(task => task.StartAsync(_cancellation.Token)).ToArray();
            return Task.CompletedTask;
        }

        public void SetWord(string label, ushort value)
        {
            SetWord(ReadValues, ReadOffsets, label, value);
            Buffer.UpdateReadBuffer(ReadValues);
        }

        public void SetAscii(string label, string value, int wordCount)
        {
            SetAscii(ReadValues, ReadOffsets, label, value, wordCount);
            Buffer.UpdateReadBuffer(ReadValues);
        }

        public ushort ReadWriteWord(string label)
            => Buffer.GetWriteBuffer()[WriteOffsets[label]];

        public async ValueTask DisposeAsync()
        {
            _cancellation.Cancel();
            await Task.WhenAll(_runningTasks);
            _cancellation.Dispose();
            await _provider.DisposeAsync();
        }

        private static IReadOnlyList<ModuleIoSnapshot> BuildBindings()
            => HomogenizationSignalTestProfile.Signals
                .OrderBy(static signal => signal.SortOrder)
                .Select(static signal => new ModuleIoSnapshot(
                    signal.SignalKey,
                    $"D{signal.SortOrder}",
                    signal.AddressCount,
                    signal.DataType,
                    signal.DirectionText,
                    signal.SortOrder))
                .ToArray();

        private static Dictionary<string, int> BuildOffsets(IReadOnlyList<ModuleIoSnapshot> bindings, string direction)
        {
            var offsets = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var currentOffset = 0;

            foreach (var binding in bindings
                         .Where(binding => string.Equals(binding.Direction, direction, StringComparison.OrdinalIgnoreCase))
                         .OrderBy(binding => binding.SortOrder))
            {
                offsets[binding.SignalKey] = currentOffset;
                currentOffset += Math.Max(1, binding.AddressCount);
            }

            return offsets;
        }

        private static int GetBufferSize(IReadOnlyList<ModuleIoSnapshot> bindings, string direction)
            => bindings
                .Where(binding => string.Equals(binding.Direction, direction, StringComparison.OrdinalIgnoreCase))
                .OrderBy(binding => binding.SortOrder)
                .Sum(static binding => Math.Max(1, binding.AddressCount));

        private static void SetWord(ushort[] buffer, IReadOnlyDictionary<string, int> offsets, string label, ushort value)
            => buffer[offsets[label]] = value;

        private static void SetAscii(ushort[] buffer, IReadOnlyDictionary<string, int> offsets, string label, string value, int wordCount)
        {
            var bytes = System.Text.Encoding.ASCII.GetBytes(value);
            for (var wordIndex = 0; wordIndex < wordCount; wordIndex++)
            {
                var lowIndex = wordIndex * 2;
                var highIndex = lowIndex + 1;
                var low = lowIndex < bytes.Length ? bytes[lowIndex] : (byte)0;
                var high = highIndex < bytes.Length ? bytes[highIndex] : (byte)0;
                buffer[offsets[label] + wordIndex] = (ushort)(low | (high << 8));
            }
        }
    }

    private sealed class FakeHomogenizationModuleParamProvider
        : IModuleParamProvider<HomogenizationParams.Mes, HomogenizationParams.Cloud, HomogenizationParams.Business>
    {
        public bool DuplicateCheckEnabled { get; set; }

        public int GetCallCount { get; private set; }

        public Task<ModuleParamSnapshot<HomogenizationParams.Mes, HomogenizationParams.Cloud, HomogenizationParams.Business>> GetAsync(
            CancellationToken cancellationToken = default)
        {
            GetCallCount++;
            return Task.FromResult(new ModuleParamSnapshot<HomogenizationParams.Mes, HomogenizationParams.Cloud, HomogenizationParams.Business>(
                "Homogenization",
                EmptyGroup<HomogenizationParams.Mes>(ModuleParamCategory.Mes),
                EmptyGroup<HomogenizationParams.Cloud>(ModuleParamCategory.Cloud),
                new ModuleParamGroup<HomogenizationParams.Business>(
                    "Homogenization",
                    ModuleParamCategory.Business,
                    new Dictionary<HomogenizationParams.Business, string>
                    {
                        [HomogenizationParams.Business.启用托盘码重码验证] = DuplicateCheckEnabled.ToString()
                    },
                    new Dictionary<HomogenizationParams.Business, string?>
                    {
                        [HomogenizationParams.Business.启用托盘码重码验证] = "false"
                    },
                    new Dictionary<HomogenizationParams.Business, ParamValueKind>
                    {
                        [HomogenizationParams.Business.启用托盘码重码验证] = ParamValueKind.Bool
                    },
                    warn: null)));
        }

        private static ModuleParamGroup<TEnum> EmptyGroup<TEnum>(ModuleParamCategory category)
            where TEnum : struct, Enum
            => new(
                "Homogenization",
                category,
                new Dictionary<TEnum, string>(),
                new Dictionary<TEnum, string?>(),
                new Dictionary<TEnum, ParamValueKind>(),
                warn: null);
    }

    private sealed class CapturingHomogenizationMesChannel : IHomogenizationMesScenarioChannel
    {
        public List<string> InboundTrayCodes { get; } = [];

        public string ProcessType => "Homogenization";

        public ProcessUploadMode UploadMode => ProcessUploadMode.Single;

        public MesCallResult InboundResult { get; set; } = MesCallResult.Success();

        public MesCallResult RealtimeResult { get; set; } = MesCallResult.Success();

        public MesCallResult RecipeResult { get; set; } = MesCallResult.Success();

        public MesCallResult EquipmentStatusResult { get; set; } = MesCallResult.Success();

        public Exception? InboundException { get; set; }

        public Task<MesCallResult> UploadInboundAsync(
            DeviceSession? device,
            string trayCode,
            CancellationToken cancellationToken = default)
        {
            InboundTrayCodes.Add(trayCode);
            if (InboundException is not null)
            {
                throw InboundException;
            }

            return Task.FromResult(InboundResult);
        }

        public Task<MesCallResult> UploadOutboundAsync(
            DeviceSession? device,
            HomogenizationCellData cellData,
            CancellationToken cancellationToken = default)
            => Task.FromResult(MesCallResult.Success());

        public Task<MesCallResult> UploadRealtimeAsync(
            DeviceSession? device,
            HomogenizationRealtimeSnapshot snapshot,
            CancellationToken cancellationToken = default)
            => Task.FromResult(RealtimeResult);

        public Task<MesCallResult> UploadRecipeAsync(
            DeviceSession? device,
            HomogenizationRecipeSnapshot snapshot,
            CancellationToken cancellationToken = default)
            => Task.FromResult(RecipeResult);

        public Task<MesCallResult> UploadEquipmentStatusAsync(
            DeviceSession? device,
            HomogenizationEquipmentStatusSnapshot snapshot,
            CancellationToken cancellationToken = default)
            => Task.FromResult(EquipmentStatusResult);

        public Task<MesCallResult<HomogenizationMainPlan>> GetMainPlanAsync(
            HomogenizationMainPlanRequest request,
            CancellationToken cancellationToken = default)
            => Task.FromResult(MesCallResult<HomogenizationMainPlan>.Success(new HomogenizationMainPlan([])));

        public Task<MesCallResult<HomogenizationTraceBatchResult>> GenerateTraceBatchNumberAsync(
            HomogenizationTraceBatchRequest request,
            CancellationToken cancellationToken = default)
            => Task.FromResult(MesCallResult<HomogenizationTraceBatchResult>.Success(null));

        public Task<MesCallResult> UploadAsync(
            ProcessUploadContext context,
            IReadOnlyList<CellCompletedRecord> records,
            CancellationToken cancellationToken = default)
            => Task.FromResult(MesCallResult.Success());
    }

    private sealed class CapturingDataPipelineService : IDataPipelineService
    {
        public List<CellCompletedRecord> Records { get; } = [];

        public DataPipelineEnqueueResult Result { get; set; } = DataPipelineEnqueueResult.Accepted();

        public Exception? ExceptionToThrow { get; set; }

        public int PendingCount => Records.Count;

        public int OverflowCount => Result.WasOverflow ? 1 : 0;

        public int SpillCount => 0;

        public ValueTask<DataPipelineEnqueueResult> EnqueueAsync(
            CellCompletedRecord record,
            CancellationToken cancellationToken = default)
        {
            if (ExceptionToThrow is not null)
            {
                throw ExceptionToThrow;
            }

            Records.Add(record);
            return ValueTask.FromResult(Result);
        }

        public bool TryDequeue(out CellCompletedRecord? record)
        {
            record = Records.Count == 0 ? null : Records[0];
            if (Records.Count > 0)
            {
                Records.RemoveAt(0);
                return true;
            }

            return false;
        }

        public ValueTask<bool> WaitToReadAsync(CancellationToken cancellationToken = default)
            => ValueTask.FromResult(Records.Count > 0);
    }

    private sealed class AllowAllHomogenizationProductionGate : IHomogenizationProductionGate
    {
        public Task<MesCallResult> EnsureReadyAsync(
            HomogenizationContext context,
            CancellationToken cancellationToken = default)
            => Task.FromResult(MesCallResult.Success("测试门禁通过。"));
    }
}
