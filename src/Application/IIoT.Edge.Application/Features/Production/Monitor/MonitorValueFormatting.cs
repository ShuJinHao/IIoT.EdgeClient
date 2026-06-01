using System.Collections;
using System.Data;
using System.Globalization;
using System.Reflection;
using System.Text.Json;
using IIoT.Edge.Application.Abstractions.Time;
using IIoT.Edge.SharedKernel.DataPipeline.CellData;
using IIoT.Edge.SharedKernel.Context;

namespace IIoT.Edge.Application.Features.Production.Monitor;

internal static class MonitorValueFormatting
{
    public static DataTable BuildCellTable(ProductionContext ctx, IProductionTimeProvider productionTime)
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

    public static IReadOnlyList<MonitorSnapshotRow> BuildContextProjectionRows(
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

    public static string FormatTimestamp(DateTime? timestamp, IProductionTimeProvider productionTime)
        => timestamp.HasValue && timestamp.Value.Year > 1900
            ? productionTime.ToBusinessTime(timestamp.Value).ToString("HH:mm:ss.fff")
            : "--";

    public static string FormatTimestamp(DateTimeOffset? timestamp, IProductionTimeProvider productionTime)
        => timestamp.HasValue && timestamp.Value.Year > 1900
            ? productionTime.ToBusinessTime(timestamp.Value.UtcDateTime).ToString("HH:mm:ss.fff")
            : "--";

    public static DateTime? FindLastHeartbeat(ProductionContext context)
        => FindLatestTimestamp(
            context,
            static key => key.Contains("Heartbeat", StringComparison.OrdinalIgnoreCase),
            "LastHeartbeatAt");

    public static DateTime? FindLastUpdated(ProductionContext context)
        => FindLatestTimestamp(
            context,
            static _ => true,
            "LastRealtimeAt",
            "LastEquipmentStatusAt",
            "LastOutboundAt",
            "LastInboundAt",
            "LastHeartbeatAt");

    public static string FormatValue(object? value, IProductionTimeProvider productionTime) => value switch
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
}
