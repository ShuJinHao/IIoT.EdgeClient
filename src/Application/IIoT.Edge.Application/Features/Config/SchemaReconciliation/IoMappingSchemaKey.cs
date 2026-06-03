namespace IIoT.Edge.Application.Features.Config.SchemaReconciliation;

internal readonly record struct IoMappingSchemaKey(
    int NetworkDeviceId,
    string Direction,
    string SignalKey)
{
    public static string Create(int networkDeviceId, string direction, string signalKey)
        => $"{networkDeviceId}:{Normalize(direction)}:{Normalize(signalKey)}";

    public static bool TryParse(string key, out IoMappingSchemaKey value)
    {
        value = default;
        var parts = key.Split(':', 3, StringSplitOptions.TrimEntries);
        if (parts.Length != 3 || !int.TryParse(parts[0], out var networkDeviceId) || networkDeviceId <= 0)
        {
            return false;
        }

        value = new IoMappingSchemaKey(networkDeviceId, Normalize(parts[1]), Normalize(parts[2]));
        return !string.IsNullOrWhiteSpace(value.Direction)
               && !string.IsNullOrWhiteSpace(value.SignalKey);
    }

    public bool Matches(string direction, string signalKey)
        => string.Equals(Direction, Normalize(direction), StringComparison.Ordinal)
           && string.Equals(SignalKey, Normalize(signalKey), StringComparison.Ordinal);

    private static string Normalize(string value)
        => (value ?? string.Empty).Trim().ToUpperInvariant();
}
