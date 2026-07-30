using System.Collections.Concurrent;
using System.Globalization;
using System.Text.RegularExpressions;
using Dapper;
using IIoT.Edge.Domain.Hardware.Aggregates;
using IIoT.Edge.Infrastructure.Persistence.Dapper.Connection;
using IIoT.Edge.Module.Contracts.Hardware;
using IIoT.Edge.Module.Contracts.Logging;
using IIoT.Edge.Module.Contracts.Production;
using IIoT.Edge.SharedKernel.Repository;

namespace IIoT.Edge.Infrastructure.Persistence.Dapper.Stores;

public sealed partial class ModuleProductionRecordPersistence(
    SqliteConnectionFactory connectionFactory,
    ILogService logger,
    IReadRepository<NetworkDeviceEntity>? networkDevices = null)
    : IModuleProductionRecordPersistence
{
    private const string AllDevicesKey = "__all__";
    private const int MaximumRows = 500;
    private const int CommandTimeoutSeconds = 30;
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _initializationGates =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, byte> _initializedModules =
        new(StringComparer.OrdinalIgnoreCase);

    public async Task<bool> AddAsync(
        ModuleProductionRecordEntry entry,
        CancellationToken cancellationToken = default)
    {
        Validate(entry);
        var moduleId = NormalizeModuleId(entry.ModuleId);
        await EnsureInitializedAsync(moduleId, cancellationToken).ConfigureAwait(false);

        try
        {
            using var connection = connectionFactory.Create(BuildDatabaseName(moduleId));
            var command = new CommandDefinition(
                """
                INSERT OR IGNORE INTO module_production_records
                (
                    IdempotencyKey,
                    ModuleId,
                    PlcCode,
                    DeviceCode,
                    DeviceName,
                    TaskKey,
                    SlotKey,
                    RecordCode,
                    MainPlanCode,
                    TraceBatchNumber,
                    Quantity,
                    Speed,
                    StartedAtUtc,
                    CompletedAtUtc,
                    QueueCreatedAtUtc,
                    QueueProcessedAtUtc,
                    IsOk
                )
                VALUES
                (
                    @IdempotencyKey,
                    @ModuleId,
                    @PlcCode,
                    @DeviceCode,
                    @DeviceName,
                    @TaskKey,
                    @SlotKey,
                    @RecordCode,
                    @MainPlanCode,
                    @TraceBatchNumber,
                    @Quantity,
                    @Speed,
                    @StartedAtUtc,
                    @CompletedAtUtc,
                    @QueueCreatedAtUtc,
                    @QueueProcessedAtUtc,
                    @IsOk
                );
                """,
                new
                {
                    entry.IdempotencyKey,
                    ModuleId = moduleId,
                    PlcCode = entry.ResolvePlcCode(),
                    entry.DeviceCode,
                    entry.DeviceName,
                    entry.TaskKey,
                    entry.SlotKey,
                    entry.RecordCode,
                    entry.MainPlanCode,
                    entry.TraceBatchNumber,
                    entry.Quantity,
                    entry.Speed,
                    StartedAtUtc = FormatUtc(entry.StartedAtUtc),
                    CompletedAtUtc = FormatUtc(entry.CompletedAtUtc),
                    QueueCreatedAtUtc = FormatUtc(entry.QueueCreatedAtUtc),
                    QueueProcessedAtUtc = FormatUtc(entry.QueueProcessedAtUtc),
                    IsOk = entry.IsOk ? 1 : 0
                },
                commandTimeout: CommandTimeoutSeconds,
                cancellationToken: cancellationToken);
            return await connection.ExecuteAsync(command).ConfigureAwait(false) == 1;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.Error($"[{moduleId}生产记录持久化] 写入失败：{ex.Message}");
            throw;
        }
    }

    public async Task<IReadOnlyList<ModuleProductionRecordEntry>> QueryAsync(
        ModuleProductionRecordQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        var moduleId = NormalizeModuleId(query.ModuleId);
        var rangeStartUtc = EnsureUtc(query.RangeStartUtc);
        var rangeEndUtc = EnsureUtc(query.RangeEndUtc);
        ValidateRange(rangeStartUtc, rangeEndUtc);
        await EnsureInitializedAsync(moduleId, cancellationToken).ConfigureAwait(false);
        var selection = await ResolveSelectionAsync(
            query.SelectedDeviceKey,
            cancellationToken).ConfigureAwait(false);
        if (!selection.IsResolved)
        {
            return [];
        }

        try
        {
            using var connection = connectionFactory.Create(BuildDatabaseName(moduleId));
            var command = new CommandDefinition(
                """
                SELECT
                    Id,
                    IdempotencyKey,
                    ModuleId,
                    PlcCode,
                    DeviceCode,
                    DeviceName,
                    TaskKey,
                    SlotKey,
                    RecordCode,
                    MainPlanCode,
                    TraceBatchNumber,
                    Quantity,
                    Speed,
                    StartedAtUtc,
                    CompletedAtUtc,
                    QueueCreatedAtUtc,
                    QueueProcessedAtUtc,
                    IsOk
                FROM module_production_records
                WHERE ModuleId = @ModuleId
                  AND CompletedAtUtc >= @RangeStartUtc
                  AND CompletedAtUtc < @RangeEndUtc
                  AND (
                      @SelectedDeviceKey = @AllDevicesKey
                      OR PlcCode = @SelectedDeviceKey COLLATE NOCASE
                  )
                ORDER BY CompletedAtUtc DESC, Id DESC
                LIMIT @Limit;
                """,
                new
                {
                    ModuleId = moduleId,
                    RangeStartUtc = FormatUtc(rangeStartUtc),
                    RangeEndUtc = FormatUtc(rangeEndUtc),
                    SelectedDeviceKey = selection.Key,
                    AllDevicesKey,
                    Limit = Math.Clamp(query.Limit, 1, MaximumRows)
                },
                commandTimeout: CommandTimeoutSeconds,
                cancellationToken: cancellationToken);
            var rows = await connection.QueryAsync<PersistenceRow>(command).ConfigureAwait(false);
            return rows.Select(ToEntry).ToArray();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.Error($"[{moduleId}生产记录持久化] 查询失败：{ex.Message}");
            throw;
        }
    }

    public async Task<ModuleProductionRecordSummary> QuerySummaryAsync(
        ModuleProductionRecordSummaryPersistenceQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        var moduleId = NormalizeModuleId(query.ModuleId);
        var rangeStartUtc = EnsureUtc(query.RangeStartUtc);
        var rangeEndUtc = EnsureUtc(query.RangeEndUtc);
        var recentWindowStartUtc = EnsureUtc(query.RecentWindowStartUtc);
        ValidateRange(rangeStartUtc, rangeEndUtc);
        await EnsureInitializedAsync(moduleId, cancellationToken).ConfigureAwait(false);
        var selection = await ResolveSelectionAsync(
            query.SelectedDeviceKey,
            cancellationToken).ConfigureAwait(false);
        if (!selection.IsResolved)
        {
            return new ModuleProductionRecordSummary(0, 0, 0, 0, 0, 0, string.Empty);
        }

        try
        {
            using var connection = connectionFactory.Create(BuildDatabaseName(moduleId));
            var parameters = new
            {
                ModuleId = moduleId,
                RangeStartUtc = FormatUtc(rangeStartUtc),
                RangeEndUtc = FormatUtc(rangeEndUtc),
                RecentWindowStartUtc = FormatUtc(recentWindowStartUtc),
                SelectedDeviceKey = selection.Key,
                AllDevicesKey
            };
            var summaryCommand = new CommandDefinition(
                """
                SELECT
                    COALESCE(SUM(CASE WHEN IsOk = 1 THEN Quantity ELSE 0 END), 0) AS TodayOk,
                    COALESCE(SUM(CASE WHEN IsOk = 0 THEN Quantity ELSE 0 END), 0) AS TodayNg,
                    COALESCE(SUM(CASE WHEN CompletedAtUtc >= @RecentWindowStartUtc AND IsOk = 1 THEN Quantity ELSE 0 END), 0) AS RecentOk,
                    COALESCE(SUM(CASE WHEN CompletedAtUtc >= @RecentWindowStartUtc AND IsOk = 0 THEN Quantity ELSE 0 END), 0) AS RecentNg
                FROM module_production_records
                WHERE ModuleId = @ModuleId
                  AND CompletedAtUtc >= @RangeStartUtc
                  AND CompletedAtUtc < @RangeEndUtc
                  AND (
                      @SelectedDeviceKey = @AllDevicesKey
                      OR PlcCode = @SelectedDeviceKey COLLATE NOCASE
                  );
                """,
                parameters,
                commandTimeout: CommandTimeoutSeconds,
                cancellationToken: cancellationToken);
            var totals = await connection.QuerySingleAsync<SummaryRow>(summaryCommand).ConfigureAwait(false);
            var batchCommand = new CommandDefinition(
                """
                SELECT
                    CASE
                        WHEN TRIM(MainPlanCode) <> '' THEN MainPlanCode
                        WHEN TRIM(TraceBatchNumber) <> '' THEN TraceBatchNumber
                        ELSE RecordCode
                    END
                FROM module_production_records
                WHERE ModuleId = @ModuleId
                  AND CompletedAtUtc >= @RangeStartUtc
                  AND CompletedAtUtc < @RangeEndUtc
                  AND (
                      @SelectedDeviceKey = @AllDevicesKey
                      OR PlcCode = @SelectedDeviceKey COLLATE NOCASE
                  )
                ORDER BY CompletedAtUtc DESC, Id DESC
                LIMIT 1;
                """,
                parameters,
                commandTimeout: CommandTimeoutSeconds,
                cancellationToken: cancellationToken);
            var currentBatch = await connection.ExecuteScalarAsync<string?>(batchCommand).ConfigureAwait(false)
                               ?? string.Empty;

            return new ModuleProductionRecordSummary(
                totals.TodayOk + totals.TodayNg,
                totals.TodayOk,
                totals.TodayNg,
                totals.RecentOk + totals.RecentNg,
                totals.RecentOk,
                totals.RecentNg,
                currentBatch);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.Error($"[{moduleId}生产记录持久化] 汇总查询失败：{ex.Message}");
            throw;
        }
    }

    private async Task EnsureInitializedAsync(
        string moduleId,
        CancellationToken cancellationToken)
    {
        if (_initializedModules.ContainsKey(moduleId))
        {
            return;
        }

        var gate = _initializationGates.GetOrAdd(moduleId, static _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_initializedModules.ContainsKey(moduleId))
            {
                return;
            }

            using var connection = connectionFactory.Create(BuildDatabaseName(moduleId));
            var command = new CommandDefinition(
                """
                CREATE TABLE IF NOT EXISTS module_production_records
                (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    IdempotencyKey TEXT NOT NULL UNIQUE,
                    ModuleId TEXT NOT NULL,
                    PlcCode TEXT NOT NULL,
                    DeviceCode TEXT NOT NULL,
                    DeviceName TEXT NOT NULL,
                    TaskKey TEXT NOT NULL,
                    SlotKey TEXT NOT NULL,
                    RecordCode TEXT NOT NULL,
                    MainPlanCode TEXT NOT NULL,
                    TraceBatchNumber TEXT NOT NULL,
                    Quantity INTEGER NOT NULL,
                    Speed REAL NOT NULL,
                    StartedAtUtc TEXT NOT NULL,
                    CompletedAtUtc TEXT NOT NULL,
                    QueueCreatedAtUtc TEXT NOT NULL,
                    QueueProcessedAtUtc TEXT NOT NULL,
                    IsOk INTEGER NOT NULL
                );
                CREATE INDEX IF NOT EXISTS ix_module_production_records_completed
                    ON module_production_records (CompletedAtUtc DESC);
                CREATE INDEX IF NOT EXISTS ix_module_production_records_device_completed
                    ON module_production_records (DeviceName, CompletedAtUtc DESC);
                """,
                commandTimeout: CommandTimeoutSeconds,
                cancellationToken: cancellationToken);
            await connection.ExecuteAsync(command).ConfigureAwait(false);
            var columns = await connection.QueryAsync<TableColumnInfo>(
                "PRAGMA table_info('module_production_records');").ConfigureAwait(false);
            if (!columns.Any(static column =>
                    string.Equals(column.Name, "PlcCode", StringComparison.OrdinalIgnoreCase)))
            {
                await connection.ExecuteAsync(new CommandDefinition(
                    "ALTER TABLE module_production_records ADD COLUMN PlcCode TEXT NOT NULL DEFAULT '';",
                    commandTimeout: CommandTimeoutSeconds,
                    cancellationToken: cancellationToken)).ConfigureAwait(false);
            }

            await connection.ExecuteAsync(new CommandDefinition(
                """
                UPDATE module_production_records
                SET PlcCode = DeviceCode
                WHERE TRIM(PlcCode) = ''
                  AND TRIM(DeviceCode) <> '';
                CREATE INDEX IF NOT EXISTS ix_module_production_records_plc_completed
                    ON module_production_records (PlcCode, CompletedAtUtc DESC);
                """,
                commandTimeout: CommandTimeoutSeconds,
                cancellationToken: cancellationToken)).ConfigureAwait(false);
            _initializedModules.TryAdd(moduleId, 0);
        }
        finally
        {
            gate.Release();
        }
    }

    private static ModuleProductionRecordEntry ToEntry(PersistenceRow row)
        => new(
            row.Id,
            row.IdempotencyKey,
            row.ModuleId,
            row.DeviceCode,
            row.DeviceName,
            row.TaskKey,
            row.SlotKey,
            row.RecordCode,
            row.MainPlanCode,
            row.TraceBatchNumber,
            row.Quantity,
            row.Speed,
            ParseUtc(row.StartedAtUtc),
            ParseUtc(row.CompletedAtUtc),
            ParseUtc(row.QueueCreatedAtUtc),
            ParseUtc(row.QueueProcessedAtUtc),
            row.IsOk == 1)
        {
            PlcCode = row.PlcCode
        };

    private static void Validate(ModuleProductionRecordEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        _ = NormalizeModuleId(entry.ModuleId);
        if (string.IsNullOrWhiteSpace(entry.IdempotencyKey)
            || string.IsNullOrWhiteSpace(entry.ResolvePlcCode())
            || string.IsNullOrWhiteSpace(entry.DeviceCode)
            || string.IsNullOrWhiteSpace(entry.DeviceName)
            || string.IsNullOrWhiteSpace(entry.TaskKey)
            || string.IsNullOrWhiteSpace(entry.SlotKey)
            || string.IsNullOrWhiteSpace(entry.RecordCode))
        {
            throw new ArgumentException("模块生产记录缺少必填字段。", nameof(entry));
        }

        if (entry.Quantity < 0 || EnsureUtc(entry.CompletedAtUtc) < EnsureUtc(entry.StartedAtUtc))
        {
            throw new ArgumentException("模块生产记录的数量或时间范围无效。", nameof(entry));
        }
    }

    private static void ValidateRange(DateTime rangeStartUtc, DateTime rangeEndUtc)
    {
        if (rangeEndUtc <= rangeStartUtc)
        {
            throw new ArgumentException("生产记录查询结束时间必须晚于开始时间。");
        }
    }

    private static string NormalizeModuleId(string moduleId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(moduleId);
        var normalized = moduleId.Trim();
        if (!SafeModuleIdRegex().IsMatch(normalized))
        {
            throw new ArgumentException("模块标识只能包含字母、数字、下划线或短横线。", nameof(moduleId));
        }

        return normalized.ToUpperInvariant();
    }

    private static string BuildDatabaseName(string moduleId)
        => $"{moduleId.ToLowerInvariant()}_production";

    private static string NormalizeSelection(string? selectedDeviceKey)
        => string.IsNullOrWhiteSpace(selectedDeviceKey)
            ? AllDevicesKey
            : selectedDeviceKey.Trim();

    private async Task<SelectionResolution> ResolveSelectionAsync(
        string? selectedDeviceKey,
        CancellationToken cancellationToken)
    {
        var normalized = NormalizeSelection(selectedDeviceKey);
        if (string.Equals(normalized, AllDevicesKey, StringComparison.Ordinal)
            || networkDevices is null)
        {
            return SelectionResolution.Success(normalized);
        }

        var devices = await networkDevices.GetListAsync(
            static device => device.DeviceType == DeviceType.PLC,
            cancellationToken).ConfigureAwait(false);
        var codeMatches = devices
            .Where(device => string.Equals(
                device.PlcCode,
                normalized,
                StringComparison.OrdinalIgnoreCase))
            .Take(2)
            .ToArray();
        if (codeMatches.Length > 0)
        {
            return codeMatches.Length == 1
                ? SelectionResolution.Success(codeMatches[0].PlcCode.Trim())
                : SelectionResolution.Blocked(normalized);
        }

        var nameMatches = devices
            .Where(device => string.Equals(
                device.DeviceName,
                normalized,
                StringComparison.OrdinalIgnoreCase))
            .Take(2)
            .ToArray();
        return nameMatches.Length == 1 && !string.IsNullOrWhiteSpace(nameMatches[0].PlcCode)
            ? SelectionResolution.Success(nameMatches[0].PlcCode.Trim())
            : SelectionResolution.Blocked(normalized);
    }

    private static string FormatUtc(DateTime value)
        => EnsureUtc(value).ToString("O", CultureInfo.InvariantCulture);

    private static DateTime ParseUtc(string value)
        => DateTime.Parse(
            value,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal);

    private static DateTime EnsureUtc(DateTime value)
        => value.Kind switch
        {
            DateTimeKind.Utc => value,
            DateTimeKind.Local => value.ToUniversalTime(),
            _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
        };

    [GeneratedRegex("^[A-Za-z0-9_-]+$", RegexOptions.CultureInvariant)]
    private static partial Regex SafeModuleIdRegex();

    private sealed class PersistenceRow
    {
        public long Id { get; init; }
        public string IdempotencyKey { get; init; } = string.Empty;
        public string ModuleId { get; init; } = string.Empty;

        public string PlcCode { get; init; } = string.Empty;
        public string DeviceCode { get; init; } = string.Empty;
        public string DeviceName { get; init; } = string.Empty;
        public string TaskKey { get; init; } = string.Empty;
        public string SlotKey { get; init; } = string.Empty;
        public string RecordCode { get; init; } = string.Empty;
        public string MainPlanCode { get; init; } = string.Empty;
        public string TraceBatchNumber { get; init; } = string.Empty;
        public int Quantity { get; init; }
        public decimal Speed { get; init; }
        public string StartedAtUtc { get; init; } = string.Empty;
        public string CompletedAtUtc { get; init; } = string.Empty;
        public string QueueCreatedAtUtc { get; init; } = string.Empty;
        public string QueueProcessedAtUtc { get; init; } = string.Empty;
        public int IsOk { get; init; }
    }

    private sealed record SelectionResolution(string Key, bool IsResolved)
    {
        public static SelectionResolution Success(string key) => new(key, true);

        public static SelectionResolution Blocked(string key) => new(key, false);
    }

    private sealed class TableColumnInfo
    {
        public string Name { get; init; } = string.Empty;
    }

    private sealed class SummaryRow
    {
        public long TodayOk { get; init; }
        public long TodayNg { get; init; }
        public long RecentOk { get; init; }
        public long RecentNg { get; init; }
    }
}
