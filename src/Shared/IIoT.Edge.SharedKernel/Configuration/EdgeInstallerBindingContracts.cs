using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using IIoT.Edge.SharedKernel.Security;

namespace IIoT.Edge.SharedKernel.Configuration;

public sealed record EdgeInstallerBindingEnvelope(
    int SchemaVersion,
    string GenerationId,
    DateTimeOffset GeneratedAtUtc,
    DateTimeOffset ExpiresAtUtc,
    string BaseUrl,
    EdgeInstallerBindingPaths Paths,
    IReadOnlyList<EdgeInstallerDeviceBinding> Bindings);

public sealed record EdgeInstallerBindingPaths(
    string DeviceInstance,
    string BootstrapRefresh,
    string ActivateDevice,
    string ActivateDeviceConfirm,
    string IdentityDeviceLogin,
    string HumanIdentityRefresh,
    string HumanSessionValidation,
    string DeviceLog,
    string PassStationBatchTemplate,
    string CapacityHourly,
    string CapacitySummary,
    string CapacitySummaryRange,
    string RecipeByDeviceTemplate,
    string ClientReleaseCatalogTemplate,
    string ClientVersionReport,
    string RuntimeHeartbeat,
    string EdgeHostPlcRuntimeStates);

public sealed record EdgeInstallerDeviceBinding(
    string ClientCode,
    string DeviceName,
    Guid ProcessId,
    string ProcessType,
    string ModuleId,
    string PluginVersion,
    string PackageSha256,
    string PluginDirectory,
    string ConfigDirectory,
    string DbDirectory,
    string DataDirectory,
    string LogsDirectory,
    string CacheDirectory,
    string ContextDirectory,
    string BuffersDirectory,
    string PendingCredentialReference,
    string PendingCredentialSecret);

public sealed record EdgeRuntimeBindingEnvelope(
    int SchemaVersion,
    string GenerationId,
    DateTimeOffset GeneratedAtUtc,
    DateTimeOffset ExpiresAtUtc,
    string BaseUrl,
    EdgeInstallerBindingPaths Paths,
    IReadOnlyList<EdgeRuntimeDeviceBinding> Bindings);

public sealed record EdgeRuntimeDeviceBinding(
    string ClientCode,
    string DeviceName,
    Guid ProcessId,
    string ProcessType,
    string ModuleId,
    string PluginVersion,
    string PackageSha256,
    string PluginDirectory,
    string ConfigDirectory,
    string DbDirectory,
    string DataDirectory,
    string LogsDirectory,
    string CacheDirectory,
    string ContextDirectory,
    string BuffersDirectory,
    string PendingCredentialReference,
    string ActivationStatus,
    string CredentialOwnerSid = "legacy-v2");

public static class EdgeInstallerBindingCodec
{
    public const int LegacySchemaVersion = 2;
    public const int CurrentSchemaVersion = 3;
    public const string RuntimePendingStatus = "Pending";
    private const string DeviceIdTemplate = "{deviceId}";

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public static EdgeInstallerBindingEnvelope ParsePayload(string json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        if (!TryReadInt32(root, "schemaVersion", out var schemaVersion))
        {
            throw new InvalidDataException("Binding schemaVersion is missing.");
        }

        return schemaVersion switch
        {
            CurrentSchemaVersion => ParseV3(root),
            LegacySchemaVersion => ParseV2(root),
            _ => throw new InvalidDataException($"Binding schemaVersion {schemaVersion} is not supported.")
        };
    }

    public static EdgeRuntimeBindingEnvelope ToRuntime(
        EdgeInstallerBindingEnvelope payload,
        string credentialOwnerSid = "legacy-v2")
    {
        ArgumentNullException.ThrowIfNull(payload);
        return new EdgeRuntimeBindingEnvelope(
            payload.SchemaVersion,
            payload.GenerationId,
            payload.GeneratedAtUtc,
            payload.ExpiresAtUtc,
            payload.BaseUrl,
            payload.Paths,
            payload.Bindings.Select(binding => new EdgeRuntimeDeviceBinding(
                binding.ClientCode,
                binding.DeviceName,
                binding.ProcessId,
                binding.ProcessType,
                binding.ModuleId,
                binding.PluginVersion,
                binding.PackageSha256,
                binding.PluginDirectory,
                binding.ConfigDirectory,
                binding.DbDirectory,
                binding.DataDirectory,
                binding.LogsDirectory,
                binding.CacheDirectory,
                binding.ContextDirectory,
                binding.BuffersDirectory,
                binding.PendingCredentialReference,
                RuntimePendingStatus,
                credentialOwnerSid)).ToArray());
    }

    public static EdgeRuntimeBindingEnvelope ParseRuntime(string json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        if (!TryReadInt32(root, "schemaVersion", out var schemaVersion)
            || schemaVersion is not (LegacySchemaVersion or CurrentSchemaVersion))
        {
            throw new InvalidDataException("Runtime binding schemaVersion is not supported.");
        }

        if (schemaVersion == CurrentSchemaVersion)
        {
            _ = ParseV3Paths(root);
        }

        var envelope = JsonSerializer.Deserialize<EdgeRuntimeBindingEnvelope>(json, SerializerOptions)
            ?? throw new InvalidDataException("Runtime binding is empty.");

        ValidateEnvelope(
            envelope.GenerationId,
            envelope.GeneratedAtUtc,
            envelope.ExpiresAtUtc,
            envelope.BaseUrl,
            envelope.Paths,
            envelope.SchemaVersion == CurrentSchemaVersion,
            envelope.Bindings.Select(static binding => (
                binding.ClientCode,
                binding.ModuleId,
                binding.DeviceName,
                binding.ProcessId,
                binding.ProcessType,
                binding.PluginVersion,
                binding.PackageSha256,
                binding.PluginDirectory,
                binding.ConfigDirectory,
                binding.DbDirectory,
                binding.DataDirectory,
                binding.LogsDirectory,
                binding.CacheDirectory,
                binding.ContextDirectory,
                binding.BuffersDirectory,
                binding.PendingCredentialReference)).ToArray());
        if (envelope.Bindings.Any(static binding =>
                !string.Equals(binding.ActivationStatus, RuntimePendingStatus, StringComparison.Ordinal)
                && !string.Equals(binding.ActivationStatus, "Activating", StringComparison.Ordinal)
                && !string.Equals(binding.ActivationStatus, "Activated", StringComparison.Ordinal)
                && !string.Equals(binding.ActivationStatus, "Expired", StringComparison.Ordinal)
                && !string.Equals(binding.ActivationStatus, "Failed", StringComparison.Ordinal)))
        {
            throw new InvalidDataException("Runtime binding activation status is invalid.");
        }
        if (envelope.SchemaVersion == CurrentSchemaVersion
            && envelope.Bindings.Any(static binding =>
                string.IsNullOrWhiteSpace(binding.CredentialOwnerSid)
                || string.Equals(binding.CredentialOwnerSid, "legacy-v2", StringComparison.Ordinal)))
        {
            throw new InvalidDataException("Runtime Binding v3 credential owner SID is missing.");
        }

        return envelope with
        {
            Bindings = envelope.Bindings.Select(NormalizeRuntimeBinding).ToArray()
        };
    }

    public static string SerializeRuntime(EdgeRuntimeBindingEnvelope envelope)
        => JsonSerializer.Serialize(envelope, SerializerOptions);

    private static EdgeInstallerBindingEnvelope ParseV3(JsonElement root)
    {
        var generationId = ReadRequiredString(root, "generationId");
        var generatedAtUtc = ReadRequiredTimestamp(root, "generatedAtUtc");
        var expiresAtUtc = ReadRequiredTimestamp(root, "expiresAtUtc");
        var baseUrl = ReadRequiredString(root, "baseUrl");
        var paths = ParseV3Paths(root);
        if (!root.TryGetProperty("bindings", out var bindingsElement)
            || bindingsElement.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidDataException("Binding entries are missing.");
        }

        var bindings = bindingsElement.EnumerateArray().Select(item =>
        {
            var clientCode = EdgeClientIdentity.NormalizeClientCode(ReadRequiredString(item, "clientCode"));
            if (!item.TryGetProperty("pendingCredential", out var credential)
                || credential.ValueKind != JsonValueKind.Object)
            {
                throw new InvalidDataException($"Pending credential is missing for {clientCode}.");
            }

            var reference = ReadRequiredString(credential, "name");
            var expectedReference = WindowsCredentialManagerStore.CreatePendingReference(generationId, clientCode);
            if (!string.Equals(reference, expectedReference, StringComparison.Ordinal))
            {
                throw new InvalidDataException($"Pending credential reference is invalid for {clientCode}.");
            }

            return new EdgeInstallerDeviceBinding(
                clientCode,
                ReadRequiredString(item, "deviceName"),
                ReadRequiredGuid(item, "processId"),
                ReadRequiredString(item, "processType"),
                ReadRequiredString(item, "moduleId"),
                ReadRequiredString(item, "pluginVersion"),
                NormalizeSha256(ReadRequiredString(item, "packageSha256")),
                ReadRequiredString(item, "pluginDirectory"),
                ReadRequiredString(item, "configDirectory"),
                ReadRequiredString(item, "dbDirectory"),
                ReadRequiredString(item, "dataDirectory"),
                ReadRequiredString(item, "logsDirectory"),
                ReadRequiredString(item, "cacheDirectory"),
                ReadRequiredString(item, "contextDirectory"),
                ReadRequiredString(item, "buffersDirectory"),
                reference,
                ReadRequiredString(credential, "secret"));
        }).ToArray();

        ValidateEnvelope(
            generationId,
            generatedAtUtc,
            expiresAtUtc,
            baseUrl,
            paths,
            true,
            bindings.Select(static binding => (
                binding.ClientCode,
                binding.ModuleId,
                binding.DeviceName,
                binding.ProcessId,
                binding.ProcessType,
                binding.PluginVersion,
                binding.PackageSha256,
                binding.PluginDirectory,
                binding.ConfigDirectory,
                binding.DbDirectory,
                binding.DataDirectory,
                binding.LogsDirectory,
                binding.CacheDirectory,
                binding.ContextDirectory,
                binding.BuffersDirectory,
                binding.PendingCredentialReference)).ToArray());
        return new EdgeInstallerBindingEnvelope(
            CurrentSchemaVersion,
            generationId.Trim(),
            generatedAtUtc,
            expiresAtUtc,
            NormalizeBaseUrl(baseUrl),
            paths,
            bindings.Select(NormalizePayloadBinding).ToArray());
    }

    private static EdgeInstallerBindingEnvelope ParseV2(JsonElement root)
    {
        var generatedAtUtc = ReadRequiredTimestamp(root, "generatedAtUtc");
        var generationId = $"legacy-v2-{generatedAtUtc.UtcTicks.ToString(CultureInfo.InvariantCulture)}";
        var baseUrl = ReadRequiredString(root, "baseUrl");
        var paths = ParseLegacyV2Paths(root);
        if (!root.TryGetProperty("bindings", out var bindingsElement)
            || bindingsElement.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidDataException("Binding entries are missing.");
        }

        var bindings = bindingsElement.EnumerateArray().Select(item =>
        {
            var clientCode = EdgeClientIdentity.NormalizeClientCode(ReadRequiredString(item, "clientCode"));
            var pluginRoot = $"plugins/{clientCode}";
            return new EdgeInstallerDeviceBinding(
                clientCode,
                ReadRequiredString(item, "deviceName"),
                ReadRequiredGuid(item, "processId"),
                "legacy-v2",
                ReadRequiredString(item, "moduleId"),
                "legacy-v2",
                new string('0', 64),
                $"{pluginRoot}/app",
                $"{pluginRoot}/config",
                $"{pluginRoot}/db",
                $"{pluginRoot}/data",
                $"{pluginRoot}/logs",
                $"{pluginRoot}/cache",
                $"{pluginRoot}/context",
                $"{pluginRoot}/buffers",
                WindowsCredentialManagerStore.CreatePendingReference(generationId, clientCode),
                ReadRequiredString(item, "bootstrapSecret"));
        }).ToArray();
        var expiresAtUtc = generatedAtUtc.AddDays(7);
        ValidateEnvelope(
            generationId,
            generatedAtUtc,
            expiresAtUtc,
            baseUrl,
            paths,
            false,
            bindings.Select(static binding => (
                binding.ClientCode,
                binding.ModuleId,
                binding.DeviceName,
                binding.ProcessId,
                binding.ProcessType,
                binding.PluginVersion,
                binding.PackageSha256,
                binding.PluginDirectory,
                binding.ConfigDirectory,
                binding.DbDirectory,
                binding.DataDirectory,
                binding.LogsDirectory,
                binding.CacheDirectory,
                binding.ContextDirectory,
                binding.BuffersDirectory,
                binding.PendingCredentialReference)).ToArray());
        return new EdgeInstallerBindingEnvelope(
            LegacySchemaVersion,
            generationId,
            generatedAtUtc,
            expiresAtUtc,
            NormalizeBaseUrl(baseUrl),
            paths,
            bindings.Select(NormalizePayloadBinding).ToArray());
    }

    private static EdgeInstallerBindingPaths ParseV3Paths(JsonElement root)
    {
        if (!root.TryGetProperty("paths", out var paths) || paths.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidDataException("Binding paths are missing.");
        }

        return EdgeBindingRouteCatalog.ParseStrictV3(paths);
    }

    private static EdgeInstallerBindingPaths ParseLegacyV2Paths(JsonElement root)
    {
        if (!root.TryGetProperty("paths", out var paths) || paths.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidDataException("Binding paths are missing.");
        }

        return new EdgeInstallerBindingPaths(
            NormalizeApiPath(ReadRequiredString(paths, "deviceInstance"), false),
            string.Empty,
            ReadOptionalApiPath(paths, "activateDevice") ?? string.Empty,
            ReadOptionalApiPath(paths, "activateDeviceConfirm") ?? string.Empty,
            string.Empty,
            string.Empty,
            string.Empty,
            string.Empty,
            ReadOptionalApiPath(paths, "passStationBatch") ?? string.Empty,
            string.Empty,
            string.Empty,
            string.Empty,
            string.Empty,
            NormalizeApiPath(ReadRequiredString(paths, "clientReleaseCatalogTemplate"), true),
            NormalizeApiPath(ReadRequiredString(paths, "clientVersionReport"), false),
            NormalizeApiPath(ReadRequiredString(paths, "runtimeHeartbeat"), false),
            ReadOptionalApiPath(paths, "plcSnapshot") ?? string.Empty);
    }

    private static void ValidateEnvelope(
        string generationId,
        DateTimeOffset generatedAtUtc,
        DateTimeOffset expiresAtUtc,
        string baseUrl,
        EdgeInstallerBindingPaths paths,
        bool requireActivationPaths,
        IReadOnlyList<(string ClientCode, string ModuleId, string DeviceName, Guid ProcessId,
            string ProcessType, string PluginVersion, string PackageSha256, string PluginDirectory,
            string ConfigDirectory, string DbDirectory, string DataDirectory, string LogsDirectory, string CacheDirectory,
            string ContextDirectory, string BuffersDirectory, string PendingCredentialReference)> bindings)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(generationId);
        _ = NormalizeBaseUrl(baseUrl);
        ArgumentNullException.ThrowIfNull(paths);
        if (requireActivationPaths)
        {
            EdgeBindingRouteCatalog.ValidateV3(paths);
        }
        else
        {
            _ = NormalizeApiPath(paths.DeviceInstance, false);
            _ = NormalizeApiPath(paths.ClientReleaseCatalogTemplate, true);
            _ = NormalizeApiPath(paths.ClientVersionReport, false);
            _ = NormalizeApiPath(paths.RuntimeHeartbeat, false);
        }
        if (generatedAtUtc == default || expiresAtUtc <= generatedAtUtc)
        {
            throw new InvalidDataException("Binding validity window is invalid.");
        }

        if (bindings.Count == 0)
        {
            throw new InvalidDataException("Binding entries are empty.");
        }

        var clientCodes = new HashSet<string>(StringComparer.Ordinal);
        var moduleIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var binding in bindings)
        {
            var clientCode = EdgeClientIdentity.NormalizeClientCode(binding.ClientCode);
            if (!clientCodes.Add(clientCode))
            {
                throw new InvalidDataException($"ClientCode {clientCode} is duplicated.");
            }

            if (string.IsNullOrWhiteSpace(binding.ModuleId) || !moduleIds.Add(binding.ModuleId.Trim()))
            {
                throw new InvalidDataException($"ModuleId is missing or duplicated for {clientCode}.");
            }

            if (string.IsNullOrWhiteSpace(binding.DeviceName)
                || binding.ProcessId == Guid.Empty
                || string.IsNullOrWhiteSpace(binding.ProcessType)
                || string.IsNullOrWhiteSpace(binding.PluginVersion)
                || binding.PackageSha256.Length != 64
                || string.IsNullOrWhiteSpace(binding.PendingCredentialReference))
            {
                throw new InvalidDataException($"Binding facts are incomplete for {clientCode}.");
            }

            ValidateCanonicalDevicePaths(clientCode, binding);
        }
    }

    private static void ValidateCanonicalDevicePaths(
        string clientCode,
        (string ClientCode, string ModuleId, string DeviceName, Guid ProcessId,
            string ProcessType, string PluginVersion, string PackageSha256, string PluginDirectory,
            string ConfigDirectory, string DbDirectory, string DataDirectory, string LogsDirectory, string CacheDirectory,
            string ContextDirectory, string BuffersDirectory, string PendingCredentialReference) binding)
    {
        var expectedRoot = $"plugins/{clientCode}";
        var expected = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [nameof(binding.PluginDirectory)] = $"{expectedRoot}/app",
            [nameof(binding.ConfigDirectory)] = $"{expectedRoot}/config",
            [nameof(binding.DbDirectory)] = $"{expectedRoot}/db",
            [nameof(binding.DataDirectory)] = $"{expectedRoot}/data",
            [nameof(binding.LogsDirectory)] = $"{expectedRoot}/logs",
            [nameof(binding.CacheDirectory)] = $"{expectedRoot}/cache",
            [nameof(binding.ContextDirectory)] = $"{expectedRoot}/context",
            [nameof(binding.BuffersDirectory)] = $"{expectedRoot}/buffers"
        };
        var actual = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [nameof(binding.PluginDirectory)] = NormalizeRelativePath(binding.PluginDirectory),
            [nameof(binding.ConfigDirectory)] = NormalizeRelativePath(binding.ConfigDirectory),
            [nameof(binding.DbDirectory)] = NormalizeRelativePath(binding.DbDirectory),
            [nameof(binding.DataDirectory)] = NormalizeRelativePath(binding.DataDirectory),
            [nameof(binding.LogsDirectory)] = NormalizeRelativePath(binding.LogsDirectory),
            [nameof(binding.CacheDirectory)] = NormalizeRelativePath(binding.CacheDirectory),
            [nameof(binding.ContextDirectory)] = NormalizeRelativePath(binding.ContextDirectory),
            [nameof(binding.BuffersDirectory)] = NormalizeRelativePath(binding.BuffersDirectory)
        };
        foreach (var pair in expected)
        {
            if (!string.Equals(actual[pair.Key], pair.Value, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException($"{pair.Key} is not canonical for {clientCode}.");
            }
        }
    }

    private static EdgeInstallerDeviceBinding NormalizePayloadBinding(EdgeInstallerDeviceBinding binding)
        => binding with
        {
            ClientCode = EdgeClientIdentity.NormalizeClientCode(binding.ClientCode),
            ModuleId = binding.ModuleId.Trim(),
            DeviceName = binding.DeviceName.Trim(),
            ProcessType = binding.ProcessType.Trim(),
            PluginVersion = binding.PluginVersion.Trim(),
            PackageSha256 = binding.PackageSha256.ToUpperInvariant(),
            PluginDirectory = NormalizeRelativePath(binding.PluginDirectory),
            ConfigDirectory = NormalizeRelativePath(binding.ConfigDirectory),
            DbDirectory = NormalizeRelativePath(binding.DbDirectory),
            DataDirectory = NormalizeRelativePath(binding.DataDirectory),
            LogsDirectory = NormalizeRelativePath(binding.LogsDirectory),
            CacheDirectory = NormalizeRelativePath(binding.CacheDirectory),
            ContextDirectory = NormalizeRelativePath(binding.ContextDirectory),
            BuffersDirectory = NormalizeRelativePath(binding.BuffersDirectory),
            PendingCredentialReference = binding.PendingCredentialReference.Trim()
        };

    private static EdgeRuntimeDeviceBinding NormalizeRuntimeBinding(EdgeRuntimeDeviceBinding binding)
        => binding with
        {
            ClientCode = EdgeClientIdentity.NormalizeClientCode(binding.ClientCode),
            ModuleId = binding.ModuleId.Trim(),
            DeviceName = binding.DeviceName.Trim(),
            ProcessType = binding.ProcessType.Trim(),
            PluginVersion = binding.PluginVersion.Trim(),
            PackageSha256 = binding.PackageSha256.ToUpperInvariant(),
            PluginDirectory = NormalizeRelativePath(binding.PluginDirectory),
            ConfigDirectory = NormalizeRelativePath(binding.ConfigDirectory),
            DbDirectory = NormalizeRelativePath(binding.DbDirectory),
            DataDirectory = NormalizeRelativePath(binding.DataDirectory),
            LogsDirectory = NormalizeRelativePath(binding.LogsDirectory),
            CacheDirectory = NormalizeRelativePath(binding.CacheDirectory),
            ContextDirectory = NormalizeRelativePath(binding.ContextDirectory),
            BuffersDirectory = NormalizeRelativePath(binding.BuffersDirectory),
            PendingCredentialReference = binding.PendingCredentialReference.Trim(),
            ActivationStatus = binding.ActivationStatus.Trim(),
            CredentialOwnerSid = binding.CredentialOwnerSid?.Trim()
                ?? throw new InvalidDataException("Runtime Binding credential owner SID is missing.")
        };

    private static string NormalizeBaseUrl(string value)
    {
        if (!Uri.TryCreate(value.Trim(), UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
            || string.IsNullOrWhiteSpace(uri.Host)
            || !string.IsNullOrEmpty(uri.UserInfo)
            || (!string.IsNullOrEmpty(uri.AbsolutePath) && uri.AbsolutePath != "/")
            || !string.IsNullOrEmpty(uri.Query)
            || !string.IsNullOrEmpty(uri.Fragment))
        {
            throw new InvalidDataException("Binding baseUrl is invalid.");
        }

        return uri.GetLeftPart(UriPartial.Authority);
    }

    private static string NormalizeApiPath(string value, bool requiresDeviceId)
    {
        var normalized = value.Trim();
        if (!normalized.StartsWith("/", StringComparison.Ordinal)
            || normalized.StartsWith("//", StringComparison.Ordinal)
            || normalized.Contains('\\')
            || normalized.Contains('?')
            || normalized.Contains('#')
            || normalized.Any(char.IsControl)
            || normalized.Split('/').Any(static segment => segment is "." or ".."))
        {
            throw new InvalidDataException("Binding API path is invalid.");
        }

        var tokenCount = normalized.Split(DeviceIdTemplate).Length - 1;
        if ((requiresDeviceId && tokenCount != 1) || (!requiresDeviceId && tokenCount != 0))
        {
            throw new InvalidDataException("Binding API path template is invalid.");
        }

        return normalized;
    }

    private static string? ReadOptionalApiPath(JsonElement paths, string propertyName)
    {
        if (!paths.TryGetProperty(propertyName, out var element)
            || element.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        return element.ValueKind == JsonValueKind.String
            ? NormalizeApiPath(element.GetString() ?? string.Empty, false)
            : throw new InvalidDataException($"Binding path {propertyName} is invalid.");
    }

    private static string NormalizeRelativePath(string value)
    {
        var normalized = value.Trim().Replace('\\', '/').Trim('/');
        if (normalized.Length == 0
            || Path.IsPathRooted(value)
            || normalized.Split('/').Any(static segment => segment.Length == 0 || segment is "." or "..")
            || normalized.Any(char.IsControl))
        {
            throw new InvalidDataException("Binding relative path is invalid.");
        }

        return normalized;
    }

    private static string NormalizeSha256(string value)
    {
        var normalized = value.Trim();
        if (normalized.Length != 64 || normalized.Any(static character => !Uri.IsHexDigit(character)))
        {
            throw new InvalidDataException("Package SHA-256 is invalid.");
        }

        return normalized.ToUpperInvariant();
    }

    private static string ReadRequiredString(JsonElement element, string propertyName)
        => element.TryGetProperty(propertyName, out var value)
           && value.ValueKind == JsonValueKind.String
           && !string.IsNullOrWhiteSpace(value.GetString())
            ? value.GetString()!.Trim()
            : throw new InvalidDataException($"Binding field {propertyName} is required.");

    private static Guid ReadRequiredGuid(JsonElement element, string propertyName)
        => Guid.TryParse(ReadRequiredString(element, propertyName), out var value) && value != Guid.Empty
            ? value
            : throw new InvalidDataException($"Binding field {propertyName} is invalid.");

    private static DateTimeOffset ReadRequiredTimestamp(JsonElement element, string propertyName)
        => DateTimeOffset.TryParse(
               ReadRequiredString(element, propertyName),
               CultureInfo.InvariantCulture,
               DateTimeStyles.RoundtripKind,
               out var value)
            ? value.ToUniversalTime()
            : throw new InvalidDataException($"Binding field {propertyName} is invalid.");

    private static bool TryReadInt32(JsonElement element, string propertyName, out int value)
    {
        value = default;
        return element.TryGetProperty(propertyName, out var property)
               && property.ValueKind == JsonValueKind.Number
               && property.TryGetInt32(out value);
    }
}
