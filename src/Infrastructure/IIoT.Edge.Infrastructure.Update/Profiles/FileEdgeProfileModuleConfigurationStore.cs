using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using IIoT.Edge.Module.Contracts.Updates;
using IIoT.Edge.SharedKernel.Configuration;

namespace IIoT.Edge.Infrastructure.Update.Profiles;

public sealed class FileEdgeProfileModuleConfigurationStore : IEdgeProfileModuleConfigurationStore
{
    public IReadOnlyList<string> ReadEnabledModules(EdgeUpdateTarget target)
    {
        ArgumentNullException.ThrowIfNull(target);

        var enabled = new List<string>();
        foreach (var path in ResolveEffectiveConfigPaths(target))
        {
            var fileEnabled = ReadEnabledModules(path);
            if (fileEnabled is not null)
            {
                enabled = fileEnabled;
            }
        }

        return enabled
            .Where(static moduleId => !string.IsNullOrWhiteSpace(moduleId))
            .Select(static moduleId => moduleId.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public void EnableModules(EdgeUpdateTarget target, IReadOnlyList<string> moduleIds)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(moduleIds);

        var targetPath = EnsureExternalMachineProfile(target);
        JsonObject root;
        if (File.Exists(targetPath))
        {
            try
            {
                root = JsonNode.Parse(File.ReadAllText(targetPath))?.AsObject()
                    ?? new JsonObject();
            }
            catch (JsonException ex)
            {
                throw new InvalidOperationException($"机器配置 JSON 无效，不能写入启用插件: {targetPath}", ex);
            }
        }
        else
        {
            root = new JsonObject();
        }

        var enabled = ReadEnabledModules(target)
            .Concat(moduleIds)
            .Where(static moduleId => !string.IsNullOrWhiteSpace(moduleId))
            .Select(static moduleId => moduleId.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(static moduleId => moduleId, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var modules = root["Modules"] as JsonObject;
        if (modules is null)
        {
            modules = new JsonObject();
            root["Modules"] = modules;
        }

        modules["Enabled"] = new JsonArray(enabled.Select(moduleId => JsonValue.Create(moduleId)).ToArray<JsonNode?>());

        var directory = Path.GetDirectoryName(targetPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllText(
            targetPath,
            root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }),
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    }

    private static IReadOnlyList<string> ResolveEffectiveConfigPaths(EdgeUpdateTarget target)
    {
        var paths = new List<string>
        {
            Path.Combine(target.HostDirectory, "appsettings.json"),
            Path.Combine(target.HostDirectory, $"appsettings.machine.{target.MachineProfile}.json"),
            EdgeClientProgramDataPaths.ResolveMachineProfileConfigPath(target.MachineProfile, target.HostDirectory)
        };

        return paths
            .Where(File.Exists)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static List<string>? ReadEnabledModules(string path)
    {
        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(path));
            var root = document.RootElement;
            if (!root.TryGetProperty("Modules", out var modules)
                || !modules.TryGetProperty("Enabled", out var enabled)
                || enabled.ValueKind != JsonValueKind.Array)
            {
                return null;
            }

            return enabled
                .EnumerateArray()
                .Where(static item => item.ValueKind == JsonValueKind.String)
                .Select(static item => item.GetString()?.Trim())
                .Where(static moduleId => !string.IsNullOrWhiteSpace(moduleId))
                .Select(static moduleId => moduleId!)
                .ToList();
        }
        catch (JsonException)
        {
            return null;
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static string EnsureExternalMachineProfile(EdgeUpdateTarget target)
    {
        var targetPath = EdgeClientProgramDataPaths.ResolveMachineProfileConfigPath(target.MachineProfile, target.HostDirectory);
        if (File.Exists(targetPath))
        {
            return targetPath;
        }

        var packagedPath = Path.Combine(target.HostDirectory, $"appsettings.machine.{target.MachineProfile}.json");
        var directory = Path.GetDirectoryName(targetPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        if (File.Exists(packagedPath))
        {
            File.Copy(packagedPath, targetPath, overwrite: false);
        }
        else
        {
            File.WriteAllText(targetPath, "{}", new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        }

        return targetPath;
    }
}
