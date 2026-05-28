using IIoT.Edge.Application.Abstractions.Time;
using IIoT.Edge.SharedKernel.Context;
using IIoT.Edge.SharedKernel.DataPipeline.CellData;
using System.Collections;
using System.Globalization;
using System.Reflection;
using System.Text.Json;

namespace IIoT.Edge.Application.Features.Production.Monitor;

public sealed record MonitorCellDebugSnapshot(
    string DeviceName,
    string InternalKey,
    string DisplayLabel,
    string ProcessType,
    string RuntimeStatusText,
    string CompletedTimeText,
    IReadOnlyList<MonitorSnapshotRow> FieldRows);

internal static class MonitorCellDebugProjection
{
    private const int MaxFlattenDepth = 8;

    private static readonly HashSet<string> SyntheticFieldNames = new(StringComparer.Ordinal)
    {
        nameof(CellDataBase.ProcessType),
        nameof(CellDataBase.DisplayLabel)
    };

    public static IReadOnlyList<MonitorCellDebugSnapshot> Build(
        ProductionContext context,
        IProductionTimeProvider productionTime)
        => context.CurrentCells
            .OrderBy(static kv => kv.Key, StringComparer.OrdinalIgnoreCase)
            .Select(kv => BuildCellSnapshot(context.DeviceName, kv.Key, kv.Value, productionTime))
            .ToList();

    private static MonitorCellDebugSnapshot BuildCellSnapshot(
        string deviceName,
        string internalKey,
        CellDataBase cellData,
        IProductionTimeProvider productionTime)
    {
        var displayLabel = string.IsNullOrWhiteSpace(cellData.DisplayLabel)
            ? internalKey
            : cellData.DisplayLabel;
        var runtimeStatus = FormatValue(TryReadProperty(cellData, "RuntimeStatus"), productionTime);
        var completedTime = FormatValue(cellData.CompletedTime, productionTime);

        var rows = new List<MonitorSnapshotRow>
        {
            new(deviceName, "InternalKey", internalKey),
            new(deviceName, nameof(CellDataBase.DisplayLabel), displayLabel),
            new(deviceName, nameof(CellDataBase.ProcessType), cellData.ProcessType)
        };

        foreach (var property in cellData.GetType()
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(static property => property.GetIndexParameters().Length == 0
                && !SyntheticFieldNames.Contains(property.Name))
            .OrderBy(static property => property.Name, StringComparer.Ordinal))
        {
            AddFieldRows(deviceName, property.Name, property.GetValue(cellData), rows, productionTime, 0);
        }

        return new MonitorCellDebugSnapshot(
            deviceName,
            internalKey,
            displayLabel,
            cellData.ProcessType,
            runtimeStatus,
            completedTime,
            rows);
    }

    private static void AddFieldRows(
        string deviceName,
        string path,
        object? value,
        List<MonitorSnapshotRow> rows,
        IProductionTimeProvider productionTime,
        int depth)
    {
        if (value is null || IsSimpleValue(value) || depth >= MaxFlattenDepth)
        {
            rows.Add(new MonitorSnapshotRow(deviceName, path, FormatValue(value, productionTime)));
            return;
        }

        if (value is IEnumerable enumerable)
        {
            AddEnumerableRows(deviceName, path, enumerable, rows, productionTime, depth);
            return;
        }

        var nestedProperties = value.GetType()
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(static property => property.GetIndexParameters().Length == 0)
            .OrderBy(static property => property.Name, StringComparer.Ordinal)
            .ToList();

        if (nestedProperties.Count == 0)
        {
            rows.Add(new MonitorSnapshotRow(deviceName, path, FormatValue(value, productionTime)));
            return;
        }

        foreach (var property in nestedProperties)
        {
            AddFieldRows(
                deviceName,
                $"{path}.{property.Name}",
                property.GetValue(value),
                rows,
                productionTime,
                depth + 1);
        }
    }

    private static void AddEnumerableRows(
        string deviceName,
        string path,
        IEnumerable values,
        List<MonitorSnapshotRow> rows,
        IProductionTimeProvider productionTime,
        int depth)
    {
        var index = 0;
        foreach (var item in values)
        {
            if (TryReadKeyValue(item, out var key, out var entryValue))
            {
                AddFieldRows(
                    deviceName,
                    $"{path}.{FormatPathSegment(key, index)}",
                    entryValue,
                    rows,
                    productionTime,
                    depth + 1);
            }
            else
            {
                AddFieldRows(
                    deviceName,
                    $"{path}[{index.ToString(CultureInfo.InvariantCulture)}]",
                    item,
                    rows,
                    productionTime,
                    depth + 1);
            }

            index++;
        }

        if (index == 0)
        {
            rows.Add(new MonitorSnapshotRow(deviceName, path, "--"));
        }
    }

    private static bool TryReadKeyValue(object? item, out object? key, out object? value)
    {
        if (item is DictionaryEntry entry)
        {
            key = entry.Key;
            value = entry.Value;
            return true;
        }

        var itemType = item?.GetType();
        var keyProperty = itemType?.GetProperty("Key", BindingFlags.Public | BindingFlags.Instance);
        var valueProperty = itemType?.GetProperty("Value", BindingFlags.Public | BindingFlags.Instance);
        if (keyProperty is null || valueProperty is null)
        {
            key = null;
            value = null;
            return false;
        }

        key = keyProperty.GetValue(item);
        value = valueProperty.GetValue(item);
        return true;
    }

    private static string FormatPathSegment(object? key, int fallbackIndex)
    {
        var text = Convert.ToString(key, CultureInfo.InvariantCulture);
        return string.IsNullOrWhiteSpace(text)
            ? $"Item{fallbackIndex.ToString(CultureInfo.InvariantCulture)}"
            : text;
    }

    private static bool IsSimpleValue(object value)
    {
        if (value is string or JsonElement or DateTime or DateTimeOffset or TimeSpan or Guid)
        {
            return true;
        }

        var type = Nullable.GetUnderlyingType(value.GetType()) ?? value.GetType();
        return type.IsPrimitive || type.IsEnum || type == typeof(decimal);
    }

    private static string FormatValue(object? value, IProductionTimeProvider productionTime) => value switch
    {
        null => "--",
        JsonElement element => FormatJsonElement(element, productionTime),
        DateTime dt => productionTime.ToBusinessTime(dt).ToString("HH:mm:ss.fff"),
        DateTimeOffset dto => productionTime.ToBusinessTime(dto.UtcDateTime).ToString("HH:mm:ss.fff"),
        bool b => b ? "OK" : "NG",
        double d => d.ToString("F3", CultureInfo.InvariantCulture),
        float f => f.ToString("F3", CultureInfo.InvariantCulture),
        decimal m => m.ToString("F3", CultureInfo.InvariantCulture),
        string text => string.IsNullOrWhiteSpace(text) ? "--" : text,
        IEnumerable enumerable => FormatEnumerable(enumerable, productionTime),
        IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture) ?? "--",
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
            JsonValueKind.Number when element.TryGetDouble(out var number)
                => number.ToString("F3", CultureInfo.InvariantCulture),
            JsonValueKind.Null => "--",
            JsonValueKind.Undefined => "--",
            _ => element.ToString()
        };
    }

    private static object? TryReadProperty(object source, string propertyName)
        => source.GetType()
            .GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance)
            ?.GetValue(source);
}
