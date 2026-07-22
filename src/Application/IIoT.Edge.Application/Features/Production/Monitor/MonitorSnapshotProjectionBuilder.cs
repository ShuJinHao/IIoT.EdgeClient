using System.Globalization;
using IIoT.Edge.Module.Contracts.Modules;
using IIoT.Edge.Module.Contracts.Plc;
using IIoT.Edge.Module.Contracts.Time;
using IIoT.Edge.Application.Features.Hardware.PlcTaskBindings;
using IIoT.Edge.Domain.Hardware.Aggregates;
using IIoT.Edge.Module.Contracts.Runtime;

namespace IIoT.Edge.Application.Features.Production.Monitor;

internal sealed class MonitorSnapshotProjectionBuilder(
    IProductionTimeProvider productionTime,
    IMonitorStateMachineTaskProjection stateMachineTaskProjection)
    : IMonitorSnapshotProjectionBuilder
{
    private static IReadOnlyDictionary<string, int> EmptyStepStates { get; } =
        new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

    public DeviceMonitorSnapshot BuildContextSnapshot(
        ProductionContext context,
        PlcConnectionRuntimeSnapshot? runtimeStatus,
        NetworkDeviceEntity? configuredDevice,
        EdgeSyncDiagnosticsSnapshot diagnostics,
        IReadOnlyDictionary<int, PlcTaskBindingDeviceDto> taskBindingsByDevice)
    {
        var stepRows = context.StepStates
            .OrderBy(kv => kv.Key)
            .Select(kv => new MonitorSnapshotRow(
                context.DeviceName,
                kv.Key,
                kv.Value.ToString(CultureInfo.InvariantCulture)))
            .ToList();

        var deviceRows =
            context.DeviceBag.OrderBy(kv => kv.Key)
                .Select(kv => new MonitorSnapshotRow(
                    context.DeviceName,
                    kv.Key,
                    MonitorValueFormatting.FormatValue(kv.Value, productionTime)))
                .ToList();
        var equipmentStatusRows = MonitorValueFormatting.BuildContextProjectionRows(
            context,
            productionTime,
            "LastEquipmentStatusSnapshot",
            "LastEquipmentStatusAt",
            "LastEquipmentStatusResult");
        var realtimeRows = MonitorValueFormatting.BuildContextProjectionRows(
            context,
            productionTime,
            "LastRealtimeSnapshot",
            "LastRealtimeAt",
            "LastRealtimeResult");

        return new DeviceMonitorSnapshot(
            NetworkDeviceId: MonitorDeviceIdentityHelper.ResolveNetworkDeviceId(
                context.NetworkDeviceId,
                runtimeStatus,
                configuredDevice),
            DeviceName: MonitorDeviceIdentityHelper.ResolveDeviceName(
                context.DeviceName,
                runtimeStatus,
                configuredDevice),
            Source: MonitorSnapshotSource.ProductionContext,
            HasPlcConfiguration: configuredDevice is not null,
            IsPlcConfigurationEnabled: configuredDevice?.IsEnabled == true,
            PlcEndpointText: MonitorDeviceIdentityHelper.FormatEndpoint(configuredDevice),
            StepRows: stepRows,
            StateMachineTaskRows: stateMachineTaskProjection.BuildRows(
                configuredDevice,
                context.StepStates,
                taskBindingsByDevice),
            DeviceDataRows: deviceRows,
            EquipmentStatusRows: equipmentStatusRows,
            RealtimeRows: realtimeRows,
            IsConnected: runtimeStatus?.IsConnected == true,
            ConnectionState: runtimeStatus?.ConnectionState ?? PlcConnectionState.Unknown,
            LastConnectedAtText: MonitorValueFormatting.FormatTimestamp(runtimeStatus?.LastConnectedAtUtc, productionTime),
            LastFailureAtText: MonitorValueFormatting.FormatTimestamp(runtimeStatus?.LastFailureAtUtc, productionTime),
            LastErrorText: string.IsNullOrWhiteSpace(runtimeStatus?.LastError) ? "--" : runtimeStatus.LastError!,
            LastHeartbeatText: MonitorValueFormatting.FormatTimestamp(
                MonitorValueFormatting.FindLastHeartbeat(context),
                productionTime),
            LastUpdatedText: MonitorValueFormatting.FormatTimestamp(
                MonitorValueFormatting.FindLastUpdated(context),
                productionTime),
            CellCount: context.CurrentCells.Count,
            CellTable: MonitorValueFormatting.BuildCellTable(context, productionTime),
            CellDebugRows: MonitorCellDebugProjection.Build(context, productionTime),
            CloudSync: diagnostics.Cloud,
            MesSync: diagnostics.Mes,
            ContextPersistence: diagnostics.ContextPersistence);
    }

    public DeviceMonitorSnapshot BuildRuntimeOnlySnapshot(
        PlcConnectionRuntimeSnapshot runtimeStatus,
        NetworkDeviceEntity? configuredDevice,
        EdgeSyncDiagnosticsSnapshot diagnostics,
        IReadOnlyDictionary<int, PlcTaskBindingDeviceDto> taskBindingsByDevice)
    {
        var latestRuntimeTimestamp = MonitorDeviceIdentityHelper.ResolveLatestRuntimeTimestamp(runtimeStatus);

        return new DeviceMonitorSnapshot(
            NetworkDeviceId: MonitorDeviceIdentityHelper.ResolveNetworkDeviceId(0, runtimeStatus, configuredDevice),
            DeviceName: MonitorDeviceIdentityHelper.ResolveDeviceName(null, runtimeStatus, configuredDevice),
            Source: MonitorSnapshotSource.RuntimeStatus,
            HasPlcConfiguration: configuredDevice is not null,
            IsPlcConfigurationEnabled: configuredDevice?.IsEnabled == true,
            PlcEndpointText: MonitorDeviceIdentityHelper.FormatEndpoint(configuredDevice),
            StepRows: [],
            StateMachineTaskRows: stateMachineTaskProjection.BuildRows(
                configuredDevice,
                EmptyStepStates,
                taskBindingsByDevice),
            DeviceDataRows: [],
            EquipmentStatusRows: [],
            RealtimeRows: [],
            IsConnected: runtimeStatus.IsConnected,
            ConnectionState: runtimeStatus.ConnectionState,
            LastConnectedAtText: MonitorValueFormatting.FormatTimestamp(runtimeStatus.LastConnectedAtUtc, productionTime),
            LastFailureAtText: MonitorValueFormatting.FormatTimestamp(runtimeStatus.LastFailureAtUtc, productionTime),
            LastErrorText: string.IsNullOrWhiteSpace(runtimeStatus.LastError) ? "--" : runtimeStatus.LastError!,
            LastHeartbeatText: "--",
            LastUpdatedText: MonitorValueFormatting.FormatTimestamp(latestRuntimeTimestamp, productionTime),
            CellCount: 0,
            CellTable: MonitorCellTableSnapshot.Empty,
            CellDebugRows: [],
            CloudSync: diagnostics.Cloud,
            MesSync: diagnostics.Mes,
            ContextPersistence: diagnostics.ContextPersistence);
    }

    public DeviceMonitorSnapshot BuildConfiguredDeviceSnapshot(
        NetworkDeviceEntity device,
        EdgeSyncDiagnosticsSnapshot diagnostics,
        IReadOnlyDictionary<int, PlcTaskBindingDeviceDto> taskBindingsByDevice)
        => new(
            NetworkDeviceId: device.Id,
            DeviceName: device.DeviceName,
            Source: MonitorSnapshotSource.PlcConfiguration,
            HasPlcConfiguration: true,
            IsPlcConfigurationEnabled: device.IsEnabled,
            PlcEndpointText: MonitorDeviceIdentityHelper.FormatEndpoint(device),
            StepRows: [],
            StateMachineTaskRows: stateMachineTaskProjection.BuildRows(
                device,
                EmptyStepStates,
                taskBindingsByDevice),
            DeviceDataRows: [],
            EquipmentStatusRows: [],
            RealtimeRows: [],
            IsConnected: false,
            ConnectionState: PlcConnectionState.Disconnected,
            LastConnectedAtText: "--",
            LastFailureAtText: "--",
            LastErrorText: "--",
            LastHeartbeatText: "--",
            LastUpdatedText: "--",
            CellCount: 0,
            CellTable: MonitorCellTableSnapshot.Empty,
            CellDebugRows: [],
            CloudSync: diagnostics.Cloud,
            MesSync: diagnostics.Mes,
            ContextPersistence: diagnostics.ContextPersistence);
}
