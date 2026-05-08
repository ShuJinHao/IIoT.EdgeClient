using System.Collections.Concurrent;
using System.Text.Json;

namespace IIoT.Edge.SharedKernel.DataPipeline.CellData;

public interface ICellDataTypeRegistry
{
    void Register<T>(string processType) where T : CellDataBase;

    void Register(string processType, Type cellDataType);

    Type? Resolve(string processType);

    CellDataBase? Deserialize(string processType, string json, JsonSerializerOptions? options = null);
}

public sealed class CellDataTypeRegistry : ICellDataTypeRegistry
{
    private readonly ConcurrentDictionary<string, Type> _map = new();

    public void Register<T>(string processType) where T : CellDataBase
        => _map[processType] = typeof(T);

    public void Register(string processType, Type cellDataType)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(processType);
        ArgumentNullException.ThrowIfNull(cellDataType);

        if (!typeof(CellDataBase).IsAssignableFrom(cellDataType))
        {
            throw new InvalidOperationException(
                $"CellData type '{cellDataType.FullName}' must inherit from {nameof(CellDataBase)}.");
        }

        _map[processType] = cellDataType;
    }

    public Type? Resolve(string processType)
        => _map.GetValueOrDefault(processType);

    public CellDataBase? Deserialize(string processType, string json, JsonSerializerOptions? options = null)
    {
        var type = Resolve(processType);
        if (type is null)
        {
            return null;
        }

        return (CellDataBase?)JsonSerializer.Deserialize(json, type, options);
    }
}
