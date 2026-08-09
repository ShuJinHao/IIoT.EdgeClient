using IIoT.Edge.Application.Common.Identity;
using IIoT.Edge.SharedKernel.Configuration;
using Microsoft.Extensions.Configuration;

namespace IIoT.Edge.Host.Bootstrap;

internal sealed class ConfigurationDevicePluginRuntimeContext : IDevicePluginRuntimeContext
{
    public ConfigurationDevicePluginRuntimeContext(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        var section = configuration.GetSection("DevicePluginBinding");
        var schemaVersion = section.GetValue<int?>("SchemaVersion") ?? 2;
        if (schemaVersion < 3)
        {
            Current = DevicePluginRuntimeIdentity.Legacy;
            return;
        }

        var generationId = Require(section, "GenerationId");
        var clientCode = EdgeClientIdentity.NormalizeClientCode(Require(section, "ClientCode"));
        var processType = NormalizeToken(Require(section, "ProcessType"), "ProcessType");
        var moduleId = NormalizeToken(Require(section, "ModuleId"), "ModuleId");
        var pluginVersion = NormalizeToken(Require(section, "PluginVersion"), "PluginVersion");
        var packageSha256 = RequireSha256(section, "PackageSha256");
        Current = new DevicePluginRuntimeIdentity(
            schemaVersion,
            generationId,
            clientCode,
            processType,
            moduleId,
            pluginVersion,
            packageSha256);
    }

    public DevicePluginRuntimeIdentity Current { get; }

    private static string Require(IConfiguration section, string key)
        => string.IsNullOrWhiteSpace(section[key])
            ? throw new InvalidDataException($"DevicePluginBinding:{key} is required for schema v3.")
            : section[key]!.Trim();

    private static string NormalizeToken(string value, string key)
        => value.Length > 128 || value.Any(char.IsControl)
            ? throw new InvalidDataException($"DevicePluginBinding:{key} is invalid.")
            : value;

    private static string RequireSha256(IConfiguration section, string key)
    {
        var value = Require(section, key).ToLowerInvariant();
        return value.Length == 64 && value.All(Uri.IsHexDigit)
            ? value
            : throw new InvalidDataException($"DevicePluginBinding:{key} must be SHA-256.");
    }
}

internal sealed class RuntimeBoundEdgeProcessModule(
    IIoT.Edge.Module.Contracts.Modules.IEdgeProcessModule inner,
    string processType) : IIoT.Edge.Module.Contracts.Modules.IEdgeProcessModule
{
    internal System.Reflection.Assembly ImplementationAssembly => inner.GetType().Assembly;

    public string ModuleId => inner.ModuleId;
    public string ProcessType { get; } = processType;
    public string DisplayName => inner.DisplayName;
    public bool RequiresCloudUploader => inner.RequiresCloudUploader;
    public bool RequiresMesUploader => inner.RequiresMesUploader;

    public void Configure(IIoT.Edge.Module.Contracts.Modules.IEdgeProcessModuleBuilder builder)
        => inner.Configure(builder);
}
