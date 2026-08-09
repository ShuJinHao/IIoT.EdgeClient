using System.Text.Json;
using System.Text.Json.Nodes;

namespace IIoT.Edge.SharedKernel.Configuration;

public enum EdgeBindingRouteKey
{
    DeviceInstance,
    BootstrapRefresh,
    ActivateDevice,
    ActivateDeviceConfirm,
    IdentityDeviceLogin,
    HumanIdentityRefresh,
    HumanSessionValidation,
    DeviceLog,
    PassStationBatchTemplate,
    CapacityHourly,
    CapacitySummary,
    CapacitySummaryRange,
    RecipeByDeviceTemplate,
    ClientReleaseCatalogTemplate,
    ClientVersionReport,
    RuntimeHeartbeat,
    EdgeHostPlcRuntimeStates
}

public sealed record EdgeBindingRouteDescriptor(
    EdgeBindingRouteKey Key,
    string WireName,
    string MachineConfigKey,
    string? RequiredPlaceholder,
    string RuntimeConsumer,
    IReadOnlyList<string>? FixedSegments = null);

/// <summary>
/// Binding v3 在 Edge 侧的唯一 17 路由描述表。解析、机器配置物化、占位符校验和
/// 运行消费者映射都必须从这里取得，不允许再维护平行的路由子集。
/// </summary>
public static class EdgeBindingRouteCatalog
{
    public const int ExpectedRouteCount = 17;

    public static IReadOnlyList<EdgeBindingRouteDescriptor> All { get; } =
    [
        new(EdgeBindingRouteKey.DeviceInstance, "deviceInstance", "DeviceInstance", null, "DeviceBootstrap"),
        new(EdgeBindingRouteKey.BootstrapRefresh, "bootstrapRefresh", "BootstrapRefresh", null, "DeviceBootstrap"),
        new(EdgeBindingRouteKey.ActivateDevice, "activateDevice", "ActivateDevice", null, "DeviceActivation"),
        new(EdgeBindingRouteKey.ActivateDeviceConfirm, "activateDeviceConfirm", "ActivateDeviceConfirm", null, "DeviceActivation"),
        new(EdgeBindingRouteKey.IdentityDeviceLogin, "identityDeviceLogin", "IdentityDeviceLogin", null, "HumanIdentity"),
        new(EdgeBindingRouteKey.HumanIdentityRefresh, "humanIdentityRefresh", "HumanIdentityRefresh", null, "HumanIdentity"),
        new(EdgeBindingRouteKey.HumanSessionValidation, "humanSessionValidation", "HumanSessionValidation", null, "HumanIdentity"),
        new(EdgeBindingRouteKey.DeviceLog, "deviceLog", "DeviceLog", null, "DeviceLog"),
        new(
            EdgeBindingRouteKey.PassStationBatchTemplate,
            "passStationBatchTemplate",
            "PassStationBatchTemplate",
            "{typeKey}",
            "PassStation",
            ["api", "v1", "edge", "pass-stations", "{typeKey}", "batch"]),
        new(EdgeBindingRouteKey.CapacityHourly, "capacityHourly", "CapacityHourly", null, "Capacity"),
        new(EdgeBindingRouteKey.CapacitySummary, "capacitySummary", "CapacitySummary", null, "Capacity"),
        new(EdgeBindingRouteKey.CapacitySummaryRange, "capacitySummaryRange", "CapacitySummaryRange", null, "Capacity"),
        new(EdgeBindingRouteKey.RecipeByDeviceTemplate, "recipeByDeviceTemplate", "RecipeByDeviceTemplate", "{deviceId}", "Recipe"),
        new(EdgeBindingRouteKey.ClientReleaseCatalogTemplate, "clientReleaseCatalogTemplate", "ClientReleaseCatalogTemplate", "{deviceId}", "ClientUpdate"),
        new(EdgeBindingRouteKey.ClientVersionReport, "clientVersionReport", "ClientVersionReport", null, "ClientUpdate"),
        new(EdgeBindingRouteKey.RuntimeHeartbeat, "runtimeHeartbeat", "RuntimeHeartbeat", null, "RuntimeHeartbeat"),
        new(
            EdgeBindingRouteKey.EdgeHostPlcRuntimeStates,
            "edgeHostPlcRuntimeStates",
            "EdgeHostPlcRuntimeStates",
            null,
            "PlcRuntimeState",
            ["api", "v1", "edge", "edge-hosts", "plc-runtime-states"])
    ];

    private static readonly IReadOnlyDictionary<string, EdgeBindingRouteDescriptor> ByWireName =
        All.ToDictionary(descriptor => descriptor.WireName, StringComparer.Ordinal);

    static EdgeBindingRouteCatalog()
    {
        if (All.Count != ExpectedRouteCount
            || All.Select(descriptor => descriptor.Key).Distinct().Count() != ExpectedRouteCount
            || All.Select(descriptor => descriptor.WireName).Distinct(StringComparer.Ordinal).Count() != ExpectedRouteCount
            || All.Select(descriptor => descriptor.MachineConfigKey).Distinct(StringComparer.Ordinal).Count() != ExpectedRouteCount)
        {
            throw new InvalidOperationException("Binding v3 route descriptor table must contain exactly 17 unique routes.");
        }
    }

    public static EdgeInstallerBindingPaths ParseStrictV3(JsonElement paths)
    {
        if (paths.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidDataException("Binding paths must be an object.");
        }

        var values = new Dictionary<EdgeBindingRouteKey, string>();
        var propertyNames = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in paths.EnumerateObject())
        {
            if (!propertyNames.Add(property.Name))
            {
                throw new InvalidDataException($"Binding route {property.Name} is duplicated.");
            }

            if (!ByWireName.TryGetValue(property.Name, out var descriptor))
            {
                throw new InvalidDataException($"Binding route {property.Name} is unknown for schema v3.");
            }

            if (property.Value.ValueKind != JsonValueKind.String
                || string.IsNullOrWhiteSpace(property.Value.GetString()))
            {
                throw new InvalidDataException($"Binding route {property.Name} is required.");
            }

            if (!values.TryAdd(
                    descriptor.Key,
                    NormalizeAndValidate(descriptor, property.Value.GetString()!)))
            {
                throw new InvalidDataException($"Binding route {property.Name} is duplicated.");
            }
        }

        var missing = All
            .Where(descriptor => !values.ContainsKey(descriptor.Key))
            .Select(descriptor => descriptor.WireName)
            .ToArray();
        if (missing.Length != 0 || values.Count != ExpectedRouteCount)
        {
            throw new InvalidDataException(
                $"Binding v3 requires 17/17 routes; missing: {string.Join(", ", missing)}.");
        }

        return Create(values);
    }

    public static void ValidateV3(EdgeInstallerBindingPaths paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        foreach (var descriptor in All)
        {
            _ = NormalizeAndValidate(descriptor, Get(paths, descriptor.Key));
        }
    }

    public static string ValidateAndNormalize(EdgeBindingRouteKey key, string value)
    {
        var descriptor = All.Single(candidate => candidate.Key == key);
        return NormalizeAndValidate(descriptor, value);
    }

    public static void WriteMachineConfiguration(JsonObject target, EdgeInstallerBindingPaths paths)
    {
        ArgumentNullException.ThrowIfNull(target);
        ValidateV3(paths);
        foreach (var descriptor in All)
        {
            target[descriptor.MachineConfigKey] = Get(paths, descriptor.Key);
        }

        target.Remove("PlcSnapshot");
        target.Remove("PassStationBatch");
    }

    public static void ValidateMaterializedMachineConfiguration(
        JsonObject target,
        EdgeInstallerBindingPaths expected)
    {
        ArgumentNullException.ThrowIfNull(target);
        ValidateV3(expected);
        foreach (var descriptor in All)
        {
            if (target[descriptor.MachineConfigKey] is not JsonValue value
                || !value.TryGetValue<string>(out var actual)
                || !string.Equals(actual, Get(expected, descriptor.Key), StringComparison.Ordinal))
            {
                throw new InvalidDataException(
                    $"Materialized CloudApi:Paths:{descriptor.MachineConfigKey} does not match Binding v3.");
            }
        }

        if (target["PlcSnapshot"] is not null || target["PassStationBatch"] is not null)
        {
            throw new InvalidDataException("Binding v3 machine configuration contains a legacy route alias.");
        }
    }

    public static string Get(EdgeInstallerBindingPaths paths, EdgeBindingRouteKey key)
        => key switch
        {
            EdgeBindingRouteKey.DeviceInstance => paths.DeviceInstance,
            EdgeBindingRouteKey.BootstrapRefresh => paths.BootstrapRefresh,
            EdgeBindingRouteKey.ActivateDevice => paths.ActivateDevice,
            EdgeBindingRouteKey.ActivateDeviceConfirm => paths.ActivateDeviceConfirm,
            EdgeBindingRouteKey.IdentityDeviceLogin => paths.IdentityDeviceLogin,
            EdgeBindingRouteKey.HumanIdentityRefresh => paths.HumanIdentityRefresh,
            EdgeBindingRouteKey.HumanSessionValidation => paths.HumanSessionValidation,
            EdgeBindingRouteKey.DeviceLog => paths.DeviceLog,
            EdgeBindingRouteKey.PassStationBatchTemplate => paths.PassStationBatchTemplate,
            EdgeBindingRouteKey.CapacityHourly => paths.CapacityHourly,
            EdgeBindingRouteKey.CapacitySummary => paths.CapacitySummary,
            EdgeBindingRouteKey.CapacitySummaryRange => paths.CapacitySummaryRange,
            EdgeBindingRouteKey.RecipeByDeviceTemplate => paths.RecipeByDeviceTemplate,
            EdgeBindingRouteKey.ClientReleaseCatalogTemplate => paths.ClientReleaseCatalogTemplate,
            EdgeBindingRouteKey.ClientVersionReport => paths.ClientVersionReport,
            EdgeBindingRouteKey.RuntimeHeartbeat => paths.RuntimeHeartbeat,
            EdgeBindingRouteKey.EdgeHostPlcRuntimeStates => paths.EdgeHostPlcRuntimeStates,
            _ => throw new ArgumentOutOfRangeException(nameof(key), key, null)
        };

    private static EdgeInstallerBindingPaths Create(
        IReadOnlyDictionary<EdgeBindingRouteKey, string> values)
        => new(
            values[EdgeBindingRouteKey.DeviceInstance],
            values[EdgeBindingRouteKey.BootstrapRefresh],
            values[EdgeBindingRouteKey.ActivateDevice],
            values[EdgeBindingRouteKey.ActivateDeviceConfirm],
            values[EdgeBindingRouteKey.IdentityDeviceLogin],
            values[EdgeBindingRouteKey.HumanIdentityRefresh],
            values[EdgeBindingRouteKey.HumanSessionValidation],
            values[EdgeBindingRouteKey.DeviceLog],
            values[EdgeBindingRouteKey.PassStationBatchTemplate],
            values[EdgeBindingRouteKey.CapacityHourly],
            values[EdgeBindingRouteKey.CapacitySummary],
            values[EdgeBindingRouteKey.CapacitySummaryRange],
            values[EdgeBindingRouteKey.RecipeByDeviceTemplate],
            values[EdgeBindingRouteKey.ClientReleaseCatalogTemplate],
            values[EdgeBindingRouteKey.ClientVersionReport],
            values[EdgeBindingRouteKey.RuntimeHeartbeat],
            values[EdgeBindingRouteKey.EdgeHostPlcRuntimeStates]);

    private static string NormalizeAndValidate(
        EdgeBindingRouteDescriptor descriptor,
        string value)
    {
        var normalized = NormalizeRelativeApiPath(value);
        if (descriptor.FixedSegments is { Count: > 0 } fixedSegments
            && !HasExactSegments(normalized, fixedSegments))
        {
            var expected = "/" + string.Join('/', fixedSegments);
            throw new InvalidDataException(
                $"Binding route {descriptor.WireName} must equal {expected}.");
        }

        if (descriptor.RequiredPlaceholder is null)
        {
            if (normalized.Contains('{') || normalized.Contains('}'))
            {
                throw new InvalidDataException(
                    $"Binding route {descriptor.WireName} must not contain a template placeholder.");
            }
            return normalized;
        }

        var occurrences = CountOccurrences(normalized, descriptor.RequiredPlaceholder);
        var remainder = normalized.Replace(descriptor.RequiredPlaceholder, string.Empty, StringComparison.Ordinal);
        if (occurrences != 1 || remainder.Contains('{') || remainder.Contains('}'))
        {
            throw new InvalidDataException(
                $"Binding route {descriptor.WireName} must contain exactly one {descriptor.RequiredPlaceholder} placeholder.");
        }

        return normalized;
    }

    private static string NormalizeRelativeApiPath(string value)
    {
        var normalized = value.Trim();
        if (!normalized.StartsWith("/", StringComparison.Ordinal)
            || normalized.StartsWith("//", StringComparison.Ordinal)
            || normalized.Contains('\\')
            || normalized.Contains('?')
            || normalized.Contains('#')
            || normalized.Any(char.IsControl))
        {
            throw new InvalidDataException("Binding API path is invalid.");
        }

        var segments = normalized.Split('/');
        for (var index = 1; index < segments.Length; index++)
        {
            var segment = segments[index];
            if (segment.Length == 0)
            {
                throw new InvalidDataException("Binding API path contains an empty segment.");
            }

            string decoded;
            try
            {
                decoded = Uri.UnescapeDataString(segment);
            }
            catch (UriFormatException exception)
            {
                throw new InvalidDataException("Binding API path contains invalid escaping.", exception);
            }

            if (decoded is "." or ".." || decoded.Contains('/') || decoded.Contains('\\'))
            {
                throw new InvalidDataException("Binding API path contains an unsafe segment.");
            }
        }

        return normalized;
    }

    private static bool HasExactSegments(
        string normalized,
        IReadOnlyList<string> expected)
    {
        var actual = normalized.Split('/');
        if (actual.Length != expected.Count + 1 || actual[0].Length != 0)
        {
            return false;
        }

        for (var index = 0; index < expected.Count; index++)
        {
            if (!string.Equals(actual[index + 1], expected[index], StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }

    private static int CountOccurrences(string value, string token)
    {
        var count = 0;
        var index = 0;
        while ((index = value.IndexOf(token, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += token.Length;
        }
        return count;
    }
}
