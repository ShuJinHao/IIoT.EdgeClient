using IIoT.Edge.Application.Abstractions.Logging;
using Microsoft.Data.Sqlite;
using System.Globalization;

namespace IIoT.Edge.Module.DieCutting.Production;

public sealed class DieCuttingProductionRecordStore
    : IDieCuttingProductionRecordStore
{
    private const string AllDeviceFilterKey = "__all__";
    private const string DbName = "diecutting_plugin.db";
    private const string TableName = "diecutting_production_records";
    private const int BusyTimeoutMs = 5000;
    private static readonly int[] WriteRetryDelaysMs = [100, 250, 500, 1000, 2000];

    private readonly string _dbDirectory;
    private readonly ILogService _logger;
    private readonly SemaphoreSlim _initializeLock = new(1, 1);
    private bool _initialized;

    public DieCuttingProductionRecordStore(
        string dbDirectory,
        ILogService logger)
    {
        if (string.IsNullOrWhiteSpace(dbDirectory))
        {
            throw new ArgumentNullException(nameof(dbDirectory));
        }

        _dbDirectory = dbDirectory;
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    private string DbPath => Path.Combine(_dbDirectory, DbName);

    private const string CreateTableSql = @"
        CREATE TABLE IF NOT EXISTS diecutting_production_records (
            Id               INTEGER PRIMARY KEY AUTOINCREMENT,
            ModuleId         TEXT    NOT NULL,
            DeviceName       TEXT    NOT NULL,
            BatchNo          TEXT    NOT NULL,
            ClipNo           TEXT    NOT NULL DEFAULT '',
            Quantity         INTEGER NOT NULL,
            WindowStartAt    TEXT    NOT NULL,
            WindowCompleteAt TEXT    NOT NULL,
            PunchingSpeed    REAL    NOT NULL,
            PlateLengthMm    REAL    NULL,
            PlateWidthMm     REAL    NULL,
            OperatorCode     TEXT    NOT NULL DEFAULT '',
            MoldCode         TEXT    NOT NULL DEFAULT '',
            CutterCode       TEXT    NOT NULL DEFAULT '',
            RawFieldsJson    TEXT    NOT NULL DEFAULT '',
            CreatedAtUtc     TEXT    NOT NULL
        );
        CREATE INDEX IF NOT EXISTS idx_diecutting_production_module_device_time
            ON diecutting_production_records (ModuleId, DeviceName, WindowCompleteAt);
    ";

    public async Task AddAsync(DieCuttingProductionRecord record, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(record);
        cancellationToken.ThrowIfCancellationRequested();

        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);

        await ExecuteWriteAsync(
            async connection =>
            {
                await using var command = connection.CreateCommand();
                command.CommandText = @"
            INSERT INTO diecutting_production_records
                (ModuleId, DeviceName, BatchNo, Quantity, WindowStartAt, WindowCompleteAt,
                 PunchingSpeed, PlateLengthMm, PlateWidthMm, CreatedAtUtc,
                 ClipNo, OperatorCode, MoldCode, CutterCode, RawFieldsJson)
            VALUES
                (@ModuleId, @DeviceName, @BatchNo, @Quantity, @WindowStartAt, @WindowCompleteAt,
                 @PunchingSpeed, @PlateLengthMm, @PlateWidthMm, @CreatedAtUtc,
                 @ClipNo, @OperatorCode, @MoldCode, @CutterCode, @RawFieldsJson)";

                AddParameter(command, "@ModuleId", record.ModuleId);
                AddParameter(command, "@DeviceName", record.DeviceName);
                AddParameter(command, "@BatchNo", record.BatchNo);
                AddParameter(command, "@ClipNo", record.ClipNo);
                AddParameter(command, "@Quantity", record.Quantity);
                AddParameter(command, "@WindowStartAt", record.WindowStartAt.ToString("O", CultureInfo.InvariantCulture));
                AddParameter(command, "@WindowCompleteAt", record.WindowCompleteAt.ToString("O", CultureInfo.InvariantCulture));
                AddParameter(command, "@PunchingSpeed", Convert.ToDouble(record.PunchingSpeed, CultureInfo.InvariantCulture));
                AddParameter(command, "@PlateLengthMm", ToSqliteValue(record.PlateLengthMm));
                AddParameter(command, "@PlateWidthMm", ToSqliteValue(record.PlateWidthMm));
                AddParameter(command, "@OperatorCode", record.OperatorCode);
                AddParameter(command, "@MoldCode", record.MoldCode);
                AddParameter(command, "@CutterCode", record.CutterCode);
                AddParameter(command, "@RawFieldsJson", record.RawFieldsJson);
                AddParameter(command, "@CreatedAtUtc", record.CreatedAtUtc.ToString("O", CultureInfo.InvariantCulture));

                return await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            },
            cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<DieCuttingProductionRecord>> QueryAsync(
        string moduleId,
        string selectedDeviceKey,
        int limit = 500,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentException.ThrowIfNullOrWhiteSpace(moduleId);

        var normalizedLimit = Math.Clamp(limit, 1, 5000);
        var isAllSelected = string.IsNullOrWhiteSpace(selectedDeviceKey)
            || string.Equals(selectedDeviceKey, AllDeviceFilterKey, StringComparison.OrdinalIgnoreCase);

        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);

        await using var connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = isAllSelected
            ? $@"
                SELECT Id, ModuleId, DeviceName, BatchNo, Quantity, WindowStartAt, WindowCompleteAt,
                       PunchingSpeed, PlateLengthMm, PlateWidthMm, CreatedAtUtc,
                       ClipNo, OperatorCode, MoldCode, CutterCode, RawFieldsJson
                FROM {TableName}
                WHERE ModuleId = @ModuleId
                ORDER BY WindowCompleteAt DESC, Id DESC
                LIMIT @Limit"
            : $@"
                SELECT Id, ModuleId, DeviceName, BatchNo, Quantity, WindowStartAt, WindowCompleteAt,
                       PunchingSpeed, PlateLengthMm, PlateWidthMm, CreatedAtUtc,
                       ClipNo, OperatorCode, MoldCode, CutterCode, RawFieldsJson
                FROM {TableName}
                WHERE ModuleId = @ModuleId
                  AND DeviceName = @DeviceName
                ORDER BY WindowCompleteAt DESC, Id DESC
                LIMIT @Limit";

        AddParameter(command, "@ModuleId", moduleId);
        AddParameter(command, "@DeviceName", selectedDeviceKey);
        AddParameter(command, "@Limit", normalizedLimit);

        var rows = new List<DieCuttingProductionRecord>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            rows.Add(ReadRecord(reader));
        }

        return rows;
    }

    private async Task EnsureInitializedAsync(CancellationToken cancellationToken)
    {
        if (_initialized)
        {
            return;
        }

        await _initializeLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_initialized)
            {
                return;
            }

            Directory.CreateDirectory(_dbDirectory);
            await using var connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
            await using var command = connection.CreateCommand();
            command.CommandText = CreateTableSql;
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            await EnsureColumnAsync(connection, "PlateLengthMm", "REAL NULL", cancellationToken).ConfigureAwait(false);
            await EnsureColumnAsync(connection, "PlateWidthMm", "REAL NULL", cancellationToken).ConfigureAwait(false);
            await EnsureColumnAsync(connection, "ClipNo", "TEXT NOT NULL DEFAULT ''", cancellationToken).ConfigureAwait(false);
            await EnsureColumnAsync(connection, "OperatorCode", "TEXT NOT NULL DEFAULT ''", cancellationToken).ConfigureAwait(false);
            await EnsureColumnAsync(connection, "MoldCode", "TEXT NOT NULL DEFAULT ''", cancellationToken).ConfigureAwait(false);
            await EnsureColumnAsync(connection, "CutterCode", "TEXT NOT NULL DEFAULT ''", cancellationToken).ConfigureAwait(false);
            await EnsureColumnAsync(connection, "RawFieldsJson", "TEXT NOT NULL DEFAULT ''", cancellationToken).ConfigureAwait(false);
            _initialized = true;
        }
        catch (Exception ex)
        {
            _logger.Error($"[DieCuttingProduction] 初始化插件生产数据表失败: {ex.Message}");
            throw;
        }
        finally
        {
            _initializeLock.Release();
        }
    }

    private async Task<SqliteConnection> OpenConnectionAsync(CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(_dbDirectory);
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = DbPath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared,
            Pooling = false
        }.ToString());

        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await ExecutePragmaAsync(connection, "PRAGMA journal_mode=WAL;", cancellationToken).ConfigureAwait(false);
        await ExecutePragmaAsync(connection, "PRAGMA foreign_keys=ON;", cancellationToken).ConfigureAwait(false);
        await ExecutePragmaAsync(connection, $"PRAGMA busy_timeout={BusyTimeoutMs};", cancellationToken).ConfigureAwait(false);
        return connection;
    }

    private async Task ExecuteWriteAsync(
        Func<SqliteConnection, Task<int>> action,
        CancellationToken cancellationToken)
    {
        for (var attempt = 0; ; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                await using var connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
                await action(connection).ConfigureAwait(false);
                return;
            }
            catch (SqliteException ex) when (IsBusyOrLocked(ex) && attempt < WriteRetryDelaysMs.Length)
            {
                var delayMs = WriteRetryDelaysMs[attempt];
                _logger.Warn(
                    $"[DieCuttingProduction] 插件生产数据写入 busy/locked，准备重试 ({Path.GetFileName(DbPath)}) - attempt {attempt + 1}/{WriteRetryDelaysMs.Length}, delay {delayMs}ms, sqlite={ex.SqliteErrorCode}");
                await Task.Delay(delayMs, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.Error($"[DieCuttingProduction] 插件生产数据写入失败: {ex.Message}");
                throw;
            }
        }
    }

    private static async Task ExecutePragmaAsync(
        SqliteConnection connection,
        string pragma,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = pragma;
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static DieCuttingProductionRecord ReadRecord(SqliteDataReader reader)
        => new()
        {
            Id = reader.GetInt64(reader.GetOrdinal("Id")),
            ModuleId = reader.GetString(reader.GetOrdinal("ModuleId")),
            DeviceName = reader.GetString(reader.GetOrdinal("DeviceName")),
            BatchNo = reader.GetString(reader.GetOrdinal("BatchNo")),
            ClipNo = ReadString(reader, "ClipNo"),
            Quantity = reader.GetInt64(reader.GetOrdinal("Quantity")),
            WindowStartAt = ReadDateTime(reader, "WindowStartAt"),
            WindowCompleteAt = ReadDateTime(reader, "WindowCompleteAt"),
            PunchingSpeed = ReadDecimal(reader, "PunchingSpeed"),
            PlateLengthMm = ReadNullableDecimal(reader, "PlateLengthMm"),
            PlateWidthMm = ReadNullableDecimal(reader, "PlateWidthMm"),
            OperatorCode = ReadString(reader, "OperatorCode"),
            MoldCode = ReadString(reader, "MoldCode"),
            CutterCode = ReadString(reader, "CutterCode"),
            RawFieldsJson = ReadString(reader, "RawFieldsJson"),
            CreatedAtUtc = ReadDateTime(reader, "CreatedAtUtc")
        };

    private static async Task EnsureColumnAsync(
        SqliteConnection connection,
        string columnName,
        string columnDefinition,
        CancellationToken cancellationToken)
    {
        await using (var check = connection.CreateCommand())
        {
            check.CommandText = $"PRAGMA table_info({TableName});";
            await using var reader = await check.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                if (string.Equals(reader.GetString(reader.GetOrdinal("name")), columnName, StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }
            }
        }

        await using var alter = connection.CreateCommand();
        alter.CommandText = $"ALTER TABLE {TableName} ADD COLUMN {columnName} {columnDefinition};";
        await alter.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static string ReadString(SqliteDataReader reader, string name)
    {
        var ordinal = reader.GetOrdinal(name);
        return reader.IsDBNull(ordinal) ? string.Empty : reader.GetString(ordinal);
    }

    private static DateTime ReadDateTime(SqliteDataReader reader, string name)
    {
        var value = reader.GetString(reader.GetOrdinal(name));
        return DateTime.TryParse(
            value,
            CultureInfo.InvariantCulture,
            DateTimeStyles.RoundtripKind,
            out var parsed)
            ? parsed
            : default;
    }

    private static decimal ReadDecimal(SqliteDataReader reader, string name)
        => Convert.ToDecimal(reader.GetDouble(reader.GetOrdinal(name)), CultureInfo.InvariantCulture);

    private static decimal? ReadNullableDecimal(SqliteDataReader reader, string name)
    {
        var ordinal = reader.GetOrdinal(name);
        return reader.IsDBNull(ordinal)
            ? null
            : Convert.ToDecimal(reader.GetDouble(ordinal), CultureInfo.InvariantCulture);
    }

    private static object ToSqliteValue(decimal? value)
        => value.HasValue
            ? Convert.ToDouble(value.Value, CultureInfo.InvariantCulture)
            : DBNull.Value;

    private static void AddParameter(SqliteCommand command, string name, object? value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value ?? DBNull.Value;
        command.Parameters.Add(parameter);
    }

    private static bool IsBusyOrLocked(SqliteException ex)
        => ex.SqliteErrorCode is 5 or 6;
}
