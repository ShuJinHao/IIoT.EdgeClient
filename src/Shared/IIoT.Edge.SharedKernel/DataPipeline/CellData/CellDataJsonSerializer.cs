using System.Text.Json;

namespace IIoT.Edge.SharedKernel.DataPipeline.CellData;

public interface ICellDataJsonSerializer
{
    string Serialize(CellDataBase cellData);

    string SerializeMany(IEnumerable<CellDataBase> cellData);

    CellDataBase? Deserialize(string processType, string json);
}

public sealed class CellDataJsonSerializer : ICellDataJsonSerializer
{
    private readonly ICellDataTypeRegistry _typeRegistry;
    private readonly JsonSerializerOptions _options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public CellDataJsonSerializer(ICellDataTypeRegistry typeRegistry)
    {
        _typeRegistry = typeRegistry;
    }

    public string Serialize(CellDataBase cellData)
        => JsonSerializer.Serialize(cellData, cellData.GetType(), _options);

    public string SerializeMany(IEnumerable<CellDataBase> cellData)
        => JsonSerializer.Serialize(cellData, _options);

    public CellDataBase? Deserialize(string processType, string json)
        => _typeRegistry.Deserialize(processType, json, _options);
}
