using System.Text.Json;
using System.Text.Json.Serialization;
using IIoT.Edge.Module.Contracts.DataPipeline.CellData;

namespace IIoT.Edge.Host.DataPipeline.Context;

internal class CellDataBaseConverter(ICellDataTypeRegistry cellDataTypeRegistry) : JsonConverter<CellDataBase>
{
    public override CellDataBase? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using var doc = JsonDocument.ParseValue(ref reader);
        var root = doc.RootElement;

        var processType = root.TryGetProperty("processType", out var prop)
            ? prop.GetString()
            : null;

        if (processType is null)
        {
            throw new JsonException("CellData 缺少 processType 属性。");
        }

        var json = root.GetRawText();
        var result = cellDataTypeRegistry.Deserialize(processType, json, options);

        return result ?? throw new JsonException($"Unknown process type: {processType}");
    }

    public override void Write(Utf8JsonWriter writer, CellDataBase value, JsonSerializerOptions options)
    {
        JsonSerializer.Serialize(writer, value, value.GetType(), options);
    }
}

public class ObjectToInferredTypesConverter : JsonConverter<object>
{
    public override object? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        return reader.TokenType switch
        {
            JsonTokenType.True => true,
            JsonTokenType.False => false,
            JsonTokenType.Number when reader.TryGetInt32(out var i) => i,
            JsonTokenType.Number when reader.TryGetInt64(out var l) => l,
            JsonTokenType.Number => reader.GetDouble(),
            JsonTokenType.String when reader.TryGetDateTime(out var dt) => dt,
            JsonTokenType.String => reader.GetString()!,
            _ => JsonDocument.ParseValue(ref reader).RootElement.Clone()
        };
    }

    public override void Write(Utf8JsonWriter writer, object value, JsonSerializerOptions options)
    {
        JsonSerializer.Serialize(writer, value, value.GetType(), options);
    }
}
