using System.Data;
using IIoT.Edge.Application.Context;
using IIoT.Edge.Application.Abstractions.Modules;

namespace IIoT.Edge.Application.Features.Production.Monitor;

public record MonitorSnapshotRow(string DeviceName, string Name, string Value);

public enum MonitorSnapshotSource
{
    ProductionContext,
    RuntimeStatus,
    PlcConfiguration
}

public record DeviceMonitorSnapshot(
    int NetworkDeviceId,
    string DeviceName,
    MonitorSnapshotSource Source,
    bool HasPlcConfiguration,
    bool IsPlcConfigurationEnabled,
    string PlcEndpointText,
    IReadOnlyList<MonitorSnapshotRow> StepRows,
    IReadOnlyList<MonitorStateMachineTaskSnapshot> StateMachineTaskRows,
    IReadOnlyList<MonitorSnapshotRow> DeviceDataRows,
    IReadOnlyList<MonitorSnapshotRow> EquipmentStatusRows,
    IReadOnlyList<MonitorSnapshotRow> RealtimeRows,
    bool IsConnected,
    string LastConnectedAtText,
    string LastFailureAtText,
    string LastErrorText,
    string LastHeartbeatText,
    string LastUpdatedText,
    int CellCount,
    DataTable CellTable,
    IReadOnlyList<MonitorCellDebugSnapshot> CellDebugRows,
    CloudSyncDiagnosticsSnapshot CloudSync,
    MesSyncDiagnosticsSnapshot MesSync,
    ProductionContextPersistenceDiagnostics ContextPersistence);
