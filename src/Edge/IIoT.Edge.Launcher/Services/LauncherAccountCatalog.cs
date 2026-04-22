using IIoT.Edge.Launcher.Models;
using System.IO;
using System.Text.Json;

namespace IIoT.Edge.Launcher.Services;

public sealed class LauncherAccountCatalog : ILauncherAccountCatalog
{
    public const string DefaultCatalogFileName = "launcher.accounts.json";

    public const string SampleCatalogFileName = "launcher.accounts.sample.json";

    private readonly string _catalogPath;

    public LauncherAccountCatalog(string baseDirectory, string catalogFileName = DefaultCatalogFileName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(baseDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(catalogFileName);

        _catalogPath = GetCatalogPath(baseDirectory, catalogFileName);
    }

    public IReadOnlyList<LauncherAccountRecord> LoadAccounts()
    {
        if (!File.Exists(_catalogPath))
        {
            throw new FileNotFoundException($"Launcher account catalog was not found: '{_catalogPath}'.", _catalogPath);
        }

        var json = File.ReadAllText(_catalogPath);
        var entries = JsonSerializer.Deserialize<List<LauncherAccountFileEntry>>(json, JsonOptions())
            ?? [];
        if (entries.Count == 0)
        {
            throw new InvalidOperationException("Launcher account catalog is empty.");
        }

        return entries.Select(Map).ToArray();
    }

    private static LauncherAccountRecord Map(LauncherAccountFileEntry entry)
    {
        if (string.IsNullOrWhiteSpace(entry.UserName))
        {
            throw new InvalidOperationException("Launcher account catalog contains an account without UserName.");
        }

        if (string.IsNullOrWhiteSpace(entry.DisplayName))
        {
            throw new InvalidOperationException($"Launcher account '{entry.UserName}' is missing DisplayName.");
        }

        if (string.IsNullOrWhiteSpace(entry.PasswordHash))
        {
            throw new InvalidOperationException($"Launcher account '{entry.UserName}' is missing PasswordHash.");
        }

        return new LauncherAccountRecord(
            entry.UserName.Trim(),
            entry.DisplayName.Trim(),
            entry.PasswordHash.Trim(),
            entry.IsEnabled ?? true);
    }

    public static string GetCatalogPath(string baseDirectory, string catalogFileName = DefaultCatalogFileName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(baseDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(catalogFileName);

        return Path.Combine(baseDirectory, catalogFileName);
    }

    private static JsonSerializerOptions JsonOptions()
        => new()
        {
            PropertyNameCaseInsensitive = true,
            ReadCommentHandling = JsonCommentHandling.Skip
        };

    private sealed class LauncherAccountFileEntry
    {
        public string? UserName { get; set; }

        public string? DisplayName { get; set; }

        public string? PasswordHash { get; set; }

        public bool? IsEnabled { get; set; }
    }
}
