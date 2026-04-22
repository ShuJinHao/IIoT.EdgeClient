using IIoT.Edge.Launcher.Models;
using System.IO;
using System.Text.Json;

namespace IIoT.Edge.Launcher.Services;

public sealed class LauncherProfileCatalog : ILauncherProfileCatalog
{
    private const string DefaultExecutableFileName = "IIoT.Edge.Shell.exe";
    private const string DefaultIconKind = "Cog";
    private const string DefaultAccentColor = "#0F766E";

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
            throw new FileNotFoundException($"Launcher profile catalog was not found: '{_catalogPath}'.", _catalogPath);
        }

        var json = File.ReadAllText(_catalogPath);
        var entries = JsonSerializer.Deserialize<List<LauncherProfileFileEntry>>(json, JsonOptions())
            ?? [];
        if (entries.Count == 0)
        {
            throw new InvalidOperationException("Launcher profile catalog is empty.");
        }

        return entries.Select(Map).ToArray();
    }

    private LauncherProfileDefinition Map(LauncherProfileFileEntry entry)
    {
        if (string.IsNullOrWhiteSpace(entry.ProfileId))
        {
            throw new InvalidOperationException("Launcher profile catalog contains a profile without ProfileId.");
        }

        if (string.IsNullOrWhiteSpace(entry.DisplayName))
        {
            throw new InvalidOperationException($"Launcher profile '{entry.ProfileId}' is missing DisplayName.");
        }

        if (string.IsNullOrWhiteSpace(entry.MachineProfile))
        {
            throw new InvalidOperationException($"Launcher profile '{entry.ProfileId}' is missing MachineProfile.");
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

        return new LauncherProfileDefinition(
            entry.ProfileId.Trim(),
            entry.DisplayName.Trim(),
            entry.Description?.Trim() ?? string.Empty,
            imagePath,
            entry.MachineProfile.Trim(),
            executablePath,
            iconKind,
            accentColor);
    }

    private string ResolvePath(string path)
    {
        var expanded = Environment.ExpandEnvironmentVariables(path);
        return Path.GetFullPath(
            Path.IsPathRooted(expanded)
                ? expanded
                : Path.Combine(_baseDirectory, expanded));
    }

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
