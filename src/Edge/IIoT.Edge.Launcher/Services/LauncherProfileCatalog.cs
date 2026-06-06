using IIoT.Edge.Launcher.Models;
using IIoT.Edge.SharedKernel.Configuration;
using System.IO;
using System.Text.Json;

namespace IIoT.Edge.Launcher.Services;

public sealed class LauncherProfileCatalog : ILauncherProfileCatalog
{
    private const string DefaultExecutableFileName = "IIoT.Edge.Shell";
    private const string DefaultIconKind = "Cog";
    private const string DefaultAccentColor = "#0F766E";
    private const string ModulesDirectoryName = "Modules";
    private const string PluginManifestFileName = "plugin.json";

    private readonly string _baseDirectory;
    private readonly string _catalogPath;

    public LauncherProfileCatalog(string baseDirectory, string catalogFileName = "launcher.profiles.json")
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(baseDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(catalogFileName);

        _baseDirectory = baseDirectory;
        _catalogPath = Path.Combine(baseDirectory, catalogFileName);
    }

    public IReadOnlyList<LauncherProfileDefinition> LoadProfiles()
    {
        if (!File.Exists(_catalogPath))
        {
            throw new FileNotFoundException($"未找到启动器工序清单：'{_catalogPath}'。", _catalogPath);
        }

        var json = File.ReadAllText(_catalogPath);
        var entries = JsonSerializer.Deserialize<List<LauncherProfileFileEntry>>(json, JsonOptions())
            ?? [];
        if (entries.Count == 0)
        {
            throw new InvalidOperationException("启动器工序清单为空。");
        }

        return entries.Select(Map).ToArray();
    }

    private LauncherProfileDefinition Map(LauncherProfileFileEntry entry)
    {
        if (string.IsNullOrWhiteSpace(entry.ProfileId))
        {
            throw new InvalidOperationException("启动器工序清单包含缺少 ProfileId 的工序。");
        }

        if (string.IsNullOrWhiteSpace(entry.DisplayName))
        {
            throw new InvalidOperationException($"启动器工序 '{entry.ProfileId}' 缺少 DisplayName。");
        }

        if (string.IsNullOrWhiteSpace(entry.MachineProfile))
        {
            throw new InvalidOperationException($"启动器工序 '{entry.ProfileId}' 缺少 MachineProfile。");
        }

        var executablePath = string.IsNullOrWhiteSpace(entry.ExecutablePath)
            ? Path.Combine(_baseDirectory, DefaultExecutableFileName)
            : ResolvePath(entry.ExecutablePath);
        var imagePath = string.IsNullOrWhiteSpace(entry.ImagePath)
            ? null
            : ResolvePath(entry.ImagePath);
        var iconKind = string.IsNullOrWhiteSpace(entry.IconKind)
            ? DefaultIconKind
            : entry.IconKind.Trim();
        var accentColor = string.IsNullOrWhiteSpace(entry.AccentColor)
            ? DefaultAccentColor
            : entry.AccentColor.Trim();
        var machineProfile = entry.MachineProfile.Trim();
        var runtimeDirectory = Path.GetDirectoryName(executablePath) ?? _baseDirectory;

        return new LauncherProfileDefinition(
            entry.ProfileId.Trim(),
            entry.DisplayName.Trim(),
            entry.Description?.Trim() ?? string.Empty,
            imagePath,
            machineProfile,
            executablePath,
            iconKind,
            accentColor)
        {
            PluginDisplayPath = ResolvePluginDisplayPath(runtimeDirectory),
            DataDisplayPath = ResolveDataDisplayPath(runtimeDirectory, machineProfile)
        };
    }

    private string ResolvePath(string path)
    {
        var expanded = NormalizePathSeparators(EdgeClientProgramDataPaths.ExpandProgramDataTokens(path.Trim(), _baseDirectory));
        return Path.GetFullPath(
            Path.IsPathRooted(expanded)
                ? expanded
                : Path.Combine(_baseDirectory, expanded));
    }

    private static string NormalizePathSeparators(string path)
        => path.Replace('\\', Path.DirectorySeparatorChar).Replace('/', Path.DirectorySeparatorChar);

    private static string ResolvePluginDisplayPath(string runtimeDirectory)
    {
        var modulesDirectory = Path.Combine(runtimeDirectory, ModulesDirectoryName);
        if (!Directory.Exists(modulesDirectory))
        {
            return string.Empty;
        }

        var manifestPath = Directory
            .EnumerateFiles(modulesDirectory, PluginManifestFileName, SearchOption.AllDirectories)
            .OrderBy(static path => path, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();
        if (manifestPath is null)
        {
            return string.Empty;
        }

        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(manifestPath));
            var root = document.RootElement;
            var moduleId = ReadString(root, "moduleId");
            var entryAssembly = ReadString(root, "entryAssembly");
            if (string.IsNullOrWhiteSpace(moduleId) || string.IsNullOrWhiteSpace(entryAssembly))
            {
                return NormalizeDisplayPath(Path.GetRelativePath(runtimeDirectory, manifestPath));
            }

            return Path.GetFileNameWithoutExtension(entryAssembly);
        }
        catch (JsonException)
        {
            return string.Empty;
        }
        catch (IOException)
        {
            return string.Empty;
        }
        catch (UnauthorizedAccessException)
        {
            return string.Empty;
        }
    }

    private static string ResolveDataDisplayPath(string runtimeDirectory, string machineProfile)
    {
        var configPath = ResolveMachineProfileConfigPath(runtimeDirectory, machineProfile);
        var runtimeDataRoot = ReadRuntimeDataRoot(configPath);
        if (string.IsNullOrWhiteSpace(runtimeDataRoot))
        {
            return NormalizeDisplayPath(EdgeClientProgramDataPaths.ResolveProfileDataRoot(machineProfile, runtimeDirectory));
        }

        var normalizedRoot = NormalizePathSeparators(
            EdgeClientProgramDataPaths.ExpandProgramDataTokens(runtimeDataRoot, runtimeDirectory));
        var absoluteRoot = Path.GetFullPath(
            Path.IsPathRooted(normalizedRoot)
                ? normalizedRoot
                : Path.Combine(runtimeDirectory, normalizedRoot));
        var layoutRoot = Directory.GetParent(runtimeDirectory)?.FullName ?? runtimeDirectory;
        return IsUnderDirectory(layoutRoot, absoluteRoot)
            ? NormalizeDisplayPath(Path.GetRelativePath(layoutRoot, absoluteRoot))
            : NormalizeDisplayPath(absoluteRoot);
    }

    private static string ResolveMachineProfileConfigPath(string runtimeDirectory, string machineProfile)
    {
        var externalConfigPath = EdgeClientProgramDataPaths.ResolveMachineProfileConfigPath(machineProfile, runtimeDirectory);
        return File.Exists(externalConfigPath)
            ? externalConfigPath
            : Path.Combine(runtimeDirectory, $"appsettings.machine.{machineProfile}.json");
    }

    private static string? ReadRuntimeDataRoot(string configPath)
    {
        if (!File.Exists(configPath))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(configPath));
            if (!document.RootElement.TryGetProperty("Shell", out var shell))
            {
                return null;
            }

            return ReadString(shell, "RuntimeDataRoot");
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

    private static string? ReadString(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()?.Trim()
            : null;
    }

    private static string NormalizeDisplayPath(string path)
    {
        var normalized = path.Replace('\\', '/');
        if (Path.IsPathRooted(path) || IsWindowsRootedPath(normalized))
        {
            return normalized;
        }

        return normalized.TrimStart('.', '/');
    }

    private static bool IsUnderDirectory(string parentDirectory, string childPath)
    {
        var parent = Path.GetFullPath(parentDirectory)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;
        var child = Path.GetFullPath(childPath);
        return child.StartsWith(parent, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsWindowsRootedPath(string path)
        => path.Length >= 3
            && char.IsLetter(path[0])
            && path[1] == ':'
            && path[2] == '/';

    private static JsonSerializerOptions JsonOptions()
        => new()
        {
            PropertyNameCaseInsensitive = true,
            ReadCommentHandling = JsonCommentHandling.Skip
        };

    private sealed class LauncherProfileFileEntry
    {
        public string? ProfileId { get; set; }

        public string? DisplayName { get; set; }

        public string? Description { get; set; }

        public string? ImagePath { get; set; }

        public string? MachineProfile { get; set; }

        public string? ExecutablePath { get; set; }

        public string? IconKind { get; set; }

        public string? AccentColor { get; set; }
    }
}
