using System.Text.Json.Nodes;

namespace IIoT.Edge.SharedKernel.Configuration;

/// <summary>
/// Binding v3 到无秘密运行配置的唯一物化器。Installer 是正式 v3 唯一调用方；
/// Launcher 的 v2 迁移不得用本类制造或补齐 v3。
/// </summary>
public static class EdgeBindingMaterializer
{
    public static void MaterializeV3(
        JsonObject root,
        EdgeInstallerBindingEnvelope payload,
        EdgeInstallerDeviceBinding binding,
        string runtimeDataRoot,
        string pluginAppRoot)
    {
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(payload);
        ArgumentNullException.ThrowIfNull(binding);
        ArgumentException.ThrowIfNullOrWhiteSpace(runtimeDataRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(pluginAppRoot);
        if (payload.SchemaVersion != EdgeInstallerBindingCodec.CurrentSchemaVersion)
        {
            throw new InvalidDataException("EdgeBindingMaterializer only accepts Binding v3.");
        }

        EdgeBindingRouteCatalog.ValidateV3(payload.Paths);
        var clientCode = EdgeClientIdentity.NormalizeClientCode(binding.ClientCode);
        root["InstanceId"] = clientCode;

        var shell = GetOrCreate(root, "Shell");
        shell["MachineProfile"] = clientCode;
        shell["ClientCode"] = clientCode;
        shell["RuntimeDataRoot"] = runtimeDataRoot;

        var modules = GetOrCreate(root, "Modules");
        modules["Enabled"] = new JsonArray(binding.ModuleId);
        modules["PluginRoots"] = new JsonArray(pluginAppRoot);

        var cloud = GetOrCreate(root, "CloudApi");
        cloud.Remove("BootstrapSecret");
        cloud["Enabled"] = true;
        cloud["ClientCode"] = clientCode;
        cloud["BootstrapCredentialReference"] = binding.PendingCredentialReference;
        cloud["BaseUrl"] = payload.BaseUrl;
        var paths = GetOrCreate(cloud, "Paths");
        EdgeBindingRouteCatalog.WriteMachineConfiguration(paths, payload.Paths);

        var facts = GetOrCreate(root, "DevicePluginBinding");
        facts["SchemaVersion"] = EdgeInstallerBindingCodec.CurrentSchemaVersion;
        facts["GenerationId"] = payload.GenerationId;
        facts["ClientCode"] = clientCode;
        facts["DeviceName"] = binding.DeviceName;
        facts["ProcessId"] = binding.ProcessId.ToString("D");
        facts["ProcessType"] = binding.ProcessType;
        facts["ModuleId"] = binding.ModuleId;
        facts["PluginVersion"] = binding.PluginVersion;
        facts["PackageSha256"] = binding.PackageSha256;

        ValidateV3(root, payload, binding, runtimeDataRoot, pluginAppRoot);
    }

    public static void ValidateV3(
        JsonObject root,
        EdgeInstallerBindingEnvelope payload,
        EdgeInstallerDeviceBinding binding,
        string runtimeDataRoot,
        string pluginAppRoot)
    {
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(payload);
        ArgumentNullException.ThrowIfNull(binding);
        var clientCode = EdgeClientIdentity.NormalizeClientCode(binding.ClientCode);
        var cloud = RequireObject(root, "CloudApi");
        var paths = RequireObject(cloud, "Paths");
        EdgeBindingRouteCatalog.ValidateMaterializedMachineConfiguration(paths, payload.Paths);
        if (cloud["BootstrapSecret"] is not null
            || !ReadBoolean(cloud, "Enabled")
            || !EqualsString(cloud, "ClientCode", clientCode)
            || !EqualsString(cloud, "BootstrapCredentialReference", binding.PendingCredentialReference)
            || !EqualsString(cloud, "BaseUrl", payload.BaseUrl))
        {
            throw new InvalidDataException("Materialized CloudApi identity does not match Binding v3.");
        }

        var shell = RequireObject(root, "Shell");
        if (!EqualsString(root, "InstanceId", clientCode)
            || !EqualsString(shell, "MachineProfile", clientCode)
            || !EqualsString(shell, "ClientCode", clientCode)
            || !EqualsString(shell, "RuntimeDataRoot", runtimeDataRoot))
        {
            throw new InvalidDataException("Materialized Shell identity does not match Binding v3.");
        }

        var modules = RequireObject(root, "Modules");
        if (!IsSingleString(modules["Enabled"], binding.ModuleId)
            || !IsSingleString(modules["PluginRoots"], pluginAppRoot))
        {
            throw new InvalidDataException("Materialized plugin selection does not match Binding v3.");
        }

        var facts = RequireObject(root, "DevicePluginBinding");
        if (!EqualsInt32(facts, "SchemaVersion", EdgeInstallerBindingCodec.CurrentSchemaVersion)
            || !EqualsString(facts, "GenerationId", payload.GenerationId)
            || !EqualsString(facts, "ClientCode", clientCode)
            || !EqualsString(facts, "DeviceName", binding.DeviceName)
            || !EqualsString(facts, "ProcessId", binding.ProcessId.ToString("D"))
            || !EqualsString(facts, "ProcessType", binding.ProcessType)
            || !EqualsString(facts, "ModuleId", binding.ModuleId)
            || !EqualsString(facts, "PluginVersion", binding.PluginVersion)
            || !EqualsString(facts, "PackageSha256", binding.PackageSha256))
        {
            throw new InvalidDataException("Materialized DevicePluginBinding facts do not match Binding v3.");
        }
    }

    private static JsonObject GetOrCreate(JsonObject parent, string propertyName)
    {
        if (parent[propertyName] is JsonObject value)
        {
            return value;
        }

        value = new JsonObject();
        parent[propertyName] = value;
        return value;
    }

    private static JsonObject RequireObject(JsonObject parent, string propertyName)
        => parent[propertyName] as JsonObject
           ?? throw new InvalidDataException($"Materialized configuration section {propertyName} is missing.");

    private static bool EqualsString(JsonObject parent, string propertyName, string expected)
        => parent[propertyName] is JsonValue value
           && value.TryGetValue<string>(out var actual)
           && string.Equals(actual, expected, StringComparison.Ordinal);

    private static bool EqualsInt32(JsonObject parent, string propertyName, int expected)
        => parent[propertyName] is JsonValue value
           && value.TryGetValue<int>(out var actual)
           && actual == expected;

    private static bool ReadBoolean(JsonObject parent, string propertyName)
        => parent[propertyName] is JsonValue value
           && value.TryGetValue<bool>(out var actual)
           && actual;

    private static bool IsSingleString(JsonNode? node, string expected)
        => node is JsonArray array
           && array.Count == 1
           && array[0] is JsonValue value
           && value.TryGetValue<string>(out var actual)
           && string.Equals(actual, expected, StringComparison.Ordinal);
}
