using System.Text.Json.Serialization;

namespace IIoT.Edge.Module.Abstractions;

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

    [JsonPropertyName("dependencies")]
    public List<string> Dependencies { get; set; } = [];
}
