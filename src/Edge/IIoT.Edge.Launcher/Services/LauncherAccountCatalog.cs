using System.Text.Json;
using IIoT.Edge.Infrastructure.HostPersistence;
using IIoT.Edge.Launcher.Models;
using IIoT.Edge.SharedKernel.Configuration;
using IIoT.Edge.SharedKernel.Security;

namespace IIoT.Edge.Launcher.Services;

public sealed record LauncherAccountCatalogPaths(
    string CatalogPath,
    string SampleCatalogPath,
    string? HostDatabasePath = null);

public sealed class LauncherAccountCatalog : ILauncherAccountCatalog
{
    public const string DefaultCatalogFileName = "launcher.accounts.json";

    public const string SampleCatalogFileName = "launcher.accounts.sample.json";

    private readonly string _catalogPath;
    private readonly LauncherHostDatabase? _hostDatabase;

    public LauncherAccountCatalog(LauncherAccountCatalogPaths paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentException.ThrowIfNullOrWhiteSpace(paths.CatalogPath);

        _catalogPath = paths.CatalogPath;
        _hostDatabase = string.IsNullOrWhiteSpace(paths.HostDatabasePath)
            ? null
            : new LauncherHostDatabase(paths.HostDatabasePath, paths.CatalogPath);
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
        if (_hostDatabase is not null)
        {
            try
            {
                var accounts = _hostDatabase.LoadAccounts();
                return ResolveStatus(accounts.Select(Map).ToArray());
            }
            catch (Exception ex) when (IsCorruptCatalogException(ex) || ex is InvalidDataException)
            {
                return LauncherAccountCatalogStatus.Corrupt;
            }
        }

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
        if (_hostDatabase is not null)
        {
            var accounts = _hostDatabase.LoadAccounts();
            return accounts.Count == 0
                ? throw new InvalidOperationException("host.db 中本地账号为空。")
                : accounts.Select(Map).ToArray();
        }

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

        var account = new LauncherAccountRecord(
            userName.Trim(), displayName.Trim(), passwordHash.Trim(), true, 0, null);
        if (_hostDatabase is not null)
        {
            _hostDatabase.ReplaceAccounts([Map(account)]);
        }
        else
        {
            WriteFileEntries([ToFileEntry(account)]);
        }
    }

    public void UpdatePasswordHash(string userName, string passwordHash)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userName);
        ArgumentException.ThrowIfNullOrWhiteSpace(passwordHash);

        if (_hostDatabase is not null)
        {
            var account = FindAccount(userName);
            _hostDatabase.UpdateAccount(Map(account with
            {
                PasswordHash = passwordHash.Trim(),
                AccessFailedCount = 0,
                LockoutUntilUtc = null
            }));
            return;
        }

        var entries = LoadFileEntries();
        var entry = FindFileEntry(entries, userName);
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

        if (_hostDatabase is not null)
        {
            var account = FindAccount(userName);
            _hostDatabase.UpdateAccount(Map(account with
            {
                AccessFailedCount = accessFailedCount,
                LockoutUntilUtc = lockoutUntilUtc
            }));
            return;
        }

        var entries = LoadFileEntries();
        var entry = FindFileEntry(entries, userName);
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

    internal void EnsureHostDatabase()
        => _hostDatabase?.EnsureCreatedAndMigrate();

    internal void ImportRuntimeBinding(EdgeRuntimeBindingEnvelope runtimeBinding)
        => _hostDatabase?.ImportRuntimeBinding(runtimeBinding);

    private LauncherAccountRecord FindAccount(string userName)
        => _hostDatabase!.LoadAccounts()
               .Select(Map)
               .FirstOrDefault(account =>
                   string.Equals(account.UserName, userName.Trim(), StringComparison.OrdinalIgnoreCase))
           ?? throw new InvalidOperationException($"未找到本地账号：{userName}");

    private static LauncherAccountRecord Map(HostAccountRecord account)
        => new(
            account.UserName,
            account.DisplayName,
            account.PasswordHash,
            account.IsEnabled,
            account.AccessFailedCount,
            account.LockoutUntilUtc);

    private static HostAccountRecord Map(LauncherAccountRecord account)
        => new(
            account.UserName,
            account.DisplayName,
            account.PasswordHash,
            account.IsEnabled,
            account.AccessFailedCount,
            account.LockoutUntilUtc);

    private static LauncherAccountFileEntry FindFileEntry(
        IReadOnlyCollection<LauncherAccountFileEntry> entries,
        string userName)
        => entries.FirstOrDefault(entry =>
               string.Equals(entry.UserName, userName.Trim(), StringComparison.OrdinalIgnoreCase))
           ?? throw new InvalidOperationException($"未找到本地账号：{userName}");

    private static LauncherAccountFileEntry ToFileEntry(LauncherAccountRecord account)
        => new()
        {
            UserName = account.UserName,
            DisplayName = account.DisplayName,
            PasswordHash = account.PasswordHash,
            IsEnabled = account.IsEnabled,
            AccessFailedCount = account.AccessFailedCount,
            LockoutUntilUtc = account.LockoutUntilUtc
        };

    private static LauncherAccountCatalogStatus ResolveStatus(
        IReadOnlyCollection<LauncherAccountRecord> accounts)
    {
        if (accounts.Count == 0)
        {
            return LauncherAccountCatalogStatus.Missing;
        }

        var valid = 0;
        var empty = 0;
        foreach (var account in accounts)
        {
            if (string.IsNullOrWhiteSpace(account.UserName)
                || string.IsNullOrWhiteSpace(account.DisplayName))
            {
                return LauncherAccountCatalogStatus.Corrupt;
            }

            if (string.IsNullOrWhiteSpace(account.PasswordHash))
            {
                empty++;
            }
            else if (LauncherPasswordHasher.Verify(string.Empty, account.PasswordHash)
                     == EdgePasswordVerificationResult.InvalidHash)
            {
                return LauncherAccountCatalogStatus.Corrupt;
            }
            else
            {
                valid++;
            }
        }

        return valid > 0 && empty == 0
            ? LauncherAccountCatalogStatus.Ready
            : valid == 0
                ? LauncherAccountCatalogStatus.NeedsInitialSetup
                : LauncherAccountCatalogStatus.Corrupt;
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
