using IIoT.Edge.Application.Abstractions.Cloud;
using IIoT.Edge.Application.Abstractions.Config;
using IIoT.Edge.Application.Abstractions.DataPipeline;
using IIoT.Edge.Application.Abstractions.Device;
using IIoT.Edge.Application.Abstractions.Logging;
using IIoT.Edge.Application.Abstractions.Modules;
using IIoT.Edge.Application.Abstractions.Time;
using IIoT.Edge.Application.Features.Production.Planning;
using IIoT.Edge.Application.Modules.Hardware;
using IIoT.Edge.Infrastructure.DeviceComm.Plc.Store;
using IIoT.Edge.Module.Homogenization;
using IIoT.Edge.Module.Homogenization.Config;
using IIoT.Edge.Module.Homogenization.Config.Io;
using IIoT.Edge.Module.Homogenization.Config.Parameters;
using IIoT.Edge.Module.Homogenization.Mes;
using IIoT.Edge.Module.Homogenization.Payload;
using IIoT.Edge.Module.Homogenization.Production;
using IIoT.Edge.Module.Sdk.Signals;
using IIoT.Edge.SharedKernel.DataPipeline;
using IIoT.Edge.SharedKernel.DataPipeline.CellData;
using System.Globalization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

using IIoT.Edge.Application.Abstractions.Mes;
namespace IIoT.Edge.NonUiRegressionTests;

public sealed class HomogenizationBusinessChainBehaviorTests : IDisposable
{
    private readonly CultureInfo _originalCulture = CultureInfo.CurrentCulture;
    private readonly CultureInfo _originalUiCulture = CultureInfo.CurrentUICulture;

    public HomogenizationBusinessChainBehaviorTests()
    {
        var culture = CultureInfo.GetCultureInfo("zh-CN");
        CultureInfo.CurrentCulture = culture;
        CultureInfo.CurrentUICulture = culture;
    }

    public void Dispose()
    {
        CultureInfo.CurrentCulture = _originalCulture;
        CultureInfo.CurrentUICulture = _originalUiCulture;
    }

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
    public async Task Inbound_WhenReady_ShouldEnqueueMesOnlyRecordAndAckOk()
    {
        await using var harness = HomogenizationRuntimeHarness.Create();
        await harness.StartAsync();
        harness.Pipeline.Records.Clear();

        harness.SetAscii(HomogenizationSignalTestProfile.SignalKey(HomogenizationPlcSignals.ContinuousRead.托盘码), "TRAY-IN-QUEUE", 30);
        harness.SetWord(HomogenizationSignalTestProfile.SignalKey(HomogenizationPlcSignals.Interaction.扫码进站), TestCodeOptions.Plc.SignalTrigger);

        await WaitUntilAsync(() => harness.Context.GetStep("Homogenization.Inbound") == 30);

        var record = SingleRecordOfKind(harness.Pipeline, HomogenizationCellData.RecordKindInbound);
        var cellData = Assert.IsType<HomogenizationCellData>(record.CellData);
        Assert.Equal("TRAY-IN-QUEUE", cellData.TrayCode);
        Assert.Equal(DataPipelineUploadTargets.Mes, cellData.UploadTargets);
        Assert.Empty(harness.Mes.InboundTrayCodes);
        Assert.Equal(TestCodeOptions.Plc.AckOk, harness.ReadWriteWord(HomogenizationSignalTestProfile.SignalKey(HomogenizationPlcSignals.Interaction.扫码进站)));
        Assert.Equal("TRAY-IN-QUEUE", harness.Context.LastInboundTrayCode);
        Assert.Equal("进站已进入 MES 上传队列。", harness.Context.LastInboundResult);

        harness.SetWord(HomogenizationSignalTestProfile.SignalKey(HomogenizationPlcSignals.Interaction.扫码进站), TestCodeOptions.Plc.SignalReset);
        await WaitUntilAsync(() => harness.Context.GetStep("Homogenization.Inbound") == 0);

        Assert.Equal(TestCodeOptions.Plc.SignalReset, harness.ReadWriteWord(HomogenizationSignalTestProfile.SignalKey(HomogenizationPlcSignals.Interaction.扫码进站)));
    }

    [Fact]
    public async Task Inbound_WhenProductionGateBlocked_ShouldNotMarkTrayOrEnqueue()
    {
        await using var harness = HomogenizationRuntimeHarness.Create(
            productionGate: new RejectingHomogenizationProductionGate("MES 已启用，请先选择主批计划。"));
        await harness.StartAsync();
        harness.Pipeline.Records.Clear();

        harness.SetAscii(HomogenizationSignalTestProfile.SignalKey(HomogenizationPlcSignals.ContinuousRead.托盘码), "TRAY-NO-PLAN", 30);
        harness.SetWord(HomogenizationSignalTestProfile.SignalKey(HomogenizationPlcSignals.Interaction.扫码进站), TestCodeOptions.Plc.SignalTrigger);

        await WaitUntilAsync(() => harness.Context.GetStep("Homogenization.Inbound") == 30);

        Assert.False(HasRecordOfKind(harness.Pipeline, HomogenizationCellData.RecordKindInbound));
        Assert.False(harness.Context.HasProcessedTray(HomogenizationTrayCodeStage.Inbound, "TRAY-NO-PLAN"));
        Assert.Equal(TestCodeOptions.Plc.AckMesNg, harness.ReadWriteWord(HomogenizationSignalTestProfile.SignalKey(HomogenizationPlcSignals.Interaction.扫码进站)));
        Assert.Contains("请先选择主批计划", harness.Context.LastInboundResult, StringComparison.Ordinal);
        var diagnostics = harness.Diagnostics.Get(TestCodeOptions.Mes.Channels.Inbound)!;
        Assert.Equal("Blocked", diagnostics.LastResult);
        Assert.Null(diagnostics.LastFailureReason);
        Assert.Contains("请先选择主批计划", diagnostics.LastBlockedReason, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Inbound_WhenMesDisabled_ShouldAckOkAndSkipQueue()
    {
        await using var harness = HomogenizationRuntimeHarness.Create();
        harness.Parameters.MesEnabled = false;
        await harness.StartAsync();
        harness.Pipeline.Records.Clear();

        harness.SetAscii(HomogenizationSignalTestProfile.SignalKey(HomogenizationPlcSignals.ContinuousRead.托盘码), "TRAY-MES-OFF", 30);
        harness.SetWord(HomogenizationSignalTestProfile.SignalKey(HomogenizationPlcSignals.Interaction.扫码进站), TestCodeOptions.Plc.SignalTrigger);

        await WaitUntilAsync(() => harness.Context.GetStep("Homogenization.Inbound") == 30);

        Assert.False(HasRecordOfKind(harness.Pipeline, HomogenizationCellData.RecordKindInbound));
        Assert.False(harness.Context.HasProcessedTray(HomogenizationTrayCodeStage.Inbound, "TRAY-MES-OFF"));
        Assert.Equal(TestCodeOptions.Plc.AckOk, harness.ReadWriteWord(HomogenizationSignalTestProfile.SignalKey(HomogenizationPlcSignals.Interaction.扫码进站)));
        Assert.Equal("TRAY-MES-OFF", harness.Context.LastInboundTrayCode);
        Assert.Equal("MES/Cloud 上传已关闭，进站上传已跳过。", harness.Context.LastInboundResult);
        Assert.Null(harness.Diagnostics.Get(TestCodeOptions.Mes.Channels.Inbound));
    }

    [Fact]
    public async Task Inbound_WhenMesDisabledAndCloudEnabled_ShouldEnqueueCloudOnlyWithoutMainPlan()
    {
        await using var harness = HomogenizationRuntimeHarness.Create(
            productionGate: new RejectingHomogenizationProductionGate("MES 已启用，请先选择主批计划。"));
        harness.Parameters.MesEnabled = false;
        harness.Parameters.CloudEnabled = true;
        harness.Context.PlanSessionId = "PLAN-STALE";
        harness.Context.TraceBatchNumber = "TRACE-STALE";
        harness.Context.SelectedProductionPlan = new ProductionPlanOption(
            "PLAN-STALE",
            "STALE-MAIN",
            "STALE-ORDER",
            "STALE-MATERIAL",
            "STALE-BATCH",
            "STALE-PRODUCT",
            "READY",
            "CG",
            "匀浆",
            "LINE-1",
            "一线",
            "100",
            "0",
            "pcs",
            "MODEL",
            string.Empty,
            string.Empty,
            new Dictionary<string, string>());
        await harness.StartAsync();
        harness.Pipeline.Records.Clear();

        harness.SetAscii(HomogenizationSignalTestProfile.SignalKey(HomogenizationPlcSignals.ContinuousRead.托盘码), "TRAY-IN-CLOUD", 30);
        harness.SetWord(HomogenizationSignalTestProfile.SignalKey(HomogenizationPlcSignals.Interaction.扫码进站), TestCodeOptions.Plc.SignalTrigger);

        await WaitUntilAsync(() => harness.Context.GetStep("Homogenization.Inbound") == 30);

        var record = SingleRecordOfKind(harness.Pipeline, HomogenizationCellData.RecordKindInbound);
        var cellData = Assert.IsType<HomogenizationCellData>(record.CellData);
        Assert.Equal("TRAY-IN-CLOUD", cellData.TrayCode);
        Assert.Equal(DataPipelineUploadTargets.Cloud, cellData.UploadTargets);
        Assert.Equal(string.Empty, record.PlanSessionId);
        Assert.Equal(string.Empty, record.MainPlanCode);
        Assert.Equal(string.Empty, record.TraceBatchNumber);
        Assert.True(harness.Context.HasProcessedTray(HomogenizationTrayCodeStage.Inbound, "TRAY-IN-CLOUD"));
        Assert.Equal(TestCodeOptions.Plc.AckOk, harness.ReadWriteWord(HomogenizationSignalTestProfile.SignalKey(HomogenizationPlcSignals.Interaction.扫码进站)));
        Assert.Equal("进站已进入 Cloud 上传队列。", harness.Context.LastInboundResult);
        Assert.Null(harness.Diagnostics.Get(TestCodeOptions.Mes.Channels.Inbound));
    }

    [Fact]
    public async Task Inbound_WhenDataPipelineRejects_ShouldAckExceptionAndRecordFailure()
    {
        await using var harness = HomogenizationRuntimeHarness.Create(
            pipeline: new CapturingDataPipelineService
            {
                Result = DataPipelineEnqueueResult.Rejected("capacity_blocked")
            });
        await harness.StartAsync();
        harness.Pipeline.Records.Clear();

        harness.SetAscii(HomogenizationSignalTestProfile.SignalKey(HomogenizationPlcSignals.ContinuousRead.托盘码), "TRAY-IN-REJECT", 30);
        harness.SetWord(HomogenizationSignalTestProfile.SignalKey(HomogenizationPlcSignals.Interaction.扫码进站), TestCodeOptions.Plc.SignalTrigger);

        await WaitUntilAsync(() => harness.Context.GetStep("Homogenization.Inbound") == 30);

        Assert.Equal(TestCodeOptions.Plc.AckException, harness.ReadWriteWord(HomogenizationSignalTestProfile.SignalKey(HomogenizationPlcSignals.Interaction.扫码进站)));
        Assert.Equal("TRAY-IN-REJECT", harness.Context.LastInboundTrayCode);
        Assert.Contains("数据管道拒绝入队", harness.Context.LastInboundResult, StringComparison.Ordinal);
        Assert.Contains("capacity_blocked", harness.Context.LastInboundResult, StringComparison.Ordinal);
        Assert.Contains("capacity_blocked", harness.Diagnostics.Get(TestCodeOptions.Mes.Channels.Inbound)!.LastFailureReason, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Inbound_WhenCloudOnlyDataPipelineThrows_ShouldRecordCloudFailureWithoutMesDiagnostics()
    {
        await using var harness = HomogenizationRuntimeHarness.Create(
            pipeline: new CapturingDataPipelineService
            {
                ExceptionToThrow = new InvalidOperationException("本地队列异常")
            });
        harness.Parameters.MesEnabled = false;
        harness.Parameters.CloudEnabled = true;
        await harness.StartAsync();
        harness.Pipeline.Records.Clear();

        harness.SetAscii(HomogenizationSignalTestProfile.SignalKey(HomogenizationPlcSignals.ContinuousRead.托盘码), "TRAY-IN-CLOUD-EX", 30);
        harness.SetWord(HomogenizationSignalTestProfile.SignalKey(HomogenizationPlcSignals.Interaction.扫码进站), TestCodeOptions.Plc.SignalTrigger);

        await WaitUntilAsync(() => harness.Context.GetStep("Homogenization.Inbound") == 30);

        Assert.Empty(harness.Pipeline.Records);
        Assert.Equal(TestCodeOptions.Plc.AckException, harness.ReadWriteWord(HomogenizationSignalTestProfile.SignalKey(HomogenizationPlcSignals.Interaction.扫码进站)));
        Assert.Contains("本地队列异常", harness.Context.LastInboundResult, StringComparison.Ordinal);
        Assert.Null(harness.Diagnostics.Get(TestCodeOptions.Mes.Channels.Inbound));
        Assert.Equal(CloudCallOutcome.Exception, harness.CloudDiagnostics.Snapshot.LastOutcome);
        Assert.Equal("plc_inbound_enqueue_failed", harness.CloudDiagnostics.Snapshot.LastReasonCode);
        Assert.Equal("Homogenization.Inbound", harness.CloudDiagnostics.Snapshot.LastTaskKey);
        Assert.Equal("进站上传", harness.CloudDiagnostics.Snapshot.LastScenario);
    }

    [Fact]
    public async Task Inbound_WhenDuplicateCheckDisabled_ShouldAllowRepeatedTrayCode()
    {
        await using var harness = HomogenizationRuntimeHarness.Create(duplicateCheckEnabled: false);
        await harness.StartAsync();
        harness.Pipeline.Records.Clear();

        harness.SetAscii(HomogenizationSignalTestProfile.SignalKey(HomogenizationPlcSignals.ContinuousRead.托盘码), "TRAY-DUP-OFF", 30);
        harness.SetWord(HomogenizationSignalTestProfile.SignalKey(HomogenizationPlcSignals.Interaction.扫码进站), TestCodeOptions.Plc.SignalTrigger);
        await WaitUntilAsync(() => harness.Context.GetStep("Homogenization.Inbound") == 30);

        harness.SetWord(HomogenizationSignalTestProfile.SignalKey(HomogenizationPlcSignals.Interaction.扫码进站), TestCodeOptions.Plc.SignalReset);
        await WaitUntilAsync(() => harness.Context.GetStep("Homogenization.Inbound") == 0);

        harness.SetWord(HomogenizationSignalTestProfile.SignalKey(HomogenizationPlcSignals.Interaction.扫码进站), TestCodeOptions.Plc.SignalTrigger);
        await WaitUntilAsync(() => RecordsOfKind(harness.Pipeline, HomogenizationCellData.RecordKindInbound).Count == 2);
        await WaitUntilAsync(() => harness.ReadWriteWord(HomogenizationSignalTestProfile.SignalKey(HomogenizationPlcSignals.Interaction.扫码进站)) == TestCodeOptions.Plc.AckOk);

        Assert.Equal(
            ["TRAY-DUP-OFF", "TRAY-DUP-OFF"],
            RecordsOfKind(harness.Pipeline, HomogenizationCellData.RecordKindInbound)
                .Select(record => Assert.IsType<HomogenizationCellData>(record.CellData).TrayCode)
                .ToArray());
        Assert.Equal(TestCodeOptions.Plc.AckOk, harness.ReadWriteWord(HomogenizationSignalTestProfile.SignalKey(HomogenizationPlcSignals.Interaction.扫码进站)));
    }

    [Fact]
    public async Task Inbound_WhenDuplicateCheckEnabled_ShouldAckMesNgAndNotCallMesAgain()
    {
        await using var harness = HomogenizationRuntimeHarness.Create(duplicateCheckEnabled: true);
        await harness.StartAsync();
        harness.Pipeline.Records.Clear();

        harness.SetAscii(HomogenizationSignalTestProfile.SignalKey(HomogenizationPlcSignals.ContinuousRead.托盘码), "TRAY-DUP-IN", 30);
        harness.SetWord(HomogenizationSignalTestProfile.SignalKey(HomogenizationPlcSignals.Interaction.扫码进站), TestCodeOptions.Plc.SignalTrigger);
        await WaitUntilAsync(() => harness.Context.GetStep("Homogenization.Inbound") == 30);

        harness.SetWord(HomogenizationSignalTestProfile.SignalKey(HomogenizationPlcSignals.Interaction.扫码进站), TestCodeOptions.Plc.SignalReset);
        await WaitUntilAsync(() => harness.Context.GetStep("Homogenization.Inbound") == 0);

        harness.SetWord(HomogenizationSignalTestProfile.SignalKey(HomogenizationPlcSignals.Interaction.扫码进站), TestCodeOptions.Plc.SignalTrigger);
        await WaitUntilAsync(() => harness.Context.LastInboundResult?.Contains("托盘码重复", StringComparison.Ordinal) == true);

        Assert.Single(RecordsOfKind(harness.Pipeline, HomogenizationCellData.RecordKindInbound));
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
        harness.Pipeline.Records.Clear();

        harness.SetAscii(HomogenizationSignalTestProfile.SignalKey(HomogenizationPlcSignals.ContinuousRead.托盘码), "TRAY-OUT-001", 30);
        harness.SetWord(HomogenizationSignalTestProfile.SignalKey(HomogenizationPlcSignals.SingleRead.实时搅拌转速), 120);
        harness.SetWord(HomogenizationSignalTestProfile.SignalKey(HomogenizationPlcSignals.SingleRead.实时温度), 26);
        harness.SetWord(HomogenizationSignalTestProfile.SignalKey(HomogenizationPlcSignals.SingleRead.实时真空度), unchecked((ushort)(short)-9));
        harness.SetWord(HomogenizationSignalTestProfile.SignalKey(HomogenizationPlcSignals.SingleRead.出料CNT实际值), 15);
        harness.SetWord(HomogenizationSignalTestProfile.SignalKey(HomogenizationPlcSignals.SingleRead.出料NMP实际值), 18);
        harness.SetWord(HomogenizationSignalTestProfile.SignalKey(HomogenizationPlcSignals.SingleRead.出料胶液实际值), 31);
        harness.SetWord(HomogenizationSignalTestProfile.SignalKey(HomogenizationPlcSignals.Interaction.出料上传), TestCodeOptions.Plc.SignalTrigger);

        await WaitUntilAsync(() => harness.Context.GetStep("Homogenization.Outbound") == 30);

        var record = SingleRecordOfKind(harness.Pipeline, HomogenizationCellData.RecordKindOutbound);
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
    public async Task Outbound_WhenProductionGateBlocked_ShouldNotRecordOutboundOrEnqueue()
    {
        await using var harness = HomogenizationRuntimeHarness.Create(
            productionGate: new RejectingHomogenizationProductionGate("MES 已启用，请先选择主批计划。"));
        await harness.StartAsync();
        harness.Pipeline.Records.Clear();

        harness.SetAscii(HomogenizationSignalTestProfile.SignalKey(HomogenizationPlcSignals.ContinuousRead.托盘码), "TRAY-OUT-NO-PLAN", 30);
        harness.SetWord(HomogenizationSignalTestProfile.SignalKey(HomogenizationPlcSignals.Interaction.出料上传), TestCodeOptions.Plc.SignalTrigger);

        await WaitUntilAsync(() => harness.Context.GetStep("Homogenization.Outbound") == 30);

        Assert.False(HasRecordOfKind(harness.Pipeline, HomogenizationCellData.RecordKindOutbound));
        Assert.Null(harness.Context.LastOutboundRecord);
        Assert.Empty(harness.Context.OutboundRecords);
        Assert.Equal(TestCodeOptions.Plc.AckMesNg, harness.ReadWriteWord(HomogenizationSignalTestProfile.SignalKey(HomogenizationPlcSignals.Interaction.出料上传)));
        Assert.Contains("请先选择主批计划", harness.Context.LastOutboundResult, StringComparison.Ordinal);
        var diagnostics = harness.Diagnostics.Get(TestCodeOptions.Mes.Channels.Outbound)!;
        Assert.Equal("Blocked", diagnostics.LastResult);
        Assert.Null(diagnostics.LastFailureReason);
        Assert.Contains("请先选择主批计划", diagnostics.LastBlockedReason, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Outbound_WhenMesDisabledAndCloudEnabled_ShouldEnqueueCloudOnlyWithoutMainBatchGate()
    {
        await using var harness = HomogenizationRuntimeHarness.Create(
            productionGate: new RejectingHomogenizationProductionGate("MES 已启用，请先选择主批计划。"));
        harness.Parameters.MesEnabled = false;
        harness.Parameters.CloudEnabled = true;
        harness.Context.PlanSessionId = "PLAN-STALE";
        harness.Context.TraceBatchNumber = "TRACE-STALE";
        harness.Context.SelectedProductionPlan = new ProductionPlanOption(
            "PLAN-STALE",
            "STALE-MAIN",
            "STALE-ORDER",
            "STALE-MATERIAL",
            "STALE-BATCH",
            "STALE-PRODUCT",
            "READY",
            "CG",
            "匀浆",
            "LINE-1",
            "一线",
            "100",
            "0",
            "pcs",
            "MODEL",
            string.Empty,
            string.Empty,
            new Dictionary<string, string>());
        await harness.StartAsync();
        harness.Pipeline.Records.Clear();

        harness.SetAscii(HomogenizationSignalTestProfile.SignalKey(HomogenizationPlcSignals.ContinuousRead.托盘码), "TRAY-CLOUD-ONLY", 30);
        harness.SetWord(HomogenizationSignalTestProfile.SignalKey(HomogenizationPlcSignals.Interaction.出料上传), TestCodeOptions.Plc.SignalTrigger);

        await WaitUntilAsync(() => harness.Context.GetStep("Homogenization.Outbound") == 30);

        var record = SingleRecordOfKind(harness.Pipeline, HomogenizationCellData.RecordKindOutbound);
        var cellData = Assert.IsType<HomogenizationCellData>(record.CellData);
        Assert.Equal(DataPipelineUploadTargets.Cloud, cellData.UploadTargets);
        Assert.Equal(string.Empty, record.PlanSessionId);
        Assert.Equal(string.Empty, record.MainPlanCode);
        Assert.Equal(string.Empty, record.TraceBatchNumber);
        Assert.Equal(TestCodeOptions.Plc.AckOk, harness.ReadWriteWord(HomogenizationSignalTestProfile.SignalKey(HomogenizationPlcSignals.Interaction.出料上传)));
        Assert.Equal("出料已接收。", harness.Context.LastOutboundResult);
        Assert.Null(harness.Diagnostics.Get(TestCodeOptions.Mes.Channels.Outbound));
    }

    [Fact]
    public async Task Outbound_WhenMesAndCloudDisabled_ShouldRecordLocalOnlyAndSkipQueue()
    {
        await using var harness = HomogenizationRuntimeHarness.Create();
        harness.Parameters.MesEnabled = false;
        harness.Parameters.CloudEnabled = false;
        await harness.StartAsync();
        harness.Pipeline.Records.Clear();

        harness.SetAscii(HomogenizationSignalTestProfile.SignalKey(HomogenizationPlcSignals.ContinuousRead.托盘码), "TRAY-LOCAL-ONLY", 30);
        harness.SetWord(HomogenizationSignalTestProfile.SignalKey(HomogenizationPlcSignals.Interaction.出料上传), TestCodeOptions.Plc.SignalTrigger);

        await WaitUntilAsync(() => harness.Context.GetStep("Homogenization.Outbound") == 30);

        Assert.False(HasRecordOfKind(harness.Pipeline, HomogenizationCellData.RecordKindOutbound));
        Assert.NotNull(harness.Context.LastOutboundRecord);
        Assert.Single(harness.Context.OutboundRecords);
        Assert.Equal("TRAY-LOCAL-ONLY", harness.Context.LastOutboundTrayCode);
        Assert.True(harness.Context.HasProcessedTray(HomogenizationTrayCodeStage.Outbound, "TRAY-LOCAL-ONLY"));
        Assert.Equal(TestCodeOptions.Plc.AckOk, harness.ReadWriteWord(HomogenizationSignalTestProfile.SignalKey(HomogenizationPlcSignals.Interaction.出料上传)));
        Assert.Equal("MES/Cloud 上传已关闭，出料已本地记录。", harness.Context.LastOutboundResult);
    }

    [Fact]
    public async Task Outbound_WhenDuplicateCheckEnabled_ShouldAckMesNgAndNotEnqueueAgain()
    {
        await using var harness = HomogenizationRuntimeHarness.Create(duplicateCheckEnabled: true);
        await harness.StartAsync();
        harness.Pipeline.Records.Clear();

        harness.SetAscii(HomogenizationSignalTestProfile.SignalKey(HomogenizationPlcSignals.ContinuousRead.托盘码), "TRAY-DUP-OUT", 30);
        harness.SetWord(HomogenizationSignalTestProfile.SignalKey(HomogenizationPlcSignals.Interaction.出料上传), TestCodeOptions.Plc.SignalTrigger);
        await WaitUntilAsync(() => harness.Context.GetStep("Homogenization.Outbound") == 30);

        harness.SetWord(HomogenizationSignalTestProfile.SignalKey(HomogenizationPlcSignals.Interaction.出料上传), TestCodeOptions.Plc.SignalReset);
        await WaitUntilAsync(() => harness.Context.GetStep("Homogenization.Outbound") == 0);

        harness.SetWord(HomogenizationSignalTestProfile.SignalKey(HomogenizationPlcSignals.Interaction.出料上传), TestCodeOptions.Plc.SignalTrigger);
        await WaitUntilAsync(() => harness.Context.LastOutboundResult?.Contains("托盘码重复", StringComparison.Ordinal) == true);
        await WaitUntilAsync(() => harness.Diagnostics.Get(TestCodeOptions.Mes.Channels.Outbound)?.LastFailureReason?.Contains("托盘码重复", StringComparison.Ordinal) == true);

        Assert.Single(RecordsOfKind(harness.Pipeline, HomogenizationCellData.RecordKindOutbound));
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
        harness.Pipeline.Records.Clear();

        harness.SetAscii(HomogenizationSignalTestProfile.SignalKey(HomogenizationPlcSignals.ContinuousRead.托盘码), "TRAY-BIZ-TIME", 30);
        harness.SetWord(HomogenizationSignalTestProfile.SignalKey(HomogenizationPlcSignals.Interaction.出料上传), TestCodeOptions.Plc.SignalTrigger);

        await WaitUntilAsync(() => harness.Context.GetStep("Homogenization.Outbound") == 30);

        var expected = productionTime.BusinessNow;
        var record = SingleRecordOfKind(harness.Pipeline, HomogenizationCellData.RecordKindOutbound);
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
        harness.Pipeline.Records.Clear();

        harness.SetAscii(HomogenizationSignalTestProfile.SignalKey(HomogenizationPlcSignals.ContinuousRead.托盘码), string.Empty, 30);
        harness.SetWord(HomogenizationSignalTestProfile.SignalKey(HomogenizationPlcSignals.Interaction.出料上传), TestCodeOptions.Plc.SignalTrigger);

        await WaitUntilAsync(() => harness.Context.GetStep("Homogenization.Outbound") == 30);

        Assert.False(HasRecordOfKind(harness.Pipeline, HomogenizationCellData.RecordKindOutbound));
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
        harness.Pipeline.Records.Clear();

        harness.SetAscii(HomogenizationSignalTestProfile.SignalKey(HomogenizationPlcSignals.ContinuousRead.托盘码), "TRAY-OVERFLOW", 30);
        harness.SetWord(HomogenizationSignalTestProfile.SignalKey(HomogenizationPlcSignals.Interaction.出料上传), TestCodeOptions.Plc.SignalTrigger);

        await WaitUntilAsync(() => harness.Context.GetStep("Homogenization.Outbound") == 30);

        Assert.Single(RecordsOfKind(harness.Pipeline, HomogenizationCellData.RecordKindOutbound));
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
        harness.Pipeline.Records.Clear();

        harness.SetAscii(HomogenizationSignalTestProfile.SignalKey(HomogenizationPlcSignals.ContinuousRead.托盘码), "TRAY-REJECTED", 30);
        harness.SetWord(HomogenizationSignalTestProfile.SignalKey(HomogenizationPlcSignals.Interaction.出料上传), TestCodeOptions.Plc.SignalTrigger);

        await WaitUntilAsync(() => harness.Context.GetStep("Homogenization.Outbound") == 30);

        Assert.Single(RecordsOfKind(harness.Pipeline, HomogenizationCellData.RecordKindOutbound));
        Assert.Equal(TestCodeOptions.Plc.AckException, harness.ReadWriteWord(HomogenizationSignalTestProfile.SignalKey(HomogenizationPlcSignals.Interaction.出料上传)));
        Assert.Contains("数据管道拒绝入队", harness.Context.LastOutboundResult, StringComparison.Ordinal);
        Assert.Contains("capacity_blocked", harness.Context.LastOutboundResult, StringComparison.Ordinal);
        Assert.Equal("Failed", harness.Diagnostics.Get(TestCodeOptions.Mes.Channels.Outbound)!.LastResult);
        Assert.Equal(harness.Context.LastOutboundResult, harness.Diagnostics.Get(TestCodeOptions.Mes.Channels.Outbound)!.LastFailureReason);
    }

    [Fact]
    public async Task Outbound_WhenCloudOnlyDataPipelineRejects_ShouldRecordCloudFailureWithoutMesDiagnostics()
    {
        await using var harness = HomogenizationRuntimeHarness.Create(
            pipeline: new CapturingDataPipelineService
            {
                Result = DataPipelineEnqueueResult.Rejected("capacity_blocked")
            });
        harness.Parameters.MesEnabled = false;
        harness.Parameters.CloudEnabled = true;
        await harness.StartAsync();
        harness.Pipeline.Records.Clear();

        harness.SetAscii(HomogenizationSignalTestProfile.SignalKey(HomogenizationPlcSignals.ContinuousRead.托盘码), "TRAY-CLOUD-REJECTED", 30);
        harness.SetWord(HomogenizationSignalTestProfile.SignalKey(HomogenizationPlcSignals.Interaction.出料上传), TestCodeOptions.Plc.SignalTrigger);

        await WaitUntilAsync(() => harness.Context.GetStep("Homogenization.Outbound") == 30);

        Assert.Single(RecordsOfKind(harness.Pipeline, HomogenizationCellData.RecordKindOutbound));
        Assert.Equal(TestCodeOptions.Plc.AckException, harness.ReadWriteWord(HomogenizationSignalTestProfile.SignalKey(HomogenizationPlcSignals.Interaction.出料上传)));
        Assert.Contains("数据管道拒绝入队", harness.Context.LastOutboundResult, StringComparison.Ordinal);
        Assert.Null(harness.Diagnostics.Get(TestCodeOptions.Mes.Channels.Outbound));
        Assert.Equal(CloudCallOutcome.Exception, harness.CloudDiagnostics.Snapshot.LastOutcome);
        Assert.Equal("plc_outbound_enqueue_failed", harness.CloudDiagnostics.Snapshot.LastReasonCode);
        Assert.Equal("Homogenization", harness.CloudDiagnostics.Snapshot.LastProcessType);
        Assert.Equal("PLC-H", harness.CloudDiagnostics.Snapshot.LastDeviceName);
        Assert.Equal("Homogenization.Outbound", harness.CloudDiagnostics.Snapshot.LastTaskKey);
        Assert.Equal("出站上传", harness.CloudDiagnostics.Snapshot.LastScenario);
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
        harness.Pipeline.Records.Clear();

        harness.SetAscii(HomogenizationSignalTestProfile.SignalKey(HomogenizationPlcSignals.ContinuousRead.托盘码), "TRAY-OVERFLOW-FAILED", 30);
        harness.SetWord(HomogenizationSignalTestProfile.SignalKey(HomogenizationPlcSignals.Interaction.出料上传), TestCodeOptions.Plc.SignalTrigger);

        await WaitUntilAsync(() => harness.Context.GetStep("Homogenization.Outbound") == 30);

        Assert.Single(RecordsOfKind(harness.Pipeline, HomogenizationCellData.RecordKindOutbound));
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
        harness.Pipeline.Records.Clear();

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
    public async Task RecipeAndEquipmentStatus_WhenReady_ShouldEnqueueMesOnlyRecordsAndAckOk()
    {
        await using var harness = HomogenizationRuntimeHarness.Create();
        await harness.StartAsync();
        harness.Pipeline.Records.Clear();

        harness.SetWord(HomogenizationSignalTestProfile.SignalKey(HomogenizationPlcSignals.ContinuousRead.配方搅拌转速), 55);
        harness.SetWord(HomogenizationSignalTestProfile.SignalKey(HomogenizationPlcSignals.Interaction.工艺参数上传), TestCodeOptions.Plc.SignalTrigger);
        await WaitUntilAsync(() => harness.Context.GetStep("Homogenization.Recipe") == 30);

        Assert.NotNull(harness.Context.LastRecipeSnapshot);
        var recipeRecord = SingleRecordOfKind(harness.Pipeline, HomogenizationCellData.RecordKindRecipe);
        var recipeData = Assert.IsType<HomogenizationCellData>(recipeRecord.CellData);
        Assert.Same(harness.Context.LastRecipeSnapshot, recipeData.RecipeSnapshot);
        Assert.Equal(DataPipelineUploadTargets.Mes, recipeData.UploadTargets);
        Assert.Equal(TestCodeOptions.Plc.AckOk, harness.ReadWriteWord(HomogenizationSignalTestProfile.SignalKey(HomogenizationPlcSignals.Interaction.工艺参数上传)));
        Assert.Equal("配方已进入 MES 上传队列。", harness.Context.LastRecipeResult);

        harness.SetWord(HomogenizationSignalTestProfile.SignalKey(HomogenizationPlcSignals.SingleRead.设备状态值), 1);
        harness.SetWord(HomogenizationSignalTestProfile.SignalKey(HomogenizationPlcSignals.Interaction.设备状态上传), TestCodeOptions.Plc.SignalTrigger);
        await WaitUntilAsync(() => harness.Context.GetStep("Homogenization.EquipmentStatus") == 30);

        Assert.NotNull(harness.Context.LastEquipmentStatusSnapshot);
        var statusRecord = SingleRecordOfKind(harness.Pipeline, HomogenizationCellData.RecordKindEquipmentStatus);
        var statusData = Assert.IsType<HomogenizationCellData>(statusRecord.CellData);
        Assert.Same(harness.Context.LastEquipmentStatusSnapshot, statusData.EquipmentStatusSnapshot);
        Assert.Equal(DataPipelineUploadTargets.Mes, statusData.UploadTargets);
        Assert.Equal(TestCodeOptions.Plc.AckOk, harness.ReadWriteWord(HomogenizationSignalTestProfile.SignalKey(HomogenizationPlcSignals.Interaction.设备状态上传)));
        Assert.Equal("设备状态已进入 MES 上传队列。", harness.Context.LastEquipmentStatusResult);
    }

    [Fact]
    public async Task Recipe_WhenMesDisabled_ShouldAckOkAndSkipQueue()
    {
        await using var harness = HomogenizationRuntimeHarness.Create();
        harness.Parameters.MesEnabled = false;
        await harness.StartAsync();
        harness.Pipeline.Records.Clear();

        harness.SetWord(HomogenizationSignalTestProfile.SignalKey(HomogenizationPlcSignals.ContinuousRead.配方搅拌转速), 55);
        harness.SetWord(HomogenizationSignalTestProfile.SignalKey(HomogenizationPlcSignals.Interaction.工艺参数上传), TestCodeOptions.Plc.SignalTrigger);

        await WaitUntilAsync(() => harness.Context.GetStep("Homogenization.Recipe") == 30);

        Assert.False(HasRecordOfKind(harness.Pipeline, HomogenizationCellData.RecordKindRecipe));
        Assert.Null(harness.Context.LastRecipeSnapshot);
        Assert.Equal(TestCodeOptions.Plc.AckOk, harness.ReadWriteWord(HomogenizationSignalTestProfile.SignalKey(HomogenizationPlcSignals.Interaction.工艺参数上传)));
        Assert.Equal("MES/Cloud 上传已关闭，配方上传已跳过。", harness.Context.LastRecipeResult);
        Assert.Null(harness.Diagnostics.Get(TestCodeOptions.Mes.Channels.Recipe));
    }

    [Fact]
    public async Task Recipe_WhenMesDisabledAndCloudEnabled_ShouldEnqueueCloudOnlyWithoutMainPlan()
    {
        await using var harness = HomogenizationRuntimeHarness.Create(
            productionGate: new RejectingHomogenizationProductionGate("MES 已启用，请先选择主批计划。"));
        harness.Parameters.MesEnabled = false;
        harness.Parameters.CloudEnabled = true;
        harness.Context.PlanSessionId = "PLAN-STALE";
        harness.Context.TraceBatchNumber = "TRACE-STALE";
        harness.Context.SelectedProductionPlan = new ProductionPlanOption(
            "PLAN-STALE",
            "STALE-MAIN",
            "STALE-ORDER",
            "STALE-MATERIAL",
            "STALE-BATCH",
            "STALE-PRODUCT",
            "READY",
            "CG",
            "匀浆",
            "LINE-1",
            "一线",
            "100",
            "0",
            "pcs",
            "MODEL",
            string.Empty,
            string.Empty,
            new Dictionary<string, string>());
        await harness.StartAsync();
        harness.Pipeline.Records.Clear();

        harness.SetWord(HomogenizationSignalTestProfile.SignalKey(HomogenizationPlcSignals.ContinuousRead.配方搅拌转速), 66);
        harness.SetWord(HomogenizationSignalTestProfile.SignalKey(HomogenizationPlcSignals.Interaction.工艺参数上传), TestCodeOptions.Plc.SignalTrigger);

        await WaitUntilAsync(() => harness.Context.GetStep("Homogenization.Recipe") == 30);

        var recipeRecord = SingleRecordOfKind(harness.Pipeline, HomogenizationCellData.RecordKindRecipe);
        var recipeData = Assert.IsType<HomogenizationCellData>(recipeRecord.CellData);
        Assert.Same(harness.Context.LastRecipeSnapshot, recipeData.RecipeSnapshot);
        Assert.Equal(66, recipeData.RecipeSnapshot!.StirringSpeed[0]);
        Assert.Equal(DataPipelineUploadTargets.Cloud, recipeData.UploadTargets);
        Assert.Equal(string.Empty, recipeRecord.PlanSessionId);
        Assert.Equal(string.Empty, recipeRecord.MainPlanCode);
        Assert.Equal(string.Empty, recipeRecord.TraceBatchNumber);
        Assert.Equal(TestCodeOptions.Plc.AckOk, harness.ReadWriteWord(HomogenizationSignalTestProfile.SignalKey(HomogenizationPlcSignals.Interaction.工艺参数上传)));
        Assert.Equal("配方已进入 Cloud 上传队列。", harness.Context.LastRecipeResult);
        Assert.Null(harness.Diagnostics.Get(TestCodeOptions.Mes.Channels.Recipe));
    }

    [Fact]
    public async Task Recipe_WhenMesAndCloudEnabled_ShouldEnqueueAllTargets()
    {
        await using var harness = HomogenizationRuntimeHarness.Create();
        harness.Parameters.CloudEnabled = true;
        await harness.StartAsync();
        harness.Pipeline.Records.Clear();

        harness.SetWord(HomogenizationSignalTestProfile.SignalKey(HomogenizationPlcSignals.ContinuousRead.配方搅拌转速), 77);
        harness.SetWord(HomogenizationSignalTestProfile.SignalKey(HomogenizationPlcSignals.Interaction.工艺参数上传), TestCodeOptions.Plc.SignalTrigger);

        await WaitUntilAsync(() => harness.Context.GetStep("Homogenization.Recipe") == 30);

        var recipeRecord = SingleRecordOfKind(harness.Pipeline, HomogenizationCellData.RecordKindRecipe);
        var recipeData = Assert.IsType<HomogenizationCellData>(recipeRecord.CellData);
        Assert.Equal(DataPipelineUploadTargets.All, recipeData.UploadTargets);
        Assert.Equal(77, recipeData.RecipeSnapshot!.StirringSpeed[0]);
        Assert.Equal(TestCodeOptions.Plc.AckOk, harness.ReadWriteWord(HomogenizationSignalTestProfile.SignalKey(HomogenizationPlcSignals.Interaction.工艺参数上传)));
        Assert.Equal("配方已进入 MES/Cloud 上传队列。", harness.Context.LastRecipeResult);
    }

    [Fact]
    public async Task Recipe_WhenCloudOnlyDataPipelineThrows_ShouldRecordCloudFailureWithoutMesDiagnostics()
    {
        await using var harness = HomogenizationRuntimeHarness.Create(
            pipeline: new CapturingDataPipelineService
            {
                ExceptionToThrow = new InvalidOperationException("本地队列异常")
            });
        harness.Parameters.MesEnabled = false;
        harness.Parameters.CloudEnabled = true;
        await harness.StartAsync();
        harness.Pipeline.Records.Clear();

        harness.SetWord(HomogenizationSignalTestProfile.SignalKey(HomogenizationPlcSignals.ContinuousRead.配方搅拌转速), 88);
        harness.SetWord(HomogenizationSignalTestProfile.SignalKey(HomogenizationPlcSignals.Interaction.工艺参数上传), TestCodeOptions.Plc.SignalTrigger);

        await WaitUntilAsync(() => harness.Context.GetStep("Homogenization.Recipe") == 30);

        Assert.Empty(harness.Pipeline.Records);
        Assert.Equal(TestCodeOptions.Plc.AckException, harness.ReadWriteWord(HomogenizationSignalTestProfile.SignalKey(HomogenizationPlcSignals.Interaction.工艺参数上传)));
        Assert.Contains("本地队列异常", harness.Context.LastRecipeResult, StringComparison.Ordinal);
        Assert.Null(harness.Diagnostics.Get(TestCodeOptions.Mes.Channels.Recipe));
        Assert.Equal(CloudCallOutcome.Exception, harness.CloudDiagnostics.Snapshot.LastOutcome);
        Assert.Equal("plc_recipe_enqueue_failed", harness.CloudDiagnostics.Snapshot.LastReasonCode);
        Assert.Equal("Homogenization.Recipe", harness.CloudDiagnostics.Snapshot.LastTaskKey);
        Assert.Equal("配方上传", harness.CloudDiagnostics.Snapshot.LastScenario);
    }

    [Fact]
    public async Task EquipmentStatus_WhenProductionGateRejects_ShouldStillEnqueueWithoutMainPlan()
    {
        await using var harness = HomogenizationRuntimeHarness.Create(
            productionGate: new RejectingHomogenizationProductionGate("MES 已启用，请先选择主批计划。"));
        await harness.StartAsync();
        harness.Pipeline.Records.Clear();
        harness.Context.PlanSessionId = "PLAN-STALE";
        harness.Context.TraceBatchNumber = "TRACE-STALE";
        harness.Context.SelectedProductionPlan = new ProductionPlanOption(
            "PLAN-STALE",
            "STALE-MAIN",
            "STALE-ORDER",
            "STALE-MATERIAL",
            "STALE-BATCH",
            "STALE-PRODUCT",
            "READY",
            "CG",
            "匀浆",
            "LINE-1",
            "一线",
            "100",
            "0",
            "pcs",
            "MODEL",
            string.Empty,
            string.Empty,
            new Dictionary<string, string>());

        harness.SetWord(HomogenizationSignalTestProfile.SignalKey(HomogenizationPlcSignals.SingleRead.设备状态值), 1);
        harness.SetWord(HomogenizationSignalTestProfile.SignalKey(HomogenizationPlcSignals.Interaction.设备状态上传), TestCodeOptions.Plc.SignalTrigger);
        await WaitUntilAsync(() => harness.Context.GetStep("Homogenization.EquipmentStatus") == 30);

        var statusRecord = SingleRecordOfKind(harness.Pipeline, HomogenizationCellData.RecordKindEquipmentStatus);
        var statusData = Assert.IsType<HomogenizationCellData>(statusRecord.CellData);
        Assert.Equal(1, statusData.EquipmentStatusSnapshot!.StatusCode);
        Assert.Equal(string.Empty, statusRecord.PlanSessionId);
        Assert.Equal(string.Empty, statusRecord.MainPlanCode);
        Assert.Equal(string.Empty, statusRecord.TraceBatchNumber);
        Assert.Equal(TestCodeOptions.Plc.AckOk, harness.ReadWriteWord(HomogenizationSignalTestProfile.SignalKey(HomogenizationPlcSignals.Interaction.设备状态上传)));
        Assert.Equal("设备状态已进入 MES 上传队列。", harness.Context.LastEquipmentStatusResult);
    }

    [Fact]
    public async Task EquipmentStatus_WhenMesDisabledAndCloudEnabled_ShouldEnqueueCloudOnlyWithoutMainPlan()
    {
        await using var harness = HomogenizationRuntimeHarness.Create();
        harness.Parameters.MesEnabled = false;
        harness.Parameters.CloudEnabled = true;
        await harness.StartAsync();
        harness.Pipeline.Records.Clear();

        harness.SetWord(HomogenizationSignalTestProfile.SignalKey(HomogenizationPlcSignals.SingleRead.设备状态值), 1);
        harness.SetWord(HomogenizationSignalTestProfile.SignalKey(HomogenizationPlcSignals.Interaction.设备状态上传), TestCodeOptions.Plc.SignalTrigger);
        await WaitUntilAsync(() => harness.Context.GetStep("Homogenization.EquipmentStatus") == 30);

        var statusRecord = SingleRecordOfKind(harness.Pipeline, HomogenizationCellData.RecordKindEquipmentStatus);
        var statusData = Assert.IsType<HomogenizationCellData>(statusRecord.CellData);
        Assert.Equal(DataPipelineUploadTargets.Cloud, statusData.UploadTargets);
        Assert.Equal(string.Empty, statusRecord.PlanSessionId);
        Assert.Equal(string.Empty, statusRecord.MainPlanCode);
        Assert.Equal(string.Empty, statusRecord.TraceBatchNumber);
        Assert.Equal(TestCodeOptions.Plc.AckOk, harness.ReadWriteWord(HomogenizationSignalTestProfile.SignalKey(HomogenizationPlcSignals.Interaction.设备状态上传)));
        Assert.Equal("设备状态已进入 Cloud 上传队列。", harness.Context.LastEquipmentStatusResult);
    }

    [Fact]
    public async Task EquipmentStatus_WhenCloudOnlyDataPipelineThrows_ShouldRecordCloudFailureWithoutMesDiagnostics()
    {
        await using var harness = HomogenizationRuntimeHarness.Create(
            pipeline: new CapturingDataPipelineService
            {
                ExceptionToThrow = new InvalidOperationException("本地队列异常")
            });
        harness.Parameters.MesEnabled = false;
        harness.Parameters.CloudEnabled = true;
        await harness.StartAsync();
        harness.Pipeline.Records.Clear();

        harness.SetWord(HomogenizationSignalTestProfile.SignalKey(HomogenizationPlcSignals.SingleRead.设备状态值), 1);
        harness.SetWord(HomogenizationSignalTestProfile.SignalKey(HomogenizationPlcSignals.Interaction.设备状态上传), TestCodeOptions.Plc.SignalTrigger);
        await WaitUntilAsync(() => string.Equals(
            harness.CloudDiagnostics.Snapshot.LastTaskKey,
            "Homogenization.EquipmentStatus",
            StringComparison.Ordinal));

        Assert.Empty(harness.Pipeline.Records);
        Assert.Equal(TestCodeOptions.Plc.AckException, harness.ReadWriteWord(HomogenizationSignalTestProfile.SignalKey(HomogenizationPlcSignals.Interaction.设备状态上传)));
        Assert.Contains("本地队列异常", harness.Context.LastEquipmentStatusResult, StringComparison.Ordinal);
        Assert.Null(harness.Diagnostics.Get(TestCodeOptions.Mes.Channels.EquipmentStatus));
        Assert.Equal(CloudCallOutcome.Exception, harness.CloudDiagnostics.Snapshot.LastOutcome);
        Assert.Equal("plc_equipment_status_enqueue_failed", harness.CloudDiagnostics.Snapshot.LastReasonCode);
        Assert.Equal("Homogenization.EquipmentStatus", harness.CloudDiagnostics.Snapshot.LastTaskKey);
        Assert.Equal("设备状态上传", harness.CloudDiagnostics.Snapshot.LastScenario);
    }

    [Fact]
    public async Task EquipmentStatus_WhenMesAndCloudEnabled_ShouldEnqueueAllTargetsWithoutMainPlan()
    {
        await using var harness = HomogenizationRuntimeHarness.Create();
        harness.Parameters.CloudEnabled = true;
        await harness.StartAsync();
        harness.Pipeline.Records.Clear();

        harness.SetWord(HomogenizationSignalTestProfile.SignalKey(HomogenizationPlcSignals.SingleRead.设备状态值), 1);
        harness.SetWord(HomogenizationSignalTestProfile.SignalKey(HomogenizationPlcSignals.Interaction.设备状态上传), TestCodeOptions.Plc.SignalTrigger);
        await WaitUntilAsync(() => harness.Context.GetStep("Homogenization.EquipmentStatus") == 30);

        var statusRecord = SingleRecordOfKind(harness.Pipeline, HomogenizationCellData.RecordKindEquipmentStatus);
        var statusData = Assert.IsType<HomogenizationCellData>(statusRecord.CellData);
        Assert.Equal(DataPipelineUploadTargets.All, statusData.UploadTargets);
        Assert.Equal(string.Empty, statusRecord.MainPlanCode);
        Assert.Equal(TestCodeOptions.Plc.AckOk, harness.ReadWriteWord(HomogenizationSignalTestProfile.SignalKey(HomogenizationPlcSignals.Interaction.设备状态上传)));
        Assert.Equal("设备状态已进入 MES/Cloud 上传队列。", harness.Context.LastEquipmentStatusResult);
    }

    [Fact]
    public async Task EquipmentStatus_WhenMesAndCloudDisabled_ShouldAckAndSkipQueue()
    {
        await using var harness = HomogenizationRuntimeHarness.Create();
        harness.Parameters.MesEnabled = false;
        harness.Parameters.CloudEnabled = false;
        await harness.StartAsync();
        harness.Pipeline.Records.Clear();

        harness.SetWord(HomogenizationSignalTestProfile.SignalKey(HomogenizationPlcSignals.SingleRead.设备状态值), 1);
        harness.SetWord(HomogenizationSignalTestProfile.SignalKey(HomogenizationPlcSignals.Interaction.设备状态上传), TestCodeOptions.Plc.SignalTrigger);
        await WaitUntilAsync(() => harness.Context.GetStep("Homogenization.EquipmentStatus") == 30);

        Assert.Empty(RecordsOfKind(harness.Pipeline, HomogenizationCellData.RecordKindEquipmentStatus));
        Assert.Equal(TestCodeOptions.Plc.AckOk, harness.ReadWriteWord(HomogenizationSignalTestProfile.SignalKey(HomogenizationPlcSignals.Interaction.设备状态上传)));
        Assert.Equal("MES/Cloud 上传已关闭，设备状态上传已跳过。", harness.Context.LastEquipmentStatusResult);
    }

    [Fact]
    public async Task EquipmentStatus_ShouldNotWriteCloudDeviceLog()
    {
        await using var harness = HomogenizationRuntimeHarness.Create();
        await harness.StartAsync();
        harness.Pipeline.Records.Clear();
        harness.Logger.Entries.Clear();

        harness.SetWord(HomogenizationSignalTestProfile.SignalKey(HomogenizationPlcSignals.SingleRead.设备状态值), 1);
        harness.SetWord(HomogenizationSignalTestProfile.SignalKey(HomogenizationPlcSignals.Interaction.设备状态上传), TestCodeOptions.Plc.SignalTrigger);
        await WaitUntilAsync(() => harness.Context.GetStep("Homogenization.EquipmentStatus") == 30);

        Assert.True(HasRecordOfKind(harness.Pipeline, HomogenizationCellData.RecordKindEquipmentStatus));
        Assert.DoesNotContain(
            harness.Logger.Entries,
            entry => entry.Message.Contains("设备状态采集", StringComparison.Ordinal)
                     || entry.Message.Contains("PLC/设备=PLC-H", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Realtime_WhenReady_ShouldEnqueueMesOnlyRecordWithoutStoppingRuntime()
    {
        await using var harness = HomogenizationRuntimeHarness.Create();

        harness.SetWord(HomogenizationSignalTestProfile.SignalKey(HomogenizationPlcSignals.SingleRead.实时搅拌转速), 101);
        harness.SetWord(HomogenizationSignalTestProfile.SignalKey(HomogenizationPlcSignals.SingleRead.实时温度), 27);
        await harness.StartAsync();

        await WaitUntilAsync(() => HasRecordOfKind(harness.Pipeline, HomogenizationCellData.RecordKindRealtime));

        Assert.NotNull(harness.Context.LastRealtimeSnapshot);
        Assert.Equal(101, harness.Context.LastRealtimeSnapshot!.StirringSpeed);
        Assert.Equal(27, harness.Context.LastRealtimeSnapshot.Temperature);
        var realtimeRecord = SingleRecordOfKind(harness.Pipeline, HomogenizationCellData.RecordKindRealtime);
        var realtimeData = Assert.IsType<HomogenizationCellData>(realtimeRecord.CellData);
        Assert.Same(harness.Context.LastRealtimeSnapshot, realtimeData.RealtimeSnapshot);
        Assert.Equal(DataPipelineUploadTargets.Mes, realtimeData.UploadTargets);
        Assert.Equal("实时数据已进入 MES 上传队列。", harness.Context.LastRealtimeResult);
    }

    [Fact]
    public async Task Realtime_WhenMesDisabledAndCloudEnabled_ShouldEnqueueCloudOnlyWithoutMainPlan()
    {
        await using var harness = HomogenizationRuntimeHarness.Create();
        harness.Parameters.MesEnabled = false;
        harness.Parameters.CloudEnabled = true;
        harness.SetWord(HomogenizationSignalTestProfile.SignalKey(HomogenizationPlcSignals.SingleRead.实时搅拌转速), 101);
        harness.SetWord(HomogenizationSignalTestProfile.SignalKey(HomogenizationPlcSignals.SingleRead.实时温度), 27);
        await harness.StartAsync();

        await WaitUntilAsync(() => HasRecordOfKind(harness.Pipeline, HomogenizationCellData.RecordKindRealtime));

        var realtimeRecord = SingleRecordOfKind(harness.Pipeline, HomogenizationCellData.RecordKindRealtime);
        var realtimeData = Assert.IsType<HomogenizationCellData>(realtimeRecord.CellData);
        Assert.Equal(DataPipelineUploadTargets.Cloud, realtimeData.UploadTargets);
        Assert.Equal(string.Empty, realtimeRecord.PlanSessionId);
        Assert.Equal(string.Empty, realtimeRecord.MainPlanCode);
        Assert.Equal(string.Empty, realtimeRecord.TraceBatchNumber);
        Assert.Equal("实时数据已进入 Cloud 上传队列。", harness.Context.LastRealtimeResult);
    }

    [Fact]
    public async Task Realtime_WhenCloudOnlyDataPipelineThrows_ShouldRecordCloudFailureWithoutMesDiagnostics()
    {
        await using var harness = HomogenizationRuntimeHarness.Create(
            pipeline: new CapturingDataPipelineService
            {
                ExceptionToThrow = new InvalidOperationException("本地队列异常")
            });
        harness.Parameters.MesEnabled = false;
        harness.Parameters.CloudEnabled = true;
        harness.SetWord(HomogenizationSignalTestProfile.SignalKey(HomogenizationPlcSignals.SingleRead.实时搅拌转速), 101);
        harness.SetWord(HomogenizationSignalTestProfile.SignalKey(HomogenizationPlcSignals.SingleRead.实时温度), 27);
        await harness.StartAsync();

        await WaitUntilAsync(() => harness.Context.LastRealtimeResult?.Contains("本地队列异常", StringComparison.Ordinal) == true);

        Assert.Empty(RecordsOfKind(harness.Pipeline, HomogenizationCellData.RecordKindRealtime));
        Assert.Null(harness.Context.LastRealtimeFingerprint);
        Assert.Null(harness.Diagnostics.Get(TestCodeOptions.Mes.Channels.Realtime));
        Assert.Equal(CloudCallOutcome.Exception, harness.CloudDiagnostics.Snapshot.LastOutcome);
        Assert.Equal("plc_realtime_enqueue_failed", harness.CloudDiagnostics.Snapshot.LastReasonCode);
        Assert.Equal("Homogenization.Realtime", harness.CloudDiagnostics.Snapshot.LastTaskKey);
        Assert.Equal("实时数据上传", harness.CloudDiagnostics.Snapshot.LastScenario);
    }

    [Fact]
    public async Task Realtime_WhenMesAndCloudEnabled_ShouldEnqueueAllTargets()
    {
        await using var harness = HomogenizationRuntimeHarness.Create();
        harness.Parameters.CloudEnabled = true;
        harness.SetWord(HomogenizationSignalTestProfile.SignalKey(HomogenizationPlcSignals.SingleRead.实时搅拌转速), 101);
        harness.SetWord(HomogenizationSignalTestProfile.SignalKey(HomogenizationPlcSignals.SingleRead.实时温度), 27);
        await harness.StartAsync();

        await WaitUntilAsync(() => HasRecordOfKind(harness.Pipeline, HomogenizationCellData.RecordKindRealtime));

        var realtimeRecord = SingleRecordOfKind(harness.Pipeline, HomogenizationCellData.RecordKindRealtime);
        var realtimeData = Assert.IsType<HomogenizationCellData>(realtimeRecord.CellData);
        Assert.Equal(DataPipelineUploadTargets.All, realtimeData.UploadTargets);
        Assert.Equal("实时数据已进入 MES/Cloud 上传队列。", harness.Context.LastRealtimeResult);
    }

    [Fact]
    public async Task Realtime_WhenMesAndCloudDisabled_ShouldSkipQueueAndKeepFingerprintUnset()
    {
        await using var harness = HomogenizationRuntimeHarness.Create();
        harness.Parameters.MesEnabled = false;
        harness.Parameters.CloudEnabled = false;
        harness.SetWord(HomogenizationSignalTestProfile.SignalKey(HomogenizationPlcSignals.SingleRead.实时搅拌转速), 101);
        harness.SetWord(HomogenizationSignalTestProfile.SignalKey(HomogenizationPlcSignals.SingleRead.实时温度), 27);
        await harness.StartAsync();

        await WaitUntilAsync(() => string.Equals(
            harness.Context.LastRealtimeResult,
            "MES/Cloud 上传已关闭，实时数据上传已跳过。",
            StringComparison.Ordinal));

        Assert.Empty(RecordsOfKind(harness.Pipeline, HomogenizationCellData.RecordKindRealtime));
        Assert.Null(harness.Context.LastRealtimeFingerprint);
    }

    [Fact]
    public async Task Realtime_WhenSnapshotUnchanged_ShouldSkipQueueUntilBufferValueChanges()
    {
        await using var harness = HomogenizationRuntimeHarness.Create(realtimeLoopIntervalMs: 20);

        harness.SetWord(HomogenizationSignalTestProfile.SignalKey(HomogenizationPlcSignals.SingleRead.实时搅拌转速), 101);
        harness.SetWord(HomogenizationSignalTestProfile.SignalKey(HomogenizationPlcSignals.SingleRead.实时温度), 27);
        await harness.StartAsync();

        await WaitUntilAsync(() => RecordsOfKind(harness.Pipeline, HomogenizationCellData.RecordKindRealtime).Count == 1);
        await WaitUntilAsync(() => string.Equals(
            harness.Context.LastRealtimeResult,
            "匀浆实时数据未变化，已跳过实时上传。",
            StringComparison.Ordinal));

        Assert.Single(RecordsOfKind(harness.Pipeline, HomogenizationCellData.RecordKindRealtime));

        harness.SetWord(HomogenizationSignalTestProfile.SignalKey(HomogenizationPlcSignals.SingleRead.实时温度), 28);

        await WaitUntilAsync(() =>
            RecordsOfKind(harness.Pipeline, HomogenizationCellData.RecordKindRealtime).Count == 2
            && string.Equals(harness.Context.LastRealtimeResult, "实时数据已进入 MES 上传队列。", StringComparison.Ordinal));

        var records = RecordsOfKind(harness.Pipeline, HomogenizationCellData.RecordKindRealtime);
        var secondData = Assert.IsType<HomogenizationCellData>(records[1].CellData);
        Assert.Equal(28, secondData.RealtimeSnapshot!.Temperature);
    }

    [Fact]
    public async Task Realtime_WhenMesAndCloudDisabledLegacyCase_ShouldSkipQueueAndKeepFingerprintUnset()
    {
        await using var harness = HomogenizationRuntimeHarness.Create(realtimeLoopIntervalMs: 20);
        harness.Parameters.MesEnabled = false;

        harness.SetWord(HomogenizationSignalTestProfile.SignalKey(HomogenizationPlcSignals.SingleRead.实时搅拌转速), 101);
        await harness.StartAsync();

        await WaitUntilAsync(() => string.Equals(
            harness.Context.LastRealtimeResult,
            "MES/Cloud 上传已关闭，实时数据上传已跳过。",
            StringComparison.Ordinal));

        Assert.Empty(RecordsOfKind(harness.Pipeline, HomogenizationCellData.RecordKindRealtime));
        Assert.Null(harness.Context.LastRealtimeFingerprint);
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

    private static CellCompletedRecord SingleRecordOfKind(
        CapturingDataPipelineService pipeline,
        string recordKind)
        => Assert.Single(RecordsOfKind(pipeline, recordKind));

    private static IReadOnlyList<CellCompletedRecord> RecordsOfKind(
        CapturingDataPipelineService pipeline,
        string recordKind)
        => pipeline.Records
            .Where(record => record is not null
                             && record.CellData is HomogenizationCellData cellData
                             && string.Equals(cellData.RecordKind, recordKind, StringComparison.Ordinal))
            .ToList();

    private static bool HasRecordOfKind(
        CapturingDataPipelineService pipeline,
        string recordKind)
        => RecordsOfKind(pipeline, recordKind).Count > 0;

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
            FakeCloudDiagnosticsStore cloudDiagnostics,
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
            CloudDiagnostics = cloudDiagnostics;
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

        public FakeCloudDiagnosticsStore CloudDiagnostics { get; }

        public FakeHomogenizationModuleParamProvider Parameters { get; }

        public FakeProductionTimeProvider ProductionTime { get; }

        public FakeDeviceService DeviceService { get; }

        public FakeLogService Logger { get; }

        public static HomogenizationRuntimeHarness Create(
            CapturingHomogenizationMesChannel? mes = null,
            CapturingDataPipelineService? pipeline = null,
            bool duplicateCheckEnabled = false,
            FakeProductionTimeProvider? productionTime = null,
            IHomogenizationProductionGate? productionGate = null,
            int realtimeLoopIntervalMs = 10_000)
        {
            mes ??= new CapturingHomogenizationMesChannel();
            pipeline ??= new CapturingDataPipelineService();
            productionTime ??= new FakeProductionTimeProvider();
            productionGate ??= new AllowAllHomogenizationProductionGate();

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
            var cloudDiagnostics = new FakeCloudDiagnosticsStore();
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
            services.AddSingleton<ICloudUploadDiagnosticsStore>(cloudDiagnostics);
            services.AddSingleton<IHomogenizationMesScenarioChannel>(mes);
            services.AddSingleton<IDataPipelineService>(pipeline);
            services.AddSingleton<IProductionTimeProvider>(productionTime);
            services.AddSingleton<IProductionContextSignalBindingStore>(signalBindingStore);
            services.AddSingleton<IModuleParamProvider<HomogenizationParams.Mes, HomogenizationParams.Cloud, HomogenizationParams.Business>>(parameters);
            services.AddSingleton(productionGate);
            services.AddSingleton(new HomogenizationCellDataValidator());
            services.AddSingleton(Options.Create(new HomogenizationModuleOptions
            {
                Runtime = new HomogenizationRuntimeOptions
                {
                    EventLoopIntervalMs = 20,
                    MinEventLoopIntervalMs = 10,
                    RealtimeLoopIntervalMs = realtimeLoopIntervalMs,
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
                cloudDiagnostics,
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
        public bool MesEnabled { get; set; } = true;

        public bool CloudEnabled { get; set; }

        public bool DuplicateCheckEnabled { get; set; }

        public int GetCallCount { get; private set; }

        public Task<ModuleParamSnapshot<HomogenizationParams.Mes, HomogenizationParams.Cloud, HomogenizationParams.Business>> GetAsync(
            CancellationToken cancellationToken = default)
        {
            GetCallCount++;
            return Task.FromResult(new ModuleParamSnapshot<HomogenizationParams.Mes, HomogenizationParams.Cloud, HomogenizationParams.Business>(
                "Homogenization",
                new ModuleParamGroup<HomogenizationParams.Mes>(
                    "Homogenization",
                    ModuleParamCategory.Mes,
                    new Dictionary<HomogenizationParams.Mes, string>
                    {
                        [HomogenizationParams.Mes.启用] = MesEnabled.ToString()
                    },
                    new Dictionary<HomogenizationParams.Mes, string?>
                    {
                        [HomogenizationParams.Mes.启用] = "true"
                    },
                    new Dictionary<HomogenizationParams.Mes, ParamValueKind>
                    {
                        [HomogenizationParams.Mes.启用] = ParamValueKind.Bool
                    },
                    warn: null),
                new ModuleParamGroup<HomogenizationParams.Cloud>(
                    "Homogenization",
                    ModuleParamCategory.Cloud,
                    new Dictionary<HomogenizationParams.Cloud, string>
                    {
                        [HomogenizationParams.Cloud.启用] = CloudEnabled.ToString()
                    },
                    new Dictionary<HomogenizationParams.Cloud, string?>
                    {
                        [HomogenizationParams.Cloud.启用] = "false"
                    },
                    new Dictionary<HomogenizationParams.Cloud, ParamValueKind>
                    {
                        [HomogenizationParams.Cloud.启用] = ParamValueKind.Bool
                    },
                    warn: null),
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

    private sealed class RejectingHomogenizationProductionGate(string message) : IHomogenizationProductionGate
    {
        public Task<MesCallResult> EnsureReadyAsync(
            HomogenizationContext context,
            CancellationToken cancellationToken = default)
            => Task.FromResult(MesCallResult.BusinessRejected(message));
    }
}
