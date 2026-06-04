using IIoT.Edge.Application.Abstractions.Device;
using IIoT.Edge.Application.Abstractions.Modules;
using IIoT.Edge.Application.Context;
using IIoT.Edge.Application.Features.Production.Monitor;
using System.Globalization;

using IIoT.Edge.Application.Abstractions.Cloud;
using IIoT.Edge.Application.Abstractions.Mes;
using IIoT.Edge.Application.Abstractions.Shared;
namespace IIoT.Edge.Presentation.VisualTestData;

/// <summary>
/// 实时监控视觉验收数据源，只生成 ViewModel 绑定快照，不参与真实运行时上下文、PLC 或上传链路。
/// </summary>
public sealed class VisualTestMonitorSnapshotQueryFacade(VisualTestDataOptions options) : IMonitorSnapshotQueryFacade
{
    private static readonly string[] RuntimeStatuses = ["混料中", "待出料", "已出料", "待复核"];

    public Task<List<DeviceMonitorSnapshot>> GetSnapshotsAsync(CancellationToken cancellationToken = default)
    {
        var now = DateTimeOffset.Now;
        var diagnostics = CreateDiagnostics();
        return Task.FromResult(new List<DeviceMonitorSnapshot>
        {
            CreateSnapshot(
                networkDeviceId: 9001,
                deviceName: options.PrimaryDeviceName,
                lineName: "A 线",
                endpoint: "127.0.0.1:6000",
                connected: true,
                diagnostics,
                now,
                offset: 0),
            CreateSnapshot(
                networkDeviceId: 9002,
                deviceName: VisualTestScenario.SecondaryDeviceName,
                lineName: "B 线",
                endpoint: "127.0.0.1:6001",
                connected: true,
                diagnostics,
                now,
                offset: 1)
        });
    }

    private DeviceMonitorSnapshot CreateSnapshot(
        int networkDeviceId,
        string deviceName,
        string lineName,
        string endpoint,
        bool connected,
        EdgeSyncDiagnosticsSnapshot diagnostics,
        DateTimeOffset now,
        int offset)
    {
        var tick = now.Second + offset * 7;
        var batchCode = VisualTestScenario.ResolveBatchCode(options);
        var cells = BuildCellDebugRows(deviceName, lineName, batchCode, now, offset, tick);
        var temperature = 41.8 + (tick + offset) % 8 * 0.2;
        var speed = 610 + (tick + offset * 3) % 18;
        var vacuum = -88.0 - (tick + offset) % 5 * 0.4;
        var cntActual = 120.5 + (tick + offset) % 9 * 0.4;
        var nmpActual = 82.0 + (tick + offset) % 6 * 0.3;
        var glueActual = 56.5 + (tick + offset) % 5 * 0.2;

        return new DeviceMonitorSnapshot(
            NetworkDeviceId: networkDeviceId,
            DeviceName: deviceName,
            Source: MonitorSnapshotSource.ProductionContext,
            HasPlcConfiguration: true,
            IsPlcConfigurationEnabled: true,
            PlcEndpointText: endpoint,
            StepRows:
            [
                new(deviceName, "Heartbeat.Step", "30"),
                new(deviceName, "RealtimeSampling.Step", "10"),
                new(deviceName, "OutboundCapture.Step", tick % 20 < 12 ? "10" : "30"),
                new(deviceName, "TraceBatchCheck.Step", offset == 0 ? "0" : "30")
            ],
            StateMachineTaskRows: BuildStateMachineRows(connected, tick, offset),
            DeviceDataRows:
            [
                new(deviceName, "Line", lineName),
                new(deviceName, "CurrentRecipe", $"{VisualTestScenario.RecipeName} {VisualTestScenario.RecipeVersion}"),
                new(deviceName, "MainBatchPlan", VisualTestScenario.MainPlanCode),
                new(deviceName, "BatchNumber", batchCode),
                new(deviceName, "CurrentCellCount", cells.Count.ToString(CultureInfo.InvariantCulture))
            ],
            EquipmentStatusRows:
            [
                new(deviceName, "RuntimeStatus", "混料中"),
                new(deviceName, "Temperature", $"{temperature:F1} C"),
                new(deviceName, "Vacuum", $"{vacuum:F1} KPa"),
                new(deviceName, "StirringSpeed", $"{speed} RPM"),
                new(deviceName, "ActiveTank", offset == 0 ? "CNT A/B" : "NMP/Glue")
            ],
            RealtimeRows:
            [
                new(deviceName, "CntActualKg", $"{cntActual:F1}"),
                new(deviceName, "CntTargetKg", "128.0"),
                new(deviceName, "NmpActualKg", $"{nmpActual:F1}"),
                new(deviceName, "NmpTargetKg", "88.0"),
                new(deviceName, "GlueActualKg", $"{glueActual:F1}")
            ],
            IsConnected: connected,
            LastConnectedAtText: now.AddMinutes(-48 - offset * 9).ToString("HH:mm:ss"),
            LastFailureAtText: "--",
            LastErrorText: "--",
            LastHeartbeatText: now.AddMilliseconds(-220 - offset * 60).ToString("HH:mm:ss.fff"),
            LastUpdatedText: now.ToString("HH:mm:ss.fff"),
            CellCount: cells.Count,
            CellTable: BuildCellTable(cells),
            CellDebugRows: cells,
            CloudSync: diagnostics.Cloud,
            MesSync: diagnostics.Mes,
            ContextPersistence: diagnostics.ContextPersistence);
    }

    private static IReadOnlyList<MonitorStateMachineTaskSnapshot> BuildStateMachineRows(
        bool connected,
        int tick,
        int offset)
        =>
        [
            new(
                "Heartbeat",
                "PLC 心跳",
                true,
                connected,
                true,
                connected ? 30 : 10,
                connected ? "等待 PLC 复位" : "连接中断",
                connected ? string.Empty : "PLC 心跳不可用",
                true,
                2,
                connected ? 0 : 1,
                connected ? "--" : "Heartbeat.Read"),
            new(
                "CellInbound",
                "电芯入站采集",
                true,
                true,
                true,
                10,
                "处理中",
                string.Empty,
                false,
                6,
                0,
                "--"),
            new(
                "RealtimeSampling",
                "工艺实时采样",
                true,
                true,
                true,
                10,
                "处理中",
                string.Empty,
                false,
                8,
                0,
                "--"),
            new(
                "OutboundCapture",
                "出料记录生成",
                true,
                true,
                true,
                tick % 20 < 12 ? 10 : 30,
                tick % 20 < 12 ? "处理中" : "等待 PLC 复位",
                string.Empty,
                false,
                7,
                0,
                "--"),
            new(
                "TraceBatchCheck",
                "追溯批次校验",
                true,
                offset == 0,
                offset == 0,
                offset == 0 ? 0 : null,
                offset == 0 ? "等待触发" : "暂无步骤状态",
                offset == 0 ? string.Empty : "备用 PLC 未绑定追溯批次校验任务",
                false,
                3,
                offset == 0 ? 0 : 1,
                offset == 0 ? "--" : "TraceBatch.Check")
        ];

    private IReadOnlyList<MonitorCellDebugSnapshot> BuildCellDebugRows(
        string deviceName,
        string lineName,
        string batchCode,
        DateTimeOffset now,
        int offset,
        int tick)
    {
        var count = offset == 0 ? 9 : 6;
        return Enumerable.Range(0, count)
            .Select(index =>
            {
                var trayIndex = index + 1;
                var internalKey = $"HG-{offset + 1:D2}-CELL-{trayIndex:D3}";
                var displayLabel = $"{lineName} 电芯 {trayIndex:D2}";
                var status = RuntimeStatuses[(index + tick) % RuntimeStatuses.Length];
                var completedTime = status == "已出料"
                    ? now.AddMinutes(-index * 4 - 3).ToString("HH:mm:ss")
                    : "--";
                var cntActual = 120.2 + (tick + index) % 9 * 0.5;
                var temperature = 41.6 + (tick + index) % 7 * 0.2;

                return new MonitorCellDebugSnapshot(
                    deviceName,
                    internalKey,
                    displayLabel,
                    VisualTestScenario.ProcessName,
                    status,
                    completedTime,
                    BuildCellFieldRows(
                        deviceName,
                        internalKey,
                        displayLabel,
                        lineName,
                        batchCode,
                        status,
                        completedTime,
                        cntActual,
                        temperature,
                        now,
                        offset,
                        index));
            })
            .ToList();
    }

    private static IReadOnlyList<MonitorSnapshotRow> BuildCellFieldRows(
        string deviceName,
        string internalKey,
        string displayLabel,
        string lineName,
        string batchCode,
        string status,
        string completedTime,
        double cntActual,
        double temperature,
        DateTimeOffset now,
        int offset,
        int index)
        =>
        [
            new(deviceName, "CellData.InternalKey", internalKey),
            new(deviceName, "CellData.DisplayLabel", displayLabel),
            new(deviceName, "CellData.ProcessType", VisualTestScenario.ProcessName),
            new(deviceName, "CellData.Line", lineName),
            new(deviceName, "CellData.TrayCode", $"TR-HG-{offset + 1:D2}-{index + 1:D3}"),
            new(deviceName, "CellData.MainBatchPlan", VisualTestScenario.MainPlanCode),
            new(deviceName, "CellData.BatchNumber", $"{batchCode}-{index + 1:D2}"),
            new(deviceName, "CellData.RuntimeStatus", status),
            new(deviceName, "RealtimeSnapshot.StirringSpeed", (612 + index % 7 * 3).ToString(CultureInfo.InvariantCulture)),
            new(deviceName, "RealtimeSnapshot.Temperature", temperature.ToString("0.0", CultureInfo.InvariantCulture)),
            new(deviceName, "RealtimeSnapshot.Vacuum", (-88.0 - index % 5 * 0.4).ToString("0.0", CultureInfo.InvariantCulture)),
            new(deviceName, "Material.CntActualKg", cntActual.ToString("0.0", CultureInfo.InvariantCulture)),
            new(deviceName, "Material.CntTargetKg", "128.0"),
            new(deviceName, "Material.NmpActualKg", (82.0 + index % 6 * 0.4).ToString("0.0", CultureInfo.InvariantCulture)),
            new(deviceName, "Material.NmpTargetKg", "88.0"),
            new(deviceName, "Material.GlueActualKg", (56.0 + index % 5 * 0.3).ToString("0.0", CultureInfo.InvariantCulture)),
            new(deviceName, "Process.SetStirringTimeMinutes", "45"),
            new(deviceName, "Process.RemainingStirringTimeMinutes", Math.Max(0, 45 - index % 9 * 4).ToString(CultureInfo.InvariantCulture)),
            new(deviceName, "Process.SetDispersionTimeMinutes", "30"),
            new(deviceName, "Process.RemainingDispersionTimeMinutes", Math.Max(0, 30 - index % 6 * 5).ToString(CultureInfo.InvariantCulture)),
            new(deviceName, "CellData.InboundTime", now.AddMinutes(-index * 5 - 18).ToString("yyyy-MM-dd HH:mm:ss")),
            new(deviceName, "CellData.CompletedTime", completedTime),
            new(deviceName, "CellData.LastUpdatedAt", now.ToString("yyyy-MM-dd HH:mm:ss.fff"))
        ];

    private static MonitorCellTableSnapshot BuildCellTable(IReadOnlyList<MonitorCellDebugSnapshot> cells)
    {
        string[] columns =
        [
            "InternalKey",
            "DisplayLabel",
            "RuntimeStatus",
            "CompletedTime"
        ];

        var rows = cells
            .Select(static cell => new MonitorCellTableRow(
            [
                cell.InternalKey,
                cell.DisplayLabel,
                cell.RuntimeStatusText,
                cell.CompletedTimeText
            ]))
            .ToList();

        return new MonitorCellTableSnapshot(columns, rows);
    }

    private static EdgeSyncDiagnosticsSnapshot CreateDiagnostics()
    {
        var cloud = new CloudSyncDiagnosticsSnapshot(
            GateState: EdgeUploadGateState.Unknown,
            BlockReason: EdgeUploadBlockReason.DeviceUnidentified,
            RuntimeState: CloudRetryRuntimeState.Idle,
            LastAttemptAt: null,
            LastSuccessAt: null,
            LastFailureAt: null,
            LastOutcome: CloudCallOutcome.SkippedUploadNotReady,
            LastReasonCode: "VisualTestData",
            LastProcessType: null,
            PendingRetryCount: 0,
            PendingDeviceLogCount: 0,
            PendingCapacityCount: 0,
            IsPausedWaitingForRecovery: false,
            IsCapacityBlocked: false,
            BlockedChannel: null,
            BlockedReason: string.Empty,
            LastCapacityBlockAt: null,
            IsPersistenceFaulted: false,
            LastPersistenceFaultAt: null,
            PersistenceFaultMessage: null,
            Heartbeat: ExternalHeartbeatSnapshot.Unknown(ExternalSystemKind.Cloud, "VisualTestData"),
            DeadLetters: DeadLetterDiagnosticsSnapshot.Empty);

        var mes = new MesSyncDiagnosticsSnapshot(
            RuntimeState: MesRetryRuntimeState.Idle,
            LastAttemptAt: null,
            LastSuccessAt: null,
            LastFailureAt: null,
            LastFailureReason: null,
            PendingRetryCount: 0,
            Channels: [],
            IsCapacityBlocked: false,
            BlockedChannel: null,
            BlockedReason: string.Empty,
            LastCapacityBlockAt: null,
            IsPersistenceFaulted: false,
            LastPersistenceFaultAt: null,
            PersistenceFaultMessage: null,
            Heartbeat: ExternalHeartbeatSnapshot.Unknown(ExternalSystemKind.Mes, "VisualTestData"),
            DeadLetters: DeadLetterDiagnosticsSnapshot.Empty);

        return new EdgeSyncDiagnosticsSnapshot(
            "VisualTestData",
            cloud,
            mes,
            new ProductionContextPersistenceDiagnostics(0, null));
    }
}
