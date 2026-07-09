using System.Text.Json;
using IIoT.Edge.Launcher.Models;
using IIoT.Edge.SharedKernel.Security;

namespace IIoT.Edge.Launcher.Services;

public sealed record LauncherAccountCatalogPaths(string CatalogPath, string SampleCatalogPath);

public sealed class LauncherAccountCatalog : ILauncherAccountCatalog
{
    public const string DefaultCatalogFileName = "launcher.accounts.json";

    public const string SampleCatalogFileName = "launcher.accounts.sample.json";

    private readonly string _catalogPath;

    public LauncherAccountCatalog(LauncherAccountCatalogPaths paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentException.ThrowIfNullOrWhiteSpace(paths.CatalogPath);

        _catalogPath = paths.CatalogPath;
    }

    public LauncherAccountCatalog(string baseDirectory, string catalogFileName = DefaultCatalogFileName)
        : this(new LauncherAccountCatalogPaths(
            GetCatalogPath(baseDirectory, catalogFileName),
            GetCatalogPath(baseDirectory, SampleCatalogFileName)))
    {
    }

    public static string GetCatalogPath(string baseDirectory, string catalogFileName = DefaultCatalogFileName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(baseDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(catalogFileName);

        return Path.Combine(baseDirectory, catalogFileName);
    }

    public LauncherAccountCatalogStatus GetCatalogStatus()
    {
        if (!File.Exists(_catalogPath))
        {
            return LauncherAccountCatalogStatus.Missing;
        }

        try
        {
            var entries = LoadFileEntries();
            if (entries.Count == 0)
            {
                return LauncherAccountCatalogStatus.Empty;
            }

            var validPasswordHashCount = 0;
            var emptyPasswordHashCount = 0;
            foreach (var entry in entries)
            {
                if (string.IsNullOrWhiteSpace(entry.UserName)
                    || string.IsNullOrWhiteSpace(entry.DisplayName))
                {
                    return LauncherAccountCatalogStatus.Corrupt;
                }

                if (string.IsNullOrWhiteSpace(entry.PasswordHash))
                {
                    emptyPasswordHashCount++;
                    continue;
                }

                var account = Map(entry);
                if (LauncherPasswordHasher.Verify(string.Empty, account.PasswordHash)
                    == EdgePasswordVerificationResult.InvalidHash)
                {
                    return LauncherAccountCatalogStatus.Corrupt;
                }

                validPasswordHashCount++;
            }

            if (validPasswordHashCount > 0 && emptyPasswordHashCount == 0)
            {
                return LauncherAccountCatalogStatus.Ready;
            }

            return validPasswordHashCount == 0
                ? LauncherAccountCatalogStatus.NeedsInitialSetup
                : LauncherAccountCatalogStatus.Corrupt;
        }
        catch (Exception ex) when (IsCorruptCatalogException(ex))
        {
            return LauncherAccountCatalogStatus.Corrupt;
        }
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

    public void InitializeAccount(string userName, string displayName, string passwordHash)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userName);
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);
        ArgumentException.ThrowIfNullOrWhiteSpace(passwordHash);

        var status = GetCatalogStatus();
        if (status is not LauncherAccountCatalogStatus.Missing
            and not LauncherAccountCatalogStatus.Empty
            and not LauncherAccountCatalogStatus.NeedsInitialSetup)
        {
            throw new InvalidOperationException("本地账号文件已存在或已损坏，不能执行首次初始化。");
        }

        WriteFileEntries(
        [
            new LauncherAccountFileEntry
            {
                UserName = userName.Trim(),
                DisplayName = displayName.Trim(),
                PasswordHash = passwordHash.Trim(),
                IsEnabled = true,
                AccessFailedCount = 0,
                LockoutUntilUtc = null
            }
        ]);
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
        entry.AccessFailedCount = 0;
        entry.LockoutUntilUtc = null;
        WriteFileEntries(entries);
    }

    public void UpdateLoginSecurityState(string userName, int accessFailedCount, DateTimeOffset? lockoutUntilUtc)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userName);
        if (accessFailedCount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(accessFailedCount), "失败次数不能为负数。");
        }

        var entries = LoadFileEntries();
        var entry = entries.FirstOrDefault(x =>
            string.Equals(x.UserName, userName.Trim(), StringComparison.OrdinalIgnoreCase));
        if (entry is null)
        {
            throw new InvalidOperationException($"未找到本地账号：{userName}");
        }

        entry.AccessFailedCount = accessFailedCount;
        entry.LockoutUntilUtc = lockoutUntilUtc;
        WriteFileEntries(entries);
    }

    private void WriteFileEntries(List<LauncherAccountFileEntry> entries)
    {
        var directory = Path.GetDirectoryName(_catalogPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var tempPath = Path.Combine(
            string.IsNullOrWhiteSpace(directory) ? "." : directory,
            $".{Path.GetFileName(_catalogPath)}.{Guid.NewGuid():N}.tmp");

        File.WriteAllText(tempPath, JsonSerializer.Serialize(entries, JsonOptionsIndented()));
        try
        {
            if (File.Exists(_catalogPath))
            {
                File.Replace(tempPath, _catalogPath, null);
                return;
            }

            File.Move(tempPath, _catalogPath);
        }
        finally
        {
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }
        }
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
            entry.IsEnabled ?? true,
            Math.Max(0, entry.AccessFailedCount ?? 0),
            entry.LockoutUntilUtc);
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

    private static bool IsCorruptCatalogException(Exception ex)
        => ex is IOException
            or UnauthorizedAccessException
            or InvalidOperationException
            or JsonException;

    private sealed class LauncherAccountFileEntry
    {
        public string? UserName { get; set; }

        public string? DisplayName { get; set; }

        public string? PasswordHash { get; set; }

        public bool? IsEnabled { get; set; }

        public int? AccessFailedCount { get; set; }

        public DateTimeOffset? LockoutUntilUtc { get; set; }
    }
}
