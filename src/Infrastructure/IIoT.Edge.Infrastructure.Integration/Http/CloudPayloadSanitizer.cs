using System.Text.Json;
using System.Text.Json.Nodes;
using IIoT.Edge.Module.Contracts.Cloud;

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
        if (payload is EdgeHostPlcRuntimeStateReport)
        {
            return payload;
        }

        var node = JsonSerializer.SerializeToNode(payload);
        RemoveIdentityKeys(node);

        return node ?? payload;
    }

    private static void RemoveIdentityKeys(JsonNode? node)
    {
        switch (node)
        {
            case JsonObject obj:
                foreach (var key in BlockedIdentityKeys.ToList())
                {
                    obj.Remove(key);
                }

                foreach (var child in obj.ToList())
                {
                    RemoveIdentityKeys(child.Value);
                }

                break;
            case JsonArray array:
                foreach (var child in array)
                {
                    RemoveIdentityKeys(child);
                }

                break;
        }
    }
}
