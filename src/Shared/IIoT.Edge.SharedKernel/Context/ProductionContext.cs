using IIoT.Edge.SharedKernel.DataPipeline.Capacity;
using IIoT.Edge.SharedKernel.DataPipeline.CellData;
using System.Collections.Concurrent;
using System.Text.Json.Serialization;

namespace IIoT.Edge.SharedKernel.Context;

public class ProductionContext
{
    public string DeviceName { get; set; } = string.Empty;

    public int DeviceId { get; set; }

    [JsonInclude]
    [JsonPropertyName("stepStates")]
    public ConcurrentDictionary<string, int> StepStateEntries { get; private set; } = new();

    [JsonInclude]
    [JsonPropertyName("deviceBag")]
    public ConcurrentDictionary<string, object> DeviceBagEntries { get; private set; } = new();

    [JsonInclude]
    [JsonPropertyName("currentCells")]
    public ConcurrentDictionary<string, CellDataBase> CurrentCellEntries { get; private set; } = new();

    [JsonIgnore]
    public IReadOnlyDictionary<string, int> StepStates => StepStateEntries;

    [JsonIgnore]
    public IReadOnlyDictionary<string, object> DeviceBag => DeviceBagEntries;

    [JsonIgnore]
    public IReadOnlyDictionary<string, CellDataBase> CurrentCells => CurrentCellEntries;

    public TodayCapacity TodayCapacity { get; set; } = new();

    public event Action<string, string, object?>? DataChanged;

    public int GetStep(string taskName)
        => StepStateEntries.TryGetValue(taskName, out var step) ? step : 0;

    public void SetStep(string taskName, int step)
    {
        StepStateEntries[taskName] = step;
        DataChanged?.Invoke("Step", taskName, step);
    }

    public void Set<T>(string key, T value)
    {
        DeviceBagEntries[key] = value!;
        DataChanged?.Invoke("Device", key, value);
    }

    public T? Get<T>(string key)
        => DeviceBagEntries.TryGetValue(key, out var val) && val is T typed ? typed : default;

    public bool Has(string key)
        => DeviceBagEntries.ContainsKey(key);

    public bool RemoveDeviceData(string key)
    {
        var removed = DeviceBagEntries.TryRemove(key, out _);
        if (removed)
        {
            DataChanged?.Invoke("DeviceRemoved", key, null);
        }

        return removed;
    }

    public void AddCell(string barcode, CellDataBase cellData)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(barcode);
        ArgumentNullException.ThrowIfNull(cellData);

        var key = barcode.Trim();
        CurrentCellEntries[key] = cellData;
        DataChanged?.Invoke("CellAdded", key, cellData);
    }

    public CellDataBase? GetCell(string barcode)
        => CurrentCellEntries.TryGetValue(barcode, out var cell) ? cell : null;

    public T? GetCell<T>(string barcode) where T : CellDataBase
        => CurrentCellEntries.TryGetValue(barcode, out var cell) && cell is T typed ? typed : null;

    public bool HasCell(string barcode)
        => CurrentCellEntries.ContainsKey(barcode);

    public IReadOnlyList<string> GetAllBarcodes()
        => CurrentCellEntries.Keys.ToList().AsReadOnly();

    public bool RemoveCell(string barcode)
    {
        var removed = CurrentCellEntries.TryRemove(barcode, out _);
        if (removed)
        {
            DataChanged?.Invoke("CellRemoved", barcode, null);
        }

        return removed;
    }

    [JsonIgnore]
    public Dictionary<string, string> DebugDeviceView
        => DeviceBagEntries.OrderBy(kv => kv.Key)
            .ToDictionary(
                kv => kv.Key,
                kv => $"{kv.Value} ({kv.Value?.GetType().Name})");

    [JsonIgnore]
    public IReadOnlyDictionary<string, CellDataBase> DebugCellView
        => CurrentCellEntries;

    public void ClearAllCells()
    {
        CurrentCellEntries.Clear();
        DataChanged?.Invoke("AllCellsCleared", string.Empty, null);
    }

    public void Reset()
    {
        DeviceBagEntries.Clear();
        CurrentCellEntries.Clear();
        StepStateEntries.Clear();
        TodayCapacity.Reset();
    }
}
