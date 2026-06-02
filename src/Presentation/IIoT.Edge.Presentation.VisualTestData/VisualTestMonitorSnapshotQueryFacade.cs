using System.Data;
using IIoT.Edge.Application.Abstractions.Device;
using IIoT.Edge.Application.Abstractions.Integration;
using IIoT.Edge.Application.Abstractions.Modules;
using IIoT.Edge.Application.Context;
using IIoT.Edge.Application.Features.Production.Monitor;

namespace IIoT.Edge.Presentation.VisualTestData;

/// <summary>
/// 实时监控视觉验收数据源，只生成 ViewModel 绑定快照，不参与真实运行时上下文、PLC 或上传链路。
/// </summary>
public sealed class VisualTestMonitorSnapshotQueryFacade(VisualTestDataOptions options) : IMonitorSnapshotQueryFacade
{
    public Task<List<DeviceMonitorSnapshot>> GetSnapshotsAsync(CancellationToken cancellationToken = default)
    {
        var now = DateTimeOffset.Now;
        var diagnostics = CreateDiagnostics();
        return Task.FromResult(new List<DeviceMonitorSnapshot>
        {
            CreateSnapshot(
                networkDeviceId: 9001,
                deviceName: options.PrimaryDeviceName,
                endpoint: "127.0.0.1:6000",
                connected: true,
                diagnostics,
                now,
                offset: 0),
            CreateSnapshot(
                networkDeviceId: 9002,
                deviceName: "PLC-Homogenization-02",
                endpoint: "127.0.0.1:6001",
                connected: now.Second % 12 < 9,
                diagnostics,
                now,
                offset: 1)
        });
    }

    private DeviceMonitorSnapshot CreateSnapshot(
        int networkDeviceId,
        string deviceName,
        string endpoint,
        bool connected,
        EdgeSyncDiagnosticsSnapshot diagnostics,
        DateTimeOffset now,
        int offset)
    {
        var tick = now.Second + offset * 3;
        var temperature = 41.8 + tick % 8 * 0.2;
        var speed = 610 + tick % 12;
        var vacuum = -88.0 - tick % 5 * 0.4;
        var cellTable = BuildCellTable(offset, tick);

        return new DeviceMonitorSnapshot(
            NetworkDeviceId: networkDeviceId,
            DeviceName: deviceName,
            Source: MonitorSnapshotSource.ProductionContext,
            HasPlcConfiguration: true,
            IsPlcConfigurationEnabled: true,
            PlcEndpointText: endpoint,
            StepRows:
            [
                new(deviceName, "Heartbeat", connected ? "30" : "10"),
                new(deviceName, "Realtime", "30"),
                new(deviceName, "Outbound", tick % 20 < 10 ? "10" : "30")
            ],
            StateMachineTaskRows:
            [
                new("Heartbeat", "心跳任务", true, true, true, connected ? 30 : 10, connected ? "等待 PLC 复位" : "处理中", string.Empty, true, 2, 0, "--"),
                new("Realtime", "实时数据采集", true, true, true, 30, "等待 PLC 复位", string.Empty, false, 4, 0, "--"),
                new("Outbound", "出料采集", true, true, true, tick % 20 < 10 ? 10 : 30, tick % 20 < 10 ? "处理中" : "等待 PLC 复位", string.Empty, false, 6, 0, "--")
            ],
            DeviceDataRows:
            [
                new(deviceName, "CurrentBatch", options.BatchCode),
                new(deviceName, "CurrentRecipe", "匀浆视觉验收配方 V2.3"),
                new(deviceName, "Operator", "VisualTest")
            ],
            EquipmentStatusRows:
            [
                new(deviceName, "RuntimeStatus", connected ? "Running" : "Idle"),
                new(deviceName, "Temperature", $"{temperature:F1} C"),
                new(deviceName, "Vacuum", $"{vacuum:F1} KPa"),
                new(deviceName, "StirringSpeed", $"{speed} RPM")
            ],
            RealtimeRows:
            [
                new(deviceName, "CntActualKg", $"{120.5 + tick % 9:F1}"),
                new(deviceName, "CntTargetKg", "128.0"),
                new(deviceName, "NmpActualKg", $"{82.0 + tick % 6:F1}"),
                new(deviceName, "GlueActualKg", $"{56.5 + tick % 5:F1}")
            ],
            IsConnected: connected,
            LastConnectedAtText: now.AddSeconds(-tick).ToString("HH:mm:ss.fff"),
            LastFailureAtText: connected ? "--" : now.AddSeconds(-18).ToString("HH:mm:ss.fff"),
            LastErrorText: connected ? "--" : "视觉测试：连接波动",
            LastHeartbeatText: now.AddMilliseconds(-300).ToString("HH:mm:ss.fff"),
            LastUpdatedText: now.ToString("HH:mm:ss.fff"),
            CellCount: cellTable.Rows.Count,
            CellTable: cellTable,
            CellDebugRows: BuildCellDebugRows(deviceName, offset, tick),
            CloudSync: diagnostics.Cloud,
            MesSync: diagnostics.Mes,
            ContextPersistence: diagnostics.ContextPersistence);
    }

    private static DataTable BuildCellTable(int offset, int tick)
    {
        var table = new DataTable();
        table.Columns.Add("TrayCode", typeof(string));
        table.Columns.Add("BatchNo", typeof(string));
        table.Columns.Add("RuntimeStatus", typeof(string));
        table.Columns.Add("Temperature", typeof(string));
        table.Columns.Add("CompletedTime", typeof(string));

        for (var index = 0; index < 8; index++)
        {
            var row = table.NewRow();
            row["TrayCode"] = $"TR-{offset + 1:D2}-{index + 1:D3}";
            row["BatchNo"] = $"CELL-{DateTime.Today:yyyyMMdd}-{offset + 1}{index + 1:D2}";
            row["RuntimeStatus"] = index % 5 == 0 ? "待复核" : "正常";
            row["Temperature"] = $"{41.5 + (tick + index) % 7 * 0.2:F1}";
            row["CompletedTime"] = DateTime.Now.AddMinutes(-index * 6).ToString("HH:mm:ss");
            table.Rows.Add(row);
        }

        return table;
    }

    private IReadOnlyList<MonitorCellDebugSnapshot> BuildCellDebugRows(string deviceName, int offset, int tick)
    {
        return Enumerable.Range(0, 6)
            .Select(index =>
            {
                var internalKey = $"VT-CELL-{offset + 1:D2}-{index + 1:D3}";
                var rows = new List<MonitorSnapshotRow>
                {
                    new(deviceName, "InternalKey", internalKey),
                    new(deviceName, "TrayCode", $"TR-{offset + 1:D2}-{index + 1:D3}"),
                    new(deviceName, "BatchNo", options.BatchCode),
                    new(deviceName, "RuntimeStatus", index % 4 == 0 ? "待复核" : "正常"),
                    new(deviceName, "CntActualKg", $"{120.5 + (tick + index) % 9:F1}"),
                    new(deviceName, "Temperature", $"{41.5 + (tick + index) % 7 * 0.2:F1}")
                };

                return new MonitorCellDebugSnapshot(
                    deviceName,
                    internalKey,
                    $"电芯 {index + 1:D2}",
                    "Homogenization",
                    index % 4 == 0 ? "待复核" : "正常",
                    DateTime.Now.AddMinutes(-index * 4).ToString("HH:mm:ss"),
                    rows);
            })
            .ToList();
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
            LastReasonCode: "VisualTest",
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
            Heartbeat: ExternalHeartbeatSnapshot.Unknown(ExternalSystemKind.Cloud, "VisualTest"),
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
            Heartbeat: ExternalHeartbeatSnapshot.Unknown(ExternalSystemKind.Mes, "VisualTest"),
            DeadLetters: DeadLetterDiagnosticsSnapshot.Empty);

        return new EdgeSyncDiagnosticsSnapshot(
            "VisualTest",
            cloud,
            mes,
            new ProductionContextPersistenceDiagnostics(0, null));
    }
}
