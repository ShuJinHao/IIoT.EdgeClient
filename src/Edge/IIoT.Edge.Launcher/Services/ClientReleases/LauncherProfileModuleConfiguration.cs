using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using IIoT.Edge.Launcher.Models;
using IIoT.Edge.SharedKernel.Configuration;

namespace IIoT.Edge.Launcher.Services;

public interface ILauncherProfileModuleConfiguration
{
    IReadOnlyList<string> ReadEnabledModules(LauncherProfileDefinition profile);

    void EnableModules(LauncherProfileDefinition profile, IReadOnlyList<string> moduleIds);
}

public sealed class LauncherProfileModuleConfiguration : ILauncherProfileModuleConfiguration
{
    public IReadOnlyList<string> ReadEnabledModules(LauncherProfileDefinition profile)
    {
        ArgumentNullException.ThrowIfNull(profile);

        var hostDirectory = LauncherCloudApiConfigurationResolver.ResolveHostDirectory(profile);
        var enabled = new List<string>();
        foreach (var path in ResolveEffectiveConfigPaths(profile, hostDirectory))
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

    public void EnableModules(LauncherProfileDefinition profile, IReadOnlyList<string> moduleIds)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(moduleIds);

        var hostDirectory = LauncherCloudApiConfigurationResolver.ResolveHostDirectory(profile);
        var targetPath = EnsureExternalMachineProfile(profile, hostDirectory);
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

        var enabled = ReadEnabledModules(profile)
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

    private static IReadOnlyList<string> ResolveEffectiveConfigPaths(
        LauncherProfileDefinition profile,
        string hostDirectory)
    {
        var paths = new List<string>
        {
            Path.Combine(hostDirectory, "appsettings.json"),
            Path.Combine(hostDirectory, $"appsettings.machine.{profile.MachineProfile}.json"),
            EdgeClientProgramDataPaths.ResolveMachineProfileConfigPath(profile.MachineProfile, hostDirectory)
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

    private static string EnsureExternalMachineProfile(LauncherProfileDefinition profile, string hostDirectory)
    {
        var targetPath = EdgeClientProgramDataPaths.ResolveMachineProfileConfigPath(profile.MachineProfile, hostDirectory);
        if (File.Exists(targetPath))
        {
            return targetPath;
        }

        var packagedPath = Path.Combine(hostDirectory, $"appsettings.machine.{profile.MachineProfile}.json");
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
