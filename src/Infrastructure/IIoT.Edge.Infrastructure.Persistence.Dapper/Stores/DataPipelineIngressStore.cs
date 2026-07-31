using Dapper;
using IIoT.Edge.Application.Common.DataPipeline;
using IIoT.Edge.Infrastructure.Persistence.Dapper.Connection;
using IIoT.Edge.Infrastructure.Persistence.Dapper.Repository;
using IIoT.Edge.Module.Contracts.DataPipeline;
using IIoT.Edge.Module.Contracts.DataPipeline.CellData;
using IIoT.Edge.Module.Contracts.Logging;
using System.Data;

namespace IIoT.Edge.Infrastructure.Persistence.Dapper.Stores;

/// <summary>
/// 主队列的不可变完工信封和 per-consumer 完成回执。
/// completed 表只保留稳定 ID 墓碑，防止同一完工事实在清理信封后再次进入主链。
/// </summary>
public sealed class DataPipelineIngressStore
    : DapperRepositoryBase<DataPipelineIngressStore.IngressRow>, IDataPipelineIngressStore
{
    private const string ConsumerTableName = "durable_ingress_consumers";
    private const string CompletedTableName = "durable_ingress_completed";
    private readonly ICellDataJsonSerializer _cellDataJsonSerializer;

    public override string DbName => "pipeline_ingress";

    protected override string TableName => "durable_ingress_records";

    protected override string CreateTableSql => $"""
        CREATE TABLE IF NOT EXISTS {TableName} (
            CompletionId          TEXT    PRIMARY KEY,
            ProcessType           TEXT    NOT NULL,
            CellDataJson          TEXT    NOT NULL,
            PlcCode               TEXT    NOT NULL,
            IdempotencyKeyVersion INTEGER NOT NULL,
            NetworkDeviceId       INTEGER NULL,
            DeviceName            TEXT    NOT NULL,
            ModuleId              TEXT    NOT NULL,
            TaskKey               TEXT    NOT NULL,
            PlanSessionId         TEXT    NOT NULL,
            MainPlanCode          TEXT    NOT NULL,
            TraceBatchNumber      TEXT    NOT NULL,
            CreatedAtUtc          TEXT    NOT NULL,
            AcceptedAtUtc         TEXT    NOT NULL
        );

        CREATE INDEX IF NOT EXISTS idx_durable_ingress_accepted
            ON {TableName} (AcceptedAtUtc, CompletionId);

        CREATE TABLE IF NOT EXISTS {ConsumerTableName} (
            CompletionId   TEXT NOT NULL,
            ConsumerKey    TEXT NOT NULL,
            CompletedAtUtc TEXT NOT NULL,
            PRIMARY KEY (CompletionId, ConsumerKey),
            FOREIGN KEY (CompletionId) REFERENCES {TableName}(CompletionId) ON DELETE CASCADE
        );

        CREATE INDEX IF NOT EXISTS idx_durable_ingress_consumer
            ON {ConsumerTableName} (ConsumerKey, CompletedAtUtc);

        CREATE TABLE IF NOT EXISTS {CompletedTableName} (
            CompletionId  TEXT PRIMARY KEY,
            CompletedAtUtc TEXT NOT NULL
        );
        """;

    public DataPipelineIngressStore(
        SqliteConnectionFactory connectionFactory,
        ILogService logger,
        ICellDataJsonSerializer cellDataJsonSerializer)
        : base(connectionFactory, logger)
    {
        _cellDataJsonSerializer = cellDataJsonSerializer;
    }

    public Task<DataPipelineIngressAcceptance> AcceptAsync(
        CellCompletedRecord record,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(record);
        ArgumentNullException.ThrowIfNull(record.CellData);
        cancellationToken.ThrowIfCancellationRequested();

        var completionId = DataPipelineCompletionIdentity.Create(record);
        var cellDataJson = _cellDataJsonSerializer.Serialize(record.CellData);
        var createdAtUtc = NormalizeCreatedAt(record.CreatedAtUtc);
        var acceptedAtUtc = DateTime.UtcNow;

        return ExecuteInTransactionAsync(async (connection, transaction) =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            var alreadyCompleted = await connection.ExecuteScalarAsync<long>(new CommandDefinition(
                $"SELECT COUNT(*) FROM {CompletedTableName} WHERE CompletionId = @CompletionId",
                new { CompletionId = completionId },
                transaction,
                commandTimeout: CommandTimeout,
                cancellationToken: cancellationToken)).ConfigureAwait(false) > 0;

            if (alreadyCompleted)
            {
                return new DataPipelineIngressAcceptance(completionId, record, AlreadyCompleted: true);
            }

            await connection.ExecuteAsync(new CommandDefinition(
                $"""
                 INSERT OR IGNORE INTO {TableName}
                    (CompletionId, ProcessType, CellDataJson,
                     PlcCode, IdempotencyKeyVersion, NetworkDeviceId, DeviceName,
                     ModuleId, TaskKey, PlanSessionId, MainPlanCode, TraceBatchNumber,
                     CreatedAtUtc, AcceptedAtUtc)
                 VALUES
                    (@CompletionId, @ProcessType, @CellDataJson,
                     @PlcCode, @IdempotencyKeyVersion, @NetworkDeviceId, @DeviceName,
                     @ModuleId, @TaskKey, @PlanSessionId, @MainPlanCode, @TraceBatchNumber,
                     @CreatedAtUtc, @AcceptedAtUtc)
                 """,
                new
                {
                    CompletionId = completionId,
                    ProcessType = record.CellData.ProcessType,
                    CellDataJson = cellDataJson,
                    PlcCode = record.ResolvePlcCode(),
                    IdempotencyKeyVersion = (int)record.IdempotencyKeyVersion,
                    NetworkDeviceId = record.ResolveNetworkDeviceId(),
                    DeviceName = record.ResolveDeviceName(),
                    record.ModuleId,
                    record.TaskKey,
                    record.PlanSessionId,
                    record.MainPlanCode,
                    record.TraceBatchNumber,
                    CreatedAtUtc = createdAtUtc.ToString("O"),
                    AcceptedAtUtc = acceptedAtUtc.ToString("O")
                },
                transaction,
                commandTimeout: CommandTimeout,
                cancellationToken: cancellationToken)).ConfigureAwait(false);

            var persisted = await GetRowAsync(connection, transaction, completionId, cancellationToken)
                .ConfigureAwait(false)
                ?? throw new InvalidOperationException("完整入口信封未能可靠落盘。");

            return new DataPipelineIngressAcceptance(
                completionId,
                CreateRecord(persisted),
                AlreadyCompleted: false);
        });
    }

    public Task<DataPipelineIngressRecord?> GetAsync(
        string completionId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(completionId);
        return ExecuteReadEnvelopeAsync(completionId, cancellationToken);
    }

    public async Task<IReadOnlyList<DataPipelineIngressRecord>> GetPendingAsync(
        int batchSize,
        CancellationToken cancellationToken = default)
    {
        var size = Math.Clamp(batchSize, 1, 500);
        using var connection = GetConnection();
        cancellationToken.ThrowIfCancellationRequested();
        var rows = (await connection.QueryAsync<IngressRow>(new CommandDefinition(
            $"""
             SELECT *
             FROM {TableName}
             ORDER BY AcceptedAtUtc ASC, CompletionId ASC
             LIMIT @BatchSize
             """,
            new { BatchSize = size },
            commandTimeout: CommandTimeout,
            cancellationToken: cancellationToken)).ConfigureAwait(false)).ToList();

        if (rows.Count == 0)
        {
            return [];
        }

        var ids = rows.Select(static row => row.CompletionId).ToArray();
        var completions = (await connection.QueryAsync<ConsumerCompletionRow>(new CommandDefinition(
            $"""
             SELECT CompletionId, ConsumerKey
             FROM {ConsumerTableName}
             WHERE CompletionId IN @CompletionIds
             """,
            new { CompletionIds = ids },
            commandTimeout: CommandTimeout,
            cancellationToken: cancellationToken)).ConfigureAwait(false))
            .GroupBy(static row => row.CompletionId, StringComparer.Ordinal)
            .ToDictionary(
                static group => group.Key,
                static group => (IReadOnlySet<string>)group
                    .Select(static row => row.ConsumerKey)
                    .ToHashSet(StringComparer.OrdinalIgnoreCase),
                StringComparer.Ordinal);

        return rows
            .Select(row => new DataPipelineIngressRecord(
                row.CompletionId,
                CreateRecord(row),
                completions.GetValueOrDefault(row.CompletionId)
                    ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase)))
            .ToArray();
    }

    public Task MarkConsumerCompletedAsync(
        string completionId,
        string consumerKey,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(completionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(consumerKey);
        var normalizedKey = consumerKey.Trim().ToUpperInvariant();

        return ExecuteInTransactionAsync<int>(async (connection, transaction) =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            var exists = await connection.ExecuteScalarAsync<long>(new CommandDefinition(
                $"SELECT COUNT(*) FROM {TableName} WHERE CompletionId = @CompletionId",
                new { CompletionId = completionId },
                transaction,
                commandTimeout: CommandTimeout,
                cancellationToken: cancellationToken)).ConfigureAwait(false) > 0;

            if (!exists)
            {
                return 0;
            }

            return await connection.ExecuteAsync(new CommandDefinition(
                $"""
                 INSERT OR IGNORE INTO {ConsumerTableName}
                    (CompletionId, ConsumerKey, CompletedAtUtc)
                 VALUES
                    (@CompletionId, @ConsumerKey, @CompletedAtUtc)
                 """,
                new
                {
                    CompletionId = completionId,
                    ConsumerKey = normalizedKey,
                    CompletedAtUtc = DateTime.UtcNow.ToString("O")
                },
                transaction,
                commandTimeout: CommandTimeout,
                cancellationToken: cancellationToken)).ConfigureAwait(false);
        });
    }

    public Task<bool> CompleteIfAllConsumersFinishedAsync(
        string completionId,
        IReadOnlyCollection<string> requiredConsumerKeys,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(completionId);
        ArgumentNullException.ThrowIfNull(requiredConsumerKeys);
        var required = requiredConsumerKeys
            .Where(static key => !string.IsNullOrWhiteSpace(key))
            .Select(static key => key.Trim().ToUpperInvariant())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return ExecuteInTransactionAsync(async (connection, transaction) =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            var exists = await connection.ExecuteScalarAsync<long>(new CommandDefinition(
                $"SELECT COUNT(*) FROM {TableName} WHERE CompletionId = @CompletionId",
                new { CompletionId = completionId },
                transaction,
                commandTimeout: CommandTimeout,
                cancellationToken: cancellationToken)).ConfigureAwait(false) > 0;
            if (!exists)
            {
                return false;
            }

            var completedCount = required.Length == 0
                ? 0
                : await connection.ExecuteScalarAsync<int>(new CommandDefinition(
                    $"""
                     SELECT COUNT(DISTINCT ConsumerKey)
                     FROM {ConsumerTableName}
                     WHERE CompletionId = @CompletionId
                       AND ConsumerKey IN @RequiredKeys
                     """,
                    new { CompletionId = completionId, RequiredKeys = required },
                    transaction,
                    commandTimeout: CommandTimeout,
                    cancellationToken: cancellationToken)).ConfigureAwait(false);

            if (completedCount != required.Length)
            {
                return false;
            }

            await connection.ExecuteAsync(new CommandDefinition(
                $"""
                 INSERT OR IGNORE INTO {CompletedTableName} (CompletionId, CompletedAtUtc)
                 VALUES (@CompletionId, @CompletedAtUtc)
                 """,
                new
                {
                    CompletionId = completionId,
                    CompletedAtUtc = DateTime.UtcNow.ToString("O")
                },
                transaction,
                commandTimeout: CommandTimeout,
                cancellationToken: cancellationToken)).ConfigureAwait(false);

            var deleted = await connection.ExecuteAsync(new CommandDefinition(
                $"DELETE FROM {TableName} WHERE CompletionId = @CompletionId",
                new { CompletionId = completionId },
                transaction,
                commandTimeout: CommandTimeout,
                cancellationToken: cancellationToken)).ConfigureAwait(false);
            return deleted == 1;
        });
    }

    private async Task<DataPipelineIngressRecord?> ExecuteReadEnvelopeAsync(
        string completionId,
        CancellationToken cancellationToken)
    {
        using var connection = GetConnection();
        var row = await connection.QuerySingleOrDefaultAsync<IngressRow>(new CommandDefinition(
            $"SELECT * FROM {TableName} WHERE CompletionId = @CompletionId",
            new { CompletionId = completionId },
            commandTimeout: CommandTimeout,
            cancellationToken: cancellationToken)).ConfigureAwait(false);
        if (row is null)
        {
            return null;
        }

        var completed = (await connection.QueryAsync<string>(new CommandDefinition(
            $"SELECT ConsumerKey FROM {ConsumerTableName} WHERE CompletionId = @CompletionId",
            new { CompletionId = completionId },
            commandTimeout: CommandTimeout,
            cancellationToken: cancellationToken)).ConfigureAwait(false))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        return new DataPipelineIngressRecord(row.CompletionId, CreateRecord(row), completed);
    }

    private static Task<IngressRow?> GetRowAsync(
        IDbConnection connection,
        IDbTransaction transaction,
        string completionId,
        CancellationToken cancellationToken)
        => connection.QuerySingleOrDefaultAsync<IngressRow>(new CommandDefinition(
            "SELECT * FROM durable_ingress_records WHERE CompletionId = @CompletionId",
            new { CompletionId = completionId },
            transaction,
            commandTimeout: 30,
            cancellationToken: cancellationToken));

    private CellCompletedRecord CreateRecord(IngressRow row)
    {
        var cellData = _cellDataJsonSerializer.Deserialize(row.ProcessType, row.CellDataJson)
            ?? throw new InvalidOperationException(
                $"入口记录 {row.CompletionId} 的 CellData 无法反序列化。");
        return new CellCompletedRecord
        {
            CellData = cellData,
            PlcCode = row.PlcCode,
            IdempotencyKeyVersion = (CloudIdempotencyKeyVersion)row.IdempotencyKeyVersion,
            NetworkDeviceId = row.NetworkDeviceId,
            DeviceName = row.DeviceName,
            ModuleId = row.ModuleId,
            TaskKey = row.TaskKey,
            PlanSessionId = row.PlanSessionId,
            MainPlanCode = row.MainPlanCode,
            TraceBatchNumber = row.TraceBatchNumber,
            CreatedAtUtc = ParseUtc(row.CreatedAtUtc)
        };
    }

    private static DateTime NormalizeCreatedAt(DateTime value)
        => value == default
            ? DateTime.UtcNow
            : value.Kind switch
            {
                DateTimeKind.Utc => value,
                DateTimeKind.Local => value.ToUniversalTime(),
                _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
            };

    private static DateTime ParseUtc(string value)
    {
        var parsed = DateTime.Parse(
            value,
            System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.RoundtripKind);
        return parsed.Kind == DateTimeKind.Utc ? parsed : parsed.ToUniversalTime();
    }

    public sealed class IngressRow
    {
        public string CompletionId { get; init; } = string.Empty;
        public string ProcessType { get; init; } = string.Empty;
        public string CellDataJson { get; init; } = string.Empty;
        public string PlcCode { get; init; } = string.Empty;
        public int IdempotencyKeyVersion { get; init; }
        public int? NetworkDeviceId { get; init; }
        public string DeviceName { get; init; } = string.Empty;
        public string ModuleId { get; init; } = string.Empty;
        public string TaskKey { get; init; } = string.Empty;
        public string PlanSessionId { get; init; } = string.Empty;
        public string MainPlanCode { get; init; } = string.Empty;
        public string TraceBatchNumber { get; init; } = string.Empty;
        public string CreatedAtUtc { get; init; } = string.Empty;
        public string AcceptedAtUtc { get; init; } = string.Empty;
    }

    private sealed class ConsumerCompletionRow
    {
        public string CompletionId { get; init; } = string.Empty;
        public string ConsumerKey { get; init; } = string.Empty;
    }
}
