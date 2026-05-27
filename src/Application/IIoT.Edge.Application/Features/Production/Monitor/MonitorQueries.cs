using IIoT.Edge.Application.Context;
using IIoT.Edge.Application.Abstractions.Context;
using IIoT.Edge.Application.Abstractions.Modules;
using IIoT.Edge.Application.Abstractions.Plc;
using IIoT.Edge.Application.Abstractions.Time;
using IIoT.Edge.Application.Features.Hardware.PlcTaskBindings;
using IIoT.Edge.Application.Features.Hardware.Queries;
using IIoT.Edge.Domain.Hardware.Aggregates;
using IIoT.Edge.SharedKernel.Context;
using IIoT.Edge.SharedKernel.DataPipeline.CellData;
using IIoT.Edge.SharedKernel.Enums;
using MediatR;
using System.Collections;
using System.Data;
using System.Globalization;
using System.Reflection;
using System.Text.Json;

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

public record GetMonitorSnapshotQuery : IRequest<List<DeviceMonitorSnapshot>>;

public class GetMonitorSnapshotHandler(
    IProductionContextStore contextStore,
    IEdgeSyncDiagnosticsQuery diagnosticsQuery,
    IProductionTimeProvider productionTime,
    IPlcConnectionManager plcConnectionManager,
    IStationRuntimeRegistry runtimeRegistry,
    IPlcTaskBindingService taskBindingService,
    ISender sender)
    : IRequestHandler<GetMonitorSnapshotQuery, List<DeviceMonitorSnapshot>>
{
    public async Task<List<DeviceMonitorSnapshot>> Handle(GetMonitorSnapshotQuery request, CancellationToken ct)
    {
        var diagnostics = await diagnosticsQuery.GetCurrentAsync(ct).ConfigureAwait(false);
        var result = new List<DeviceMonitorSnapshot>();
        var contexts = contextStore.GetAll().ToList();
        var runtimeStatuses = plcConnectionManager?.GetRuntimeStatuses() ?? [];
        var configuredPlcs = await LoadConfiguredPlcDevicesAsync(ct).ConfigureAwait(false);
        var taskBindingsByDevice = await LoadTaskBindingsByDeviceAsync(configuredPlcs, ct).ConfigureAwait(false);

        foreach (var ctx in contexts)
        {
            var runtimeStatus = ResolveRuntimeStatus(ctx);
            var configuredDevice = ResolveConfiguredDevice(ctx, configuredPlcs);
            var stepRows = ctx.StepStates
                .OrderBy(kv => kv.Key)
                .Select(kv => new MonitorSnapshotRow(
                    ctx.DeviceName,
                    kv.Key,
                    kv.Value.ToString(CultureInfo.InvariantCulture)))
                .ToList();

            var deviceRows =
                ctx.DeviceBag.OrderBy(kv => kv.Key)
                    .Select(kv => new MonitorSnapshotRow(ctx.DeviceName, kv.Key, FormatValue(kv.Value, productionTime)))
                    .ToList();
            var equipmentStatusRows = BuildContextProjectionRows(
                ctx,
                productionTime,
                "LastEquipmentStatusSnapshot",
                "LastEquipmentStatusAt",
                "LastEquipmentStatusResult");
            var realtimeRows = BuildContextProjectionRows(
                ctx,
                productionTime,
                "LastRealtimeSnapshot",
                "LastRealtimeAt",
                "LastRealtimeResult");

            result.Add(new DeviceMonitorSnapshot(
                NetworkDeviceId: ResolveNetworkDeviceId(ctx.NetworkDeviceId, runtimeStatus, configuredDevice),
                DeviceName: ResolveDeviceName(ctx.DeviceName, runtimeStatus, configuredDevice),
                Source: MonitorSnapshotSource.ProductionContext,
                HasPlcConfiguration: configuredDevice is not null,
                IsPlcConfigurationEnabled: configuredDevice?.IsEnabled == true,
                PlcEndpointText: FormatEndpoint(configuredDevice),
                StepRows: stepRows,
                StateMachineTaskRows: BuildStateMachineTaskRows(configuredDevice, ctx.StepStates, taskBindingsByDevice),
                DeviceDataRows: deviceRows,
                EquipmentStatusRows: equipmentStatusRows,
                RealtimeRows: realtimeRows,
                IsConnected: runtimeStatus?.IsConnected == true,
                LastConnectedAtText: FormatTimestamp(runtimeStatus?.LastConnectedAtUtc, productionTime),
                LastFailureAtText: FormatTimestamp(runtimeStatus?.LastFailureAtUtc, productionTime),
                LastErrorText: string.IsNullOrWhiteSpace(runtimeStatus?.LastError) ? "--" : runtimeStatus.LastError!,
                LastHeartbeatText: FormatTimestamp(FindLastHeartbeat(ctx), productionTime),
                LastUpdatedText: FormatTimestamp(FindLastUpdated(ctx), productionTime),
                CellCount: ctx.CurrentCells.Count,
                CellTable: BuildCellTable(ctx, productionTime),
                CellDebugRows: MonitorCellDebugProjection.Build(ctx, productionTime),
                CloudSync: diagnostics.Cloud,
                MesSync: diagnostics.Mes,
                ContextPersistence: diagnostics.ContextPersistence));
        }

        foreach (var runtimeStatus in runtimeStatuses
            .Where(runtimeStatus => !HasContextForRuntimeStatus(contexts, runtimeStatus))
            .GroupBy(RuntimeStatusKey)
            .Select(static group => group.First()))
        {
            var configuredDevice = ResolveConfiguredDevice(runtimeStatus, configuredPlcs);
            result.Add(BuildRuntimeOnlySnapshot(runtimeStatus, configuredDevice, diagnostics, taskBindingsByDevice));
        }

        foreach (var device in configuredPlcs
            .Where(device => !HasMonitorSourceForConfiguredDevice(contexts, runtimeStatuses, device))
            .GroupBy(ConfiguredDeviceKey)
            .Select(static group => group.First()))
        {
            result.Add(BuildConfiguredDeviceSnapshot(device, diagnostics, taskBindingsByDevice));
        }

        return result;
    }

    private async Task<IReadOnlyDictionary<int, PlcTaskBindingDeviceDto>> LoadTaskBindingsByDeviceAsync(
        IReadOnlyCollection<NetworkDeviceEntity> configuredPlcs,
        CancellationToken ct)
    {
        var result = new Dictionary<int, PlcTaskBindingDeviceDto>();
        var moduleIds = configuredPlcs
            .Select(static device => device.ModuleId)
            .Where(static moduleId => !string.IsNullOrWhiteSpace(moduleId))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        foreach (var moduleId in moduleIds)
        {
            if (!runtimeRegistry.TryGetFactory(moduleId, out _))
            {
                continue;
            }

            var moduleBindings = await taskBindingService
                .GetModuleDeviceBindingsAsync(moduleId, ct)
                .ConfigureAwait(false);
            foreach (var deviceBinding in moduleBindings)
            {
                result[deviceBinding.NetworkDeviceId] = deviceBinding;
            }
        }

        return result;
    }

    private IReadOnlyList<MonitorStateMachineTaskSnapshot> BuildStateMachineTaskRows(
        NetworkDeviceEntity? device,
        IReadOnlyDictionary<string, int> stepStates,
        IReadOnlyDictionary<int, PlcTaskBindingDeviceDto> taskBindingsByDevice)
    {
        if (device is null
            || string.IsNullOrWhiteSpace(device.ModuleId)
            || !runtimeRegistry.TryGetFactory(device.ModuleId, out _)
            || !taskBindingsByDevice.TryGetValue(device.Id, out var deviceBinding))
        {
            return [];
        }

        return deviceBinding.Tasks
            .Select(task =>
            {
                var stepValue = stepStates.TryGetValue(task.Key, out var value)
                    ? value
                    : (int?)null;
                return new MonitorStateMachineTaskSnapshot(
                    Key: task.Key,
                    DisplayName: task.DisplayName,
                    Enabled: task.Enabled,
                    CanRun: task.CanRun,
                    HasSavedBinding: task.HasSavedBinding,
                    StepValue: stepValue,
                    StepText: FormatStateMachineStepText(stepValue),
                    UnavailableReason: task.CanRun ? string.Empty : task.UnavailableReason,
                    IsHeartbeatLike: task.IsHeartbeatLike,
                    RequiredSignalCount: task.RequiredSignals.Count,
                    MissingRequiredSignalCount: task.MissingRequiredSignals.Count,
                    MissingRequiredSignalsSummary: FormatRequiredSignalSummary(task.MissingRequiredSignals));
            })
            .ToList();
    }

    private async Task<IReadOnlyList<NetworkDeviceEntity>> LoadConfiguredPlcDevicesAsync(CancellationToken ct)
    {
        var devicesResult = await sender.Send(new GetAllNetworkDevicesQuery(), ct).ConfigureAwait(false);
        if (!devicesResult.IsSuccess || devicesResult.Value is null)
        {
            return [];
        }

        return devicesResult.Value
            .Where(static device =>
                device.DeviceType == DeviceType.PLC
                && !string.IsNullOrWhiteSpace(device.DeviceName))
            .OrderBy(static device => device.DeviceName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private PlcConnectionRuntimeSnapshot? ResolveRuntimeStatus(ProductionContext context)
    {
        if (context.NetworkDeviceId > 0)
        {
            var byId = plcConnectionManager.GetRuntimeStatus(context.NetworkDeviceId);
            if (byId is not null)
            {
                return byId;
            }
        }

        return plcConnectionManager.GetRuntimeStatuses()
            .FirstOrDefault(snapshot =>
                string.Equals(snapshot.DeviceName, context.DeviceName, StringComparison.OrdinalIgnoreCase));
    }

    private static bool HasContextForRuntimeStatus(
        IReadOnlyCollection<ProductionContext> contexts,
        PlcConnectionRuntimeSnapshot runtimeStatus)
        => contexts.Any(context =>
            runtimeStatus.NetworkDeviceId > 0
                && context.NetworkDeviceId == runtimeStatus.NetworkDeviceId
            || !string.IsNullOrWhiteSpace(runtimeStatus.DeviceName)
                && string.Equals(context.DeviceName, runtimeStatus.DeviceName, StringComparison.OrdinalIgnoreCase));

    private static string RuntimeStatusKey(PlcConnectionRuntimeSnapshot runtimeStatus)
        => runtimeStatus.NetworkDeviceId > 0
            ? $"id:{runtimeStatus.NetworkDeviceId.ToString(CultureInfo.InvariantCulture)}"
            : $"name:{runtimeStatus.DeviceName}";

    private static bool HasMonitorSourceForConfiguredDevice(
        IReadOnlyCollection<ProductionContext> contexts,
        IReadOnlyCollection<PlcConnectionRuntimeSnapshot> runtimeStatuses,
        NetworkDeviceEntity device)
        => contexts.Any(context => MatchesConfiguredDevice(context, device))
            || runtimeStatuses.Any(runtimeStatus => MatchesConfiguredDevice(runtimeStatus, device));

    private static bool MatchesConfiguredDevice(ProductionContext context, NetworkDeviceEntity device)
        => (device.Id > 0
                && context.NetworkDeviceId == device.Id)
            || string.Equals(context.DeviceName, device.DeviceName, StringComparison.OrdinalIgnoreCase);

    private static bool MatchesConfiguredDevice(PlcConnectionRuntimeSnapshot runtimeStatus, NetworkDeviceEntity device)
        => (device.Id > 0
                && runtimeStatus.NetworkDeviceId == device.Id)
            || string.Equals(runtimeStatus.DeviceName, device.DeviceName, StringComparison.OrdinalIgnoreCase);

    private static string ConfiguredDeviceKey(NetworkDeviceEntity device)
        => device.Id > 0
            ? $"id:{device.Id.ToString(CultureInfo.InvariantCulture)}"
            : $"name:{device.DeviceName}";

    private DeviceMonitorSnapshot BuildRuntimeOnlySnapshot(
        PlcConnectionRuntimeSnapshot runtimeStatus,
        NetworkDeviceEntity? configuredDevice,
        EdgeSyncDiagnosticsSnapshot diagnostics,
        IReadOnlyDictionary<int, PlcTaskBindingDeviceDto> taskBindingsByDevice)
    {
        var latestRuntimeTimestamp = ResolveLatestRuntimeTimestamp(runtimeStatus);

        return new DeviceMonitorSnapshot(
            NetworkDeviceId: ResolveNetworkDeviceId(0, runtimeStatus, configuredDevice),
            DeviceName: ResolveDeviceName(null, runtimeStatus, configuredDevice),
            Source: MonitorSnapshotSource.RuntimeStatus,
            HasPlcConfiguration: configuredDevice is not null,
            IsPlcConfigurationEnabled: configuredDevice?.IsEnabled == true,
            PlcEndpointText: FormatEndpoint(configuredDevice),
            StepRows: [],
            StateMachineTaskRows: BuildStateMachineTaskRows(configuredDevice, EmptyStepStates, taskBindingsByDevice),
            DeviceDataRows: [],
            EquipmentStatusRows: [],
            RealtimeRows: [],
            IsConnected: runtimeStatus.IsConnected,
            LastConnectedAtText: FormatTimestamp(runtimeStatus.LastConnectedAtUtc, productionTime),
            LastFailureAtText: FormatTimestamp(runtimeStatus.LastFailureAtUtc, productionTime),
            LastErrorText: string.IsNullOrWhiteSpace(runtimeStatus.LastError) ? "--" : runtimeStatus.LastError!,
            LastHeartbeatText: "--",
            LastUpdatedText: FormatTimestamp(latestRuntimeTimestamp, productionTime),
            CellCount: 0,
            CellTable: new DataTable(),
            CellDebugRows: [],
            CloudSync: diagnostics.Cloud,
            MesSync: diagnostics.Mes,
            ContextPersistence: diagnostics.ContextPersistence);
    }

    private DeviceMonitorSnapshot BuildConfiguredDeviceSnapshot(
        NetworkDeviceEntity device,
        EdgeSyncDiagnosticsSnapshot diagnostics,
        IReadOnlyDictionary<int, PlcTaskBindingDeviceDto> taskBindingsByDevice)
        => new(
            NetworkDeviceId: device.Id,
            DeviceName: device.DeviceName,
            Source: MonitorSnapshotSource.PlcConfiguration,
            HasPlcConfiguration: true,
            IsPlcConfigurationEnabled: device.IsEnabled,
            PlcEndpointText: FormatEndpoint(device),
            StepRows: [],
            StateMachineTaskRows: BuildStateMachineTaskRows(device, EmptyStepStates, taskBindingsByDevice),
            DeviceDataRows: [],
            EquipmentStatusRows: [],
            RealtimeRows: [],
            IsConnected: false,
            LastConnectedAtText: "--",
            LastFailureAtText: "--",
            LastErrorText: "--",
            LastHeartbeatText: "--",
            LastUpdatedText: "--",
            CellCount: 0,
            CellTable: new DataTable(),
            CellDebugRows: [],
            CloudSync: diagnostics.Cloud,
            MesSync: diagnostics.Mes,
            ContextPersistence: diagnostics.ContextPersistence);

    private static NetworkDeviceEntity? ResolveConfiguredDevice(
        ProductionContext context,
        IReadOnlyList<NetworkDeviceEntity> configuredPlcs)
    {
        if (context.NetworkDeviceId > 0)
        {
            var byId = configuredPlcs.FirstOrDefault(device => device.Id == context.NetworkDeviceId);
            if (byId is not null)
            {
                return byId;
            }
        }

        return configuredPlcs.FirstOrDefault(device =>
            string.Equals(device.DeviceName, context.DeviceName, StringComparison.OrdinalIgnoreCase));
    }

    private static NetworkDeviceEntity? ResolveConfiguredDevice(
        PlcConnectionRuntimeSnapshot runtimeStatus,
        IReadOnlyList<NetworkDeviceEntity> configuredPlcs)
    {
        if (runtimeStatus.NetworkDeviceId > 0)
        {
            var byId = configuredPlcs.FirstOrDefault(device => device.Id == runtimeStatus.NetworkDeviceId);
            if (byId is not null)
            {
                return byId;
            }
        }

        return configuredPlcs.FirstOrDefault(device =>
            string.Equals(device.DeviceName, runtimeStatus.DeviceName, StringComparison.OrdinalIgnoreCase));
    }

    private static int ResolveNetworkDeviceId(
        int contextNetworkDeviceId,
        PlcConnectionRuntimeSnapshot? runtimeStatus,
        NetworkDeviceEntity? configuredDevice)
    {
        if (contextNetworkDeviceId > 0)
        {
            return contextNetworkDeviceId;
        }

        if (runtimeStatus?.NetworkDeviceId > 0)
        {
            return runtimeStatus.NetworkDeviceId;
        }

        return configuredDevice?.Id ?? 0;
    }

    private static string ResolveDeviceName(
        string? contextDeviceName,
        PlcConnectionRuntimeSnapshot? runtimeStatus,
        NetworkDeviceEntity? configuredDevice)
    {
        if (!string.IsNullOrWhiteSpace(contextDeviceName))
        {
            return contextDeviceName;
        }

        if (!string.IsNullOrWhiteSpace(runtimeStatus?.DeviceName))
        {
            return runtimeStatus.DeviceName;
        }

        if (!string.IsNullOrWhiteSpace(configuredDevice?.DeviceName))
        {
            return configuredDevice.DeviceName;
        }

        return runtimeStatus?.NetworkDeviceId > 0
            ? runtimeStatus.NetworkDeviceId.ToString(CultureInfo.InvariantCulture)
            : "--";
    }

    private static string FormatEndpoint(NetworkDeviceEntity? device)
    {
        if (device is null)
        {
            return "--";
        }

        var endpoint = $"{device.IpAddress}:{device.Port1.ToString(CultureInfo.InvariantCulture)}";
        return device.Port2.HasValue
            ? $"{endpoint}/{device.Port2.Value.ToString(CultureInfo.InvariantCulture)}"
            : endpoint;
    }

    private static DateTimeOffset? ResolveLatestRuntimeTimestamp(PlcConnectionRuntimeSnapshot runtimeStatus)
    {
        var candidates = new[]
            {
                runtimeStatus.LastConnectedAtUtc,
                runtimeStatus.LastFailureAtUtc
            }
            .Where(static value => value.HasValue && value.Value.Year > 1900)
            .Select(static value => value!.Value)
            .OrderByDescending(static value => value)
            .ToList();

        return candidates.Count == 0 ? null : candidates[0];
    }

    private static DataTable BuildCellTable(ProductionContext ctx, IProductionTimeProvider productionTime)
    {
        var table = new DataTable();
        if (ctx.CurrentCells.Count == 0)
        {
            return table;
        }

        var firstCell = ctx.CurrentCells.Values.First();
        var properties = firstCell.GetType()
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.Name != nameof(CellDataBase.ProcessType)
                && p.Name != nameof(CellDataBase.DisplayLabel))
            .ToList();

        foreach (var prop in properties)
        {
            table.Columns.Add(prop.Name, typeof(string));
        }

        foreach (var cell in ctx.CurrentCells.Values)
        {
            var row = table.NewRow();
            foreach (var prop in properties)
            {
                row[prop.Name] = FormatValue(prop.GetValue(cell), productionTime);
            }

            table.Rows.Add(row);
        }

        return table;
    }

    private static IReadOnlyList<MonitorSnapshotRow> BuildContextProjectionRows(
        ProductionContext context,
        IProductionTimeProvider productionTime,
        string snapshotPropertyName,
        params string[] contextPropertyNames)
    {
        var rows = new List<MonitorSnapshotRow>();

        foreach (var propertyName in contextPropertyNames)
        {
            var value = TryReadProperty(context, propertyName);
            if (value is not null)
            {
                rows.Add(new MonitorSnapshotRow(
                    context.DeviceName,
                    propertyName,
                    FormatValue(value, productionTime)));
            }
        }

        var snapshot = TryReadProperty(context, snapshotPropertyName);
        if (snapshot is null)
        {
            return rows;
        }

        var properties = snapshot.GetType()
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(static property => property.GetIndexParameters().Length == 0)
            .OrderBy(static property => property.Name);

        foreach (var property in properties)
        {
            rows.Add(new MonitorSnapshotRow(
                context.DeviceName,
                property.Name,
                FormatValue(property.GetValue(snapshot), productionTime)));
        }

        return rows;
    }

    private static string FormatValue(object? value, IProductionTimeProvider productionTime) => value switch
    {
        null => "--",
        JsonElement element => FormatJsonElement(element, productionTime),
        DateTime dt => productionTime.ToBusinessTime(dt).ToString("HH:mm:ss.fff"),
        DateTimeOffset dto => productionTime.ToBusinessTime(dto.UtcDateTime).ToString("HH:mm:ss.fff"),
        bool b => b ? "OK" : "NG",
        double d => d.ToString("F3"),
        float f => f.ToString("F3"),
        decimal m => m.ToString("F3"),
        string text => text,
        IEnumerable enumerable => FormatEnumerable(enumerable, productionTime),
        _ => value?.ToString() ?? "--"
    };

    private static string FormatEnumerable(IEnumerable values, IProductionTimeProvider productionTime)
    {
        var formattedValues = values
            .Cast<object?>()
            .Select(value => FormatValue(value, productionTime))
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .ToList();

        return formattedValues.Count == 0
            ? "--"
            : string.Join("；", formattedValues);
    }

    private static string FormatJsonElement(JsonElement element, IProductionTimeProvider productionTime)
    {
        return element.ValueKind switch
        {
            JsonValueKind.String when element.TryGetDateTime(out var dateTime)
                => productionTime.ToBusinessTime(dateTime).ToString("HH:mm:ss.fff"),
            JsonValueKind.String => element.GetString() ?? "--",
            JsonValueKind.True => "OK",
            JsonValueKind.False => "NG",
            JsonValueKind.Number when element.TryGetDouble(out var number) => number.ToString("F3"),
            JsonValueKind.Null => "--",
            JsonValueKind.Undefined => "--",
            _ => element.ToString()
        };
    }

    private static DateTime? FindLastHeartbeat(ProductionContext context)
        => FindLatestTimestamp(
            context,
            static key => key.Contains("Heartbeat", StringComparison.OrdinalIgnoreCase),
            "LastHeartbeatAt");

    private static DateTime? FindLastUpdated(ProductionContext context)
        => FindLatestTimestamp(
            context,
            static _ => true,
            "LastRealtimeAt",
            "LastEquipmentStatusAt",
            "LastOutboundAt",
            "LastInboundAt",
            "LastHeartbeatAt");

    private static DateTime? FindLatestTimestamp(
        ProductionContext context,
        Func<string, bool> keyFilter,
        params string[] propertyNames)
    {
        var candidates = context.DeviceBag
            .Where(kv => keyFilter(kv.Key))
            .Select(kv => TryConvertDateTime(kv.Value));

        foreach (var propertyName in propertyNames)
        {
            candidates = candidates.Append(TryReadDateTimeProperty(context, propertyName));
        }

        return candidates
            .Where(static value => value.HasValue && value.Value.Year > 1900)
            .Select(static value => value!.Value)
            .OrderByDescending(static value => value)
            .FirstOrDefault();
    }

    private static DateTime? TryReadDateTimeProperty(ProductionContext context, string propertyName)
        => TryConvertDateTime(TryReadProperty(context, propertyName));

    private static object? TryReadProperty(object source, string propertyName)
        => source.GetType()
            .GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance)
            ?.GetValue(source);

    private static DateTime? TryConvertDateTime(object? value)
    {
        return value switch
        {
            DateTime dateTime => dateTime,
            DateTimeOffset dateTimeOffset => dateTimeOffset.UtcDateTime,
            JsonElement { ValueKind: JsonValueKind.String } element when element.TryGetDateTime(out var dateTime)
                => dateTime,
            string text when DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var parsed)
                => parsed,
            _ => null
        };
    }

    private static string FormatTimestamp(DateTime? timestamp, IProductionTimeProvider productionTime)
        => timestamp.HasValue && timestamp.Value.Year > 1900
            ? productionTime.ToBusinessTime(timestamp.Value).ToString("HH:mm:ss.fff")
            : "--";

    private static string FormatTimestamp(DateTimeOffset? timestamp, IProductionTimeProvider productionTime)
        => timestamp.HasValue && timestamp.Value.Year > 1900
            ? productionTime.ToBusinessTime(timestamp.Value.UtcDateTime).ToString("HH:mm:ss.fff")
            : "--";

    private static IReadOnlyDictionary<string, int> EmptyStepStates { get; } =
        new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

    private static string FormatStateMachineStepText(int? stepValue)
        => stepValue switch
        {
            null => "--",
            0 => "等待触发",
            10 => "处理中",
            30 => "等待 PLC 复位",
            _ => $"步骤 {stepValue.Value.ToString(CultureInfo.InvariantCulture)}"
        };

    private static string FormatRequiredSignalSummary(IReadOnlyCollection<TaskRequiredSignal> signals)
        => signals.Count == 0
            ? "--"
            : string.Join("；", signals.Select(static signal => $"{signal.SignalKey}/{signal.Direction}"));
}
