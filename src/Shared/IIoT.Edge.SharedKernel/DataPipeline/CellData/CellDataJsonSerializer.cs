using System.Text.Json;

namespace IIoT.Edge.SharedKernel.DataPipeline.CellData;

public static class CellDataJsonSerializer
{
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public static string Serialize(CellDataBase cellData)
        => JsonSerializer.Serialize(cellData, cellData.GetType(), Options);

    public static string SerializeMany(IEnumerable<CellDataBase> cellData)
        => JsonSerializer.Serialize(cellData, Options);

    public static CellDataBase? Deserialize(string processType, string json)
        => CellDataTypeRegistry.Deserialize(processType, json, Options);
}
