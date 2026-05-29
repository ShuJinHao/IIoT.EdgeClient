using System.Text.Json;
using IIoT.Edge.Launcher.Models;

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
            throw new FileNotFoundException($"本地账号文件不存在：{_catalogPath}", _catalogPath);
        }

        var entries = LoadFileEntries();
        if (entries.Count == 0)
        {
            throw new InvalidOperationException("本地账号文件为空。");
        }

        return entries.Select(Map).ToArray();
    }

    public void UpdatePasswordHash(string userName, string passwordHash)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userName);
        ArgumentException.ThrowIfNullOrWhiteSpace(passwordHash);

        var entries = LoadFileEntries();
        var entry = entries.FirstOrDefault(x =>
            string.Equals(x.UserName, userName.Trim(), StringComparison.OrdinalIgnoreCase));
        if (entry is null)
        {
            throw new InvalidOperationException($"未找到本地账号：{userName}");
        }

        entry.PasswordHash = passwordHash.Trim();
        File.WriteAllText(
            _catalogPath,
            JsonSerializer.Serialize(entries, JsonOptionsIndented()));
    }

    public static string GetCatalogPath(string baseDirectory, string catalogFileName = DefaultCatalogFileName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(baseDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(catalogFileName);

        return Path.Combine(baseDirectory, catalogFileName);
    }

    private static LauncherAccountRecord Map(LauncherAccountFileEntry entry)
    {
        if (string.IsNullOrWhiteSpace(entry.UserName))
        {
            throw new InvalidOperationException("本地账号文件包含缺少账号名的记录。");
        }

        if (string.IsNullOrWhiteSpace(entry.DisplayName))
        {
            throw new InvalidOperationException($"本地账号 '{entry.UserName}' 缺少显示名称。");
        }

        if (string.IsNullOrWhiteSpace(entry.PasswordHash))
        {
            throw new InvalidOperationException($"本地账号 '{entry.UserName}' 缺少密码哈希。");
        }

        return new LauncherAccountRecord(
            entry.UserName.Trim(),
            entry.DisplayName.Trim(),
            entry.PasswordHash.Trim(),
            entry.IsEnabled ?? true);
    }

    private static JsonSerializerOptions JsonOptions()
        => new()
        {
            PropertyNameCaseInsensitive = true,
            ReadCommentHandling = JsonCommentHandling.Skip
        };

    private static JsonSerializerOptions JsonOptionsIndented()
    {
        var options = JsonOptions();
        options.WriteIndented = true;
        return options;
    }

    private List<LauncherAccountFileEntry> LoadFileEntries()
    {
        if (!File.Exists(_catalogPath))
        {
            throw new FileNotFoundException($"本地账号文件不存在：{_catalogPath}", _catalogPath);
        }

        var json = File.ReadAllText(_catalogPath);
        return JsonSerializer.Deserialize<List<LauncherAccountFileEntry>>(json, JsonOptions())
            ?? [];
    }

    private sealed class LauncherAccountFileEntry
    {
        public string? UserName { get; set; }

        public string? DisplayName { get; set; }

        public string? PasswordHash { get; set; }

        public bool? IsEnabled { get; set; }
    }
}
