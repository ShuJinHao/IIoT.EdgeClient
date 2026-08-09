using System.Globalization;
using System.Text.Json;
using IIoT.Edge.SharedKernel.Configuration;
using Microsoft.Data.Sqlite;

namespace IIoT.Edge.Infrastructure.HostPersistence;

public sealed class LauncherHostDatabase(string databasePath, string? legacyAccountCatalogPath)
{
    private const int CurrentSchemaVersion = 1;
    private string RecoveryDatabasePath => Path.GetFullPath(databasePath) + ".recovery";

    public void EnsureCreatedAndMigrate()
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);
        var fullPath = Path.GetFullPath(databasePath);
        var directory = Path.GetDirectoryName(fullPath)
            ?? throw new InvalidOperationException("host.db directory is unavailable.");
        Directory.CreateDirectory(directory);

        if (File.Exists(fullPath))
        {
            try
            {
                using var existing = Open(fullPath, create: false);
                ValidateIntegrity(existing);
                ValidateAndMigrateSchema(existing);
            }
            catch (Exception ex) when (ex is SqliteException or InvalidDataException)
            {
                if (!TryRecoverFromIndependentBackup(fullPath))
                {
                    throw new InvalidDataException(
                        "host.db 损坏或 Schema 不受支持，且独立恢复副本不可用。",
                        ex);
                }
            }

            return;
        }

        var staging = Path.Combine(directory, $".{Path.GetFileName(fullPath)}.{Guid.NewGuid():N}.staging");
        try
        {
            using (var connection = Open(staging, create: true))
            {
                CreateSchemaV1(connection);
                ImportLegacyAccounts(connection);
                ImportLegacyHostSettings(connection);
                ValidateIntegrity(connection);
                ValidateAndMigrateSchema(connection);
            }

            File.Move(staging, fullPath);
            RefreshRecoveryBackup();
        }
        finally
        {
            if (File.Exists(staging))
            {
                File.Delete(staging);
            }
        }
    }

    public IReadOnlyList<HostAccountRecord> LoadAccounts()
    {
        EnsureCreatedAndMigrate();
        using var connection = Open(databasePath);
        using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT user_name, display_name, password_hash, is_enabled, access_failed_count, lockout_until_utc " +
            "FROM launcher_accounts ORDER BY user_name COLLATE NOCASE;";
        using var reader = command.ExecuteReader();
        var accounts = new List<HostAccountRecord>();
        while (reader.Read())
        {
            accounts.Add(new HostAccountRecord(
                reader.GetString(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetInt64(3) != 0,
                checked((int)reader.GetInt64(4)),
                reader.IsDBNull(5)
                    ? null
                    : DateTimeOffset.Parse(
                        reader.GetString(5),
                        CultureInfo.InvariantCulture,
                        DateTimeStyles.RoundtripKind)));
        }

        return accounts;
    }

    public void ReplaceAccounts(IReadOnlyCollection<HostAccountRecord> accounts)
    {
        EnsureCreatedAndMigrate();
        using var connection = Open(databasePath);
        using var transaction = connection.BeginTransaction();
        using (var delete = connection.CreateCommand())
        {
            delete.Transaction = transaction;
            delete.CommandText = "DELETE FROM launcher_accounts;";
            delete.ExecuteNonQuery();
        }

        foreach (var account in accounts)
        {
            UpsertAccount(connection, transaction, account);
        }

        transaction.Commit();
        RefreshRecoveryBackup();
    }

    public void UpdateAccount(HostAccountRecord account)
    {
        EnsureCreatedAndMigrate();
        using var connection = Open(databasePath);
        using var transaction = connection.BeginTransaction();
        UpsertAccount(connection, transaction, account);
        transaction.Commit();
        RefreshRecoveryBackup();
    }

    public void ImportRuntimeBinding(EdgeRuntimeBindingEnvelope runtimeBinding)
    {
        ArgumentNullException.ThrowIfNull(runtimeBinding);
        EnsureCreatedAndMigrate();
        using var connection = Open(databasePath);
        ReplaceRuntimeBinding(connection, runtimeBinding);
        RefreshRecoveryBackup();
    }

    /// <summary>
    /// Builds a complete, integrity-checked host.db image without mutating the live database.
    /// Binding files, credentials, host.db and its independent recovery copy can therefore be
    /// switched and rolled back as one Launcher import transaction.
    /// </summary>
    public LauncherHostDatabaseSnapshot PrepareRuntimeBindingImport(
        EdgeRuntimeBindingEnvelope runtimeBinding)
    {
        ArgumentNullException.ThrowIfNull(runtimeBinding);
        var fullPath = Path.GetFullPath(databasePath);
        var directory = Path.GetDirectoryName(fullPath)
            ?? throw new InvalidOperationException("host.db directory is unavailable.");
        Directory.CreateDirectory(directory);
        var staging = Path.Combine(
            directory,
            $".{Path.GetFileName(fullPath)}.{Guid.NewGuid():N}.import-staging");
        try
        {
            using (var destination = Open(staging, create: true))
            {
                if (File.Exists(fullPath))
                {
                    using var source = Open(fullPath, create: false);
                    ValidateIntegrity(source);
                    ValidateAndMigrateSchema(source);
                    source.BackupDatabase(destination);
                }
                else
                {
                    CreateSchemaV1(destination);
                    ImportLegacyAccounts(destination);
                    ImportLegacyHostSettings(destination);
                }

                ReplaceRuntimeBinding(destination, runtimeBinding);
                ValidateIntegrity(destination);
                ValidateAndMigrateSchema(destination);
                ValidateRuntimeBindingImport(destination, runtimeBinding);
                using var checkpoint = destination.CreateCommand();
                checkpoint.CommandText = "PRAGMA wal_checkpoint(TRUNCATE);";
                checkpoint.ExecuteNonQuery();
                using var journal = destination.CreateCommand();
                journal.CommandText = "PRAGMA journal_mode=DELETE;";
                _ = journal.ExecuteScalar();
            }

            if (File.Exists(staging + "-wal") || File.Exists(staging + "-shm"))
            {
                throw new InvalidDataException(
                    "Staged host.db retained WAL sidecars and cannot be switched atomically.");
            }

            var bytes = File.ReadAllBytes(staging);
            if (bytes.Length == 0)
            {
                throw new InvalidDataException("Staged host.db is empty.");
            }

            return new LauncherHostDatabaseSnapshot(bytes);
        }
        finally
        {
            DeleteIfPresent(staging);
            DeleteIfPresent(staging + "-wal");
            DeleteIfPresent(staging + "-shm");
        }
    }

    private static void ReplaceRuntimeBinding(
        SqliteConnection connection,
        EdgeRuntimeBindingEnvelope runtimeBinding)
    {
        using var transaction = connection.BeginTransaction();
        using (var delete = connection.CreateCommand())
        {
            delete.Transaction = transaction;
            delete.CommandText = "DELETE FROM installed_device_plugins;";
            delete.ExecuteNonQuery();
        }

        foreach (var binding in runtimeBinding.Bindings)
        {
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText =
                "INSERT INTO installed_device_plugins " +
                "(client_code, module_id, plugin_version, package_sha256, activation_status, generation_id, imported_at_utc) " +
                "VALUES ($client, $module, $version, $sha, $status, $generation, $imported) " +
                "ON CONFLICT(client_code) DO UPDATE SET module_id=excluded.module_id, " +
                "plugin_version=excluded.plugin_version, package_sha256=excluded.package_sha256, " +
                "activation_status=excluded.activation_status, generation_id=excluded.generation_id, " +
                "imported_at_utc=excluded.imported_at_utc;";
            command.Parameters.AddWithValue("$client", binding.ClientCode);
            command.Parameters.AddWithValue("$module", binding.ModuleId);
            command.Parameters.AddWithValue("$version", binding.PluginVersion);
            command.Parameters.AddWithValue("$sha", binding.PackageSha256);
            command.Parameters.AddWithValue("$status", binding.ActivationStatus);
            command.Parameters.AddWithValue("$generation", runtimeBinding.GenerationId);
            command.Parameters.AddWithValue("$imported", DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture));
            command.ExecuteNonQuery();
        }

        using (var history = connection.CreateCommand())
        {
            history.Transaction = transaction;
            history.CommandText =
                "INSERT INTO host_update_history " +
                "(component, version, status, occurred_at_utc) " +
                "VALUES ($component, $version, $status, $occurred);";
            history.Parameters.AddWithValue("$component", "RuntimeBinding");
            history.Parameters.AddWithValue("$version", runtimeBinding.GenerationId);
            history.Parameters.AddWithValue("$status", "Imported");
            history.Parameters.AddWithValue(
                "$occurred",
                DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture));
            history.ExecuteNonQuery();
        }

        transaction.Commit();
    }

    private static void ValidateRuntimeBindingImport(
        SqliteConnection connection,
        EdgeRuntimeBindingEnvelope runtimeBinding)
    {
        using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT client_code, module_id, plugin_version, package_sha256, activation_status, generation_id " +
            "FROM installed_device_plugins ORDER BY client_code COLLATE NOCASE;";
        using var reader = command.ExecuteReader();
        var actual = new Dictionary<string, string[]>(StringComparer.Ordinal);
        while (reader.Read())
        {
            if (!actual.TryAdd(
                    reader.GetString(0),
                    [
                        reader.GetString(1),
                        reader.GetString(2),
                        reader.GetString(3),
                        reader.GetString(4),
                        reader.GetString(5)
                    ]))
            {
                throw new InvalidDataException("host.db contains duplicate ClientCode rows.");
            }
        }

        if (actual.Count != runtimeBinding.Bindings.Count)
        {
            throw new InvalidDataException("host.db installed-device count does not match runtime Binding.");
        }

        foreach (var binding in runtimeBinding.Bindings)
        {
            if (!actual.TryGetValue(binding.ClientCode, out var facts)
                || !string.Equals(facts[0], binding.ModuleId, StringComparison.Ordinal)
                || !string.Equals(facts[1], binding.PluginVersion, StringComparison.Ordinal)
                || !string.Equals(facts[2], binding.PackageSha256, StringComparison.OrdinalIgnoreCase)
                || !string.Equals(facts[3], binding.ActivationStatus, StringComparison.Ordinal)
                || !string.Equals(facts[4], runtimeBinding.GenerationId, StringComparison.Ordinal))
            {
                throw new InvalidDataException(
                    $"host.db installed-device facts do not match runtime Binding: {binding.ClientCode}.");
            }
        }
    }

    private void ImportLegacyAccounts(SqliteConnection connection)
    {
        if (string.IsNullOrWhiteSpace(legacyAccountCatalogPath)
            || !File.Exists(legacyAccountCatalogPath))
        {
            return;
        }

        var entries = JsonSerializer.Deserialize<List<LegacyAccountEntry>>(
            File.ReadAllText(legacyAccountCatalogPath),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? [];
        using var transaction = connection.BeginTransaction();
        foreach (var entry in entries)
        {
            if (string.IsNullOrWhiteSpace(entry.UserName)
                || string.IsNullOrWhiteSpace(entry.DisplayName))
            {
                throw new InvalidDataException("Legacy launcher account catalog is incomplete.");
            }

            UpsertAccount(connection, transaction, new HostAccountRecord(
                entry.UserName.Trim(),
                entry.DisplayName.Trim(),
                entry.PasswordHash?.Trim() ?? string.Empty,
                entry.IsEnabled ?? true,
                Math.Max(0, entry.AccessFailedCount ?? 0),
                entry.LockoutUntilUtc));
        }

        transaction.Commit();
    }

    private void ImportLegacyHostSettings(SqliteConnection connection)
    {
        if (string.IsNullOrWhiteSpace(legacyAccountCatalogPath))
        {
            return;
        }

        var launcherDirectory = Path.GetDirectoryName(Path.GetFullPath(legacyAccountCatalogPath));
        if (string.IsNullOrWhiteSpace(launcherDirectory))
        {
            return;
        }

        using var transaction = connection.BeginTransaction();
        foreach (var fileName in new[]
                 {
                     EdgeClientProgramDataPaths.LanguageFileName,
                     EdgeClientProgramDataPaths.LauncherUpdateConfigFileName
                 })
        {
            var path = Path.Combine(launcherDirectory, fileName);
            if (!File.Exists(path))
            {
                continue;
            }

            using var document = JsonDocument.Parse(File.ReadAllText(path));
            var value = document.RootElement.GetRawText();
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText =
                "INSERT INTO host_settings(setting_key, setting_value, updated_at_utc) " +
                "VALUES ($key, $value, $updated) " +
                "ON CONFLICT(setting_key) DO NOTHING;";
            command.Parameters.AddWithValue("$key", $"legacy-json:{fileName}");
            command.Parameters.AddWithValue("$value", value);
            command.Parameters.AddWithValue(
                "$updated",
                File.GetLastWriteTimeUtc(path).ToString("O", CultureInfo.InvariantCulture));
            command.ExecuteNonQuery();
        }

        transaction.Commit();
    }

    private static void CreateSchemaV1(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText =
            $$"""
            PRAGMA journal_mode=WAL;
            PRAGMA foreign_keys=ON;
            CREATE TABLE IF NOT EXISTS host_schema (
                schema_version INTEGER NOT NULL,
                updated_at_utc TEXT NOT NULL
            );
            CREATE TABLE IF NOT EXISTS launcher_accounts (
                user_name TEXT NOT NULL COLLATE NOCASE PRIMARY KEY,
                display_name TEXT NOT NULL,
                password_hash TEXT NOT NULL,
                is_enabled INTEGER NOT NULL,
                access_failed_count INTEGER NOT NULL,
                lockout_until_utc TEXT NULL
            );
            CREATE TABLE IF NOT EXISTS installed_device_plugins (
                client_code TEXT NOT NULL PRIMARY KEY,
                module_id TEXT NOT NULL,
                plugin_version TEXT NOT NULL,
                package_sha256 TEXT NOT NULL,
                activation_status TEXT NOT NULL,
                generation_id TEXT NOT NULL,
                imported_at_utc TEXT NOT NULL
            );
            CREATE TABLE IF NOT EXISTS host_settings (
                setting_key TEXT NOT NULL PRIMARY KEY,
                setting_value TEXT NOT NULL,
                updated_at_utc TEXT NOT NULL
            );
            CREATE TABLE IF NOT EXISTS host_update_history (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                component TEXT NOT NULL,
                version TEXT NOT NULL,
                status TEXT NOT NULL,
                occurred_at_utc TEXT NOT NULL
            );
            INSERT INTO host_schema(schema_version, updated_at_utc)
            VALUES ({{CurrentSchemaVersion}}, '{{DateTimeOffset.UtcNow:O}}');
            """;
        command.ExecuteNonQuery();
    }

    private static void ValidateAndMigrateSchema(SqliteConnection connection)
    {
        using var tableCommand = connection.CreateCommand();
        tableCommand.CommandText =
            "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='host_schema';";
        if (Convert.ToInt32(tableCommand.ExecuteScalar(), CultureInfo.InvariantCulture) != 1)
        {
            throw new InvalidDataException("host.db 缺少版本表，禁止猜测迁移。");
        }

        using var versionCommand = connection.CreateCommand();
        versionCommand.CommandText = "SELECT schema_version FROM host_schema;";
        var versions = new List<int>();
        using (var reader = versionCommand.ExecuteReader())
        {
            while (reader.Read())
            {
                versions.Add(reader.GetInt32(0));
            }
        }

        if (versions.Count != 1)
        {
            throw new InvalidDataException("host.db Schema 版本记录不唯一。");
        }

        var version = versions[0];
        if (version > CurrentSchemaVersion || version <= 0)
        {
            throw new InvalidDataException($"host.db Schema 版本 {version} 不受支持。");
        }

        // Future migrations must be explicit version-to-version transactions. Version 1 is the
        // only currently released host schema; opening it must never rewrite the version row.
        if (version != CurrentSchemaVersion)
        {
            throw new InvalidDataException($"host.db Schema {version} 缺少显式迁移路径。");
        }
    }

    private static void ValidateIntegrity(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA integrity_check;";
        var result = Convert.ToString(command.ExecuteScalar(), CultureInfo.InvariantCulture);
        if (!string.Equals(result, "ok", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException($"host.db integrity check failed: {result ?? "empty"}");
        }
    }

    private static SqliteConnection Open(string path, bool create = true)
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = Path.GetFullPath(path),
            Mode = create ? SqliteOpenMode.ReadWriteCreate : SqliteOpenMode.ReadWrite,
            Cache = SqliteCacheMode.Shared
        }.ToString());
        connection.Open();
        return connection;
    }

    private void RefreshRecoveryBackup()
    {
        var fullPath = Path.GetFullPath(databasePath);
        if (!File.Exists(fullPath))
        {
            return;
        }

        var recoveryPath = RecoveryDatabasePath;
        var staging = recoveryPath + $".{Guid.NewGuid():N}.staging";
        try
        {
            using (var source = Open(fullPath, create: false))
            using (var destination = Open(staging, create: true))
            {
                source.BackupDatabase(destination);
                ValidateIntegrity(destination);
                ValidateAndMigrateSchema(destination);
            }

            File.Move(staging, recoveryPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(staging))
            {
                File.Delete(staging);
            }
        }
    }

    private bool TryRecoverFromIndependentBackup(string fullPath)
    {
        var recoveryPath = RecoveryDatabasePath;
        if (!File.Exists(recoveryPath))
        {
            return false;
        }

        try
        {
            using (var recovery = Open(recoveryPath, create: false))
            {
                ValidateIntegrity(recovery);
                ValidateAndMigrateSchema(recovery);
            }

            var corruptPath = fullPath + $".corrupt.{DateTime.UtcNow:yyyyMMddHHmmssfff}";
            File.Move(fullPath, corruptPath);
            try
            {
                File.Copy(recoveryPath, fullPath, overwrite: false);
                using var restored = Open(fullPath, create: false);
                ValidateIntegrity(restored);
                ValidateAndMigrateSchema(restored);
                return true;
            }
            catch
            {
                if (File.Exists(fullPath))
                {
                    File.Delete(fullPath);
                }

                File.Move(corruptPath, fullPath);
                throw;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SqliteException or InvalidDataException)
        {
            return false;
        }
    }

    private static void UpsertAccount(
        SqliteConnection connection,
        SqliteTransaction transaction,
        HostAccountRecord account)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            "INSERT INTO launcher_accounts " +
            "(user_name, display_name, password_hash, is_enabled, access_failed_count, lockout_until_utc) " +
            "VALUES ($user, $display, $hash, $enabled, $failed, $lockout) " +
            "ON CONFLICT(user_name) DO UPDATE SET display_name=excluded.display_name, " +
            "password_hash=excluded.password_hash, is_enabled=excluded.is_enabled, " +
            "access_failed_count=excluded.access_failed_count, lockout_until_utc=excluded.lockout_until_utc;";
        command.Parameters.AddWithValue("$user", account.UserName.Trim());
        command.Parameters.AddWithValue("$display", account.DisplayName.Trim());
        command.Parameters.AddWithValue("$hash", account.PasswordHash.Trim());
        command.Parameters.AddWithValue("$enabled", account.IsEnabled ? 1 : 0);
        command.Parameters.AddWithValue("$failed", Math.Max(0, account.AccessFailedCount));
        command.Parameters.AddWithValue(
            "$lockout",
            account.LockoutUntilUtc.HasValue
                ? account.LockoutUntilUtc.Value.ToString("O", CultureInfo.InvariantCulture)
                : DBNull.Value);
        command.ExecuteNonQuery();
    }

    private static void DeleteIfPresent(string path)
    {
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }

    private sealed class LegacyAccountEntry
    {
        public string? UserName { get; set; }
        public string? DisplayName { get; set; }
        public string? PasswordHash { get; set; }
        public bool? IsEnabled { get; set; }
        public int? AccessFailedCount { get; set; }
        public DateTimeOffset? LockoutUntilUtc { get; set; }
    }
}

public sealed record LauncherHostDatabaseSnapshot(byte[] DatabaseBytes);

public sealed record HostAccountRecord(
    string UserName,
    string DisplayName,
    string PasswordHash,
    bool IsEnabled,
    int AccessFailedCount,
    DateTimeOffset? LockoutUntilUtc);
