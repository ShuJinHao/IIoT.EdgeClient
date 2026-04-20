using IIoT.Edge.Application.Abstractions.Context;
using IIoT.Edge.Application.Abstractions.Modules;
using IIoT.Edge.SharedKernel.Context;
using IIoT.Edge.SharedKernel.DataPipeline.CellData;
using MediatR;
using System.Data;
using System.Reflection;

namespace IIoT.Edge.Application.Features.Production.Monitor;

public record DeviceMonitorSnapshot(
    string DeviceName,
    int DayShiftOk,
    int DayShiftNg,
    int DayShiftTotal,
    string DayShiftYield,
    int NightShiftOk,
    int NightShiftNg,
    int NightShiftTotal,
    string NightShiftYield,
    int TotalAll,
    int OkAll,
    int NgAll,
    string YieldAll,
    string DeviceDataSummary,
    string StepSummary,
    int CellCount,
    DataTable CellTable,
    CloudSyncDiagnosticsSnapshot CloudSync,
    MesSyncDiagnosticsSnapshot MesSync,
    ProductionContextPersistenceDiagnostics ContextPersistence);

public record GetMonitorSnapshotQuery : IRequest<List<DeviceMonitorSnapshot>>;

public class GetMonitorSnapshotHandler(
    IProductionContextStore contextStore,
    IEdgeSyncDiagnosticsQuery diagnosticsQuery)
    : IRequestHandler<GetMonitorSnapshotQuery, List<DeviceMonitorSnapshot>>
{
    public async Task<List<DeviceMonitorSnapshot>> Handle(GetMonitorSnapshotQuery request, CancellationToken ct)
    {
        var diagnostics = await diagnosticsQuery.GetCurrentAsync(ct).ConfigureAwait(false);
        var result = new List<DeviceMonitorSnapshot>();
        var contexts = contextStore.GetAll().ToList();

        if (contexts.Count == 0)
        {
            result.Add(new DeviceMonitorSnapshot(
                DeviceName: "System",
                DayShiftOk: 0,
                DayShiftNg: 0,
                DayShiftTotal: 0,
                DayShiftYield: "0%",
                NightShiftOk: 0,
                NightShiftNg: 0,
                NightShiftTotal: 0,
                NightShiftYield: "0%",
                TotalAll: 0,
                OkAll: 0,
                NgAll: 0,
                YieldAll: "0%",
                DeviceDataSummary: "No data",
                StepSummary: "No steps",
                CellCount: 0,
                CellTable: new DataTable(),
                CloudSync: diagnostics.Cloud,
                MesSync: diagnostics.Mes,
                ContextPersistence: diagnostics.ContextPersistence));

            return result;
        }

        foreach (var ctx in contexts)
        {
            var cap = ctx.TodayCapacity;
            var deviceInfo = string.Join("  ",
                ctx.DeviceBag.OrderBy(kv => kv.Key)
                    .Select(kv => $"{kv.Key}={FormatValue(kv.Value)}"));
            var stepInfo = string.Join("  ",
                ctx.StepStates.Select(kv => $"{kv.Key}={kv.Value}"));

            result.Add(new DeviceMonitorSnapshot(
                DeviceName: ctx.DeviceName,
                DayShiftOk: cap.DayShift.OkCount,
                DayShiftNg: cap.DayShift.NgCount,
                DayShiftTotal: cap.DayShift.Total,
                DayShiftYield: cap.DayShift.Yield,
                NightShiftOk: cap.NightShift.OkCount,
                NightShiftNg: cap.NightShift.NgCount,
                NightShiftTotal: cap.NightShift.Total,
                NightShiftYield: cap.NightShift.Yield,
                TotalAll: cap.TotalAll,
                OkAll: cap.OkAll,
                NgAll: cap.NgAll,
                YieldAll: cap.YieldAll,
                DeviceDataSummary: string.IsNullOrEmpty(deviceInfo) ? "No data" : deviceInfo,
                StepSummary: string.IsNullOrEmpty(stepInfo) ? "No steps" : stepInfo,
                CellCount: ctx.CurrentCells.Count,
                CellTable: BuildCellTable(ctx),
                CloudSync: diagnostics.Cloud,
                MesSync: diagnostics.Mes,
                ContextPersistence: diagnostics.ContextPersistence));
        }

        return result;
    }

    private static DataTable BuildCellTable(ProductionContext ctx)
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
                row[prop.Name] = FormatValue(prop.GetValue(cell));
            }

            table.Rows.Add(row);
        }

        return table;
    }

    private static string FormatValue(object? value) => value switch
    {
        null => "--",
        DateTime dt => ToLocalTime(dt).ToString("HH:mm:ss.fff"),
        bool b => b ? "OK" : "NG",
        double d => d.ToString("F3"),
        _ => value?.ToString() ?? "--"
    };

    private static DateTime ToLocalTime(DateTime value)
    {
        return value.Kind switch
        {
            DateTimeKind.Utc => value.ToLocalTime(),
            DateTimeKind.Unspecified => DateTime.SpecifyKind(value, DateTimeKind.Utc).ToLocalTime(),
            _ => value
        };
    }
}
