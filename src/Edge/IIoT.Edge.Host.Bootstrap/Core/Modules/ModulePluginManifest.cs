using System.Text.Json.Serialization;

namespace IIoT.Edge.Host.Bootstrap.Modules;

public sealed class ModulePluginManifest
{
    [JsonPropertyName("moduleId")]
    public string ModuleId { get; set; } = string.Empty;

    [JsonPropertyName("displayName")]
    public string DisplayName { get; set; } = string.Empty;

    [JsonPropertyName("version")]
    public string Version { get; set; } = string.Empty;

    [JsonPropertyName("hostApiVersion")]
    public string HostApiVersion { get; set; } = string.Empty;

    [JsonPropertyName("minHostVersion")]
    public string MinHostVersion { get; set; } = string.Empty;

    [JsonPropertyName("maxHostVersion")]
    public string MaxHostVersion { get; set; } = string.Empty;

    [JsonPropertyName("entryAssembly")]
    public string EntryAssembly { get; set; } = string.Empty;

    [JsonPropertyName("entryType")]
    public string EntryType { get; set; } = string.Empty;

    [JsonPropertyName("supportedProcessType")]
    public string SupportedProcessType { get; set; } = string.Empty;

    [JsonPropertyName("configurationSchema")]
    public string ConfigurationSchema { get; set; } = string.Empty;

    [JsonPropertyName("moduleSeed")]
    public ModulePluginSeedManifest? ModuleSeed { get; set; }

    [JsonPropertyName("capabilities")]
    public ModulePluginCapabilitiesManifest? Capabilities { get; set; }

    [JsonPropertyName("dependencies")]
    public List<string> Dependencies { get; set; } = [];
}

public sealed class ModulePluginSeedManifest
{
    [JsonPropertyName("schemaVersion")]
    public int SchemaVersion { get; set; }

    [JsonPropertyName("currentVersion")]
    public int CurrentVersion { get; set; }

    [JsonPropertyName("supportedEnvironments")]
    public List<string> SupportedEnvironments { get; set; } = [];

    [JsonPropertyName("newDevicesEnabled")]
    public bool? NewDevicesEnabled { get; set; }

    [JsonPropertyName("missingTaskBindingsEnabled")]
    public bool? MissingTaskBindingsEnabled { get; set; }

    [JsonPropertyName("resetBeforeImport")]
    public bool? ResetBeforeImport { get; set; }
}

public sealed class ModulePluginCapabilitiesManifest
{
    [JsonPropertyName("requiresProductionPlan")]
    public bool? RequiresProductionPlan { get; set; }
}
