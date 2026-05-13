using System.Text.Json;
using System.Text.Json.Nodes;

namespace IIoT.Edge.Infrastructure.Integration.Http;

internal interface ICloudPayloadSanitizer
{
    object Sanitize(object payload);
}

internal sealed class CloudPayloadSanitizer : ICloudPayloadSanitizer
{
    private static readonly HashSet<string> BlockedIdentityKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "macAddress",
        "mac_address",
        "clientCode",
        "client_code"
    };

    public object Sanitize(object payload)
    {
        var node = JsonSerializer.SerializeToNode(payload);
        if (node is JsonObject obj)
        {
            RemoveIdentityKeys(obj);
            return obj;
        }

        return payload;
    }

    private static void RemoveIdentityKeys(JsonObject obj)
    {
        foreach (var key in BlockedIdentityKeys.ToList())
        {
            obj.Remove(key);
        }
    }
}
