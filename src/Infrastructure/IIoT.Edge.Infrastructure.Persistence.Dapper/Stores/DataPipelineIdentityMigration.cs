using System.Data;
using System.Text.Json;
using Dapper;
using IIoT.Edge.Application.Common.DataPipeline;
using IIoT.Edge.Domain.Hardware.Aggregates;
using IIoT.Edge.Infrastructure.Persistence.Dapper.Connection;
using IIoT.Edge.Module.Contracts.Hardware;
using IIoT.Edge.Module.Contracts.Logging;
using IIoT.Edge.SharedKernel.Repository;

namespace IIoT.Edge.Infrastructure.Persistence.Dapper.Stores;

public sealed class DataPipelineIdentityMigration(
    SqliteConnectionFactory connectionFactory,
    IReadRepository<NetworkDeviceEntity> networkDevices,
    ILogService logger)
    : IDataPipelineIdentityMigration
{
    private static readonly MigrationTarget[] Targets =
    [
        new("pipeline_cloud", "failed_cloud_records"),
        new("pipeline_cloud", "cloud_fallback_records"),
        new("pipeline_cloud", "dead_cloud_records"),
        new("pipeline_mes", "failed_mes_records"),
        new("pipeline_mes", "mes_fallback_records"),
        new("pipeline_mes", "dead_mes_records")
    ];

    public async Task<DataPipelineIdentityMigrationResult> MigrateAsync(
        CancellationToken cancellationToken = default)
    {
        var devices = await networkDevices.GetListAsync(
            static device => device.DeviceType == DeviceType.PLC,
            cancellationToken).ConfigureAwait(false);
        var deviceById = devices
            .Where(static device => device.Id > 0)
            .ToDictionary(static device => device.Id);
        var migratedCount = 0;
        var issues = new List<DataPipelineIdentityMigrationIssue>();

        foreach (var target in Targets)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                using var connection = connectionFactory.Create(target.DatabaseName);
                var rows = (await connection.QueryAsync<LegacyIdentityRow>(
                    $"""
                     SELECT
                         Id,
                         CellDataJson,
                         PlcCode,
                         IdempotencyKeyVersion,
                         NetworkDeviceId,
                         DeviceName,
                         TaskKey
                     FROM {target.TableName}
                     WHERE TRIM(PlcCode) = ''
                        OR IdempotencyKeyVersion NOT IN (1, 2)
                     ORDER BY Id ASC
                     """)).ToArray();

                foreach (var row in rows)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var resolution = Resolve(row, deviceById);
                    if (!resolution.IsSuccess)
                    {
                        issues.Add(CreateIssue(target, row, resolution));
                        continue;
                    }

                    var affected = await connection.ExecuteAsync(new CommandDefinition(
                        $"""
                         UPDATE {target.TableName}
                         SET PlcCode = @PlcCode,
                             IdempotencyKeyVersion = 1
                         WHERE Id = @Id
                           AND TRIM(PlcCode) = ''
                         """,
                        new
                        {
                            row.Id,
                            resolution.PlcCode
                        },
                        cancellationToken: cancellationToken)).ConfigureAwait(false);
                    if (affected == 1)
                    {
                        migratedCount++;
                    }
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                issues.Add(new DataPipelineIdentityMigrationIssue(
                    target.DatabaseName,
                    target.TableName,
                    0,
                    string.Empty,
                    null,
                    string.Empty,
                    string.Empty,
                    "data_pipeline_identity_migration_failed",
                    ex.Message));
            }
        }

        logger.Info(
            $"[数据管道身份迁移] 已迁移 {migratedCount} 条历史记录，保留并隔离 {issues.Count} 条未解析记录。");
        return new DataPipelineIdentityMigrationResult(migratedCount, issues);
    }

    private static IdentityResolution Resolve(
        LegacyIdentityRow row,
        IReadOnlyDictionary<int, NetworkDeviceEntity> deviceById)
    {
        if (row.IdempotencyKeyVersion is not (1 or 2))
        {
            return IdentityResolution.Blocked(
                "data_pipeline_idempotency_version_invalid",
                $"幂等版本 {row.IdempotencyKeyVersion} 无效，禁止自动改写。");
        }

        if (row.IdempotencyKeyVersion == 2)
        {
            return IdentityResolution.Blocked(
                "data_pipeline_v2_plc_identity_missing",
                "V2 记录缺少 PlcCode；禁止降级为 V1 或猜测业务归属。");
        }

        var cellDataDeviceCode = TryReadCellDataDeviceCode(row.CellDataJson);
        var mappedDevice = row.NetworkDeviceId is > 0
                           && deviceById.TryGetValue(row.NetworkDeviceId.Value, out var device)
            ? device
            : null;
        if (!string.IsNullOrWhiteSpace(cellDataDeviceCode))
        {
            if (mappedDevice is not null
                && !string.Equals(
                    mappedDevice.PlcCode,
                    cellDataDeviceCode,
                    StringComparison.OrdinalIgnoreCase))
            {
                return IdentityResolution.Blocked(
                    "data_pipeline_plc_identity_conflict",
                    $"CellData.DeviceCode={cellDataDeviceCode} 与 NetworkDeviceId={row.NetworkDeviceId} "
                    + $"映射的 PlcCode={mappedDevice.PlcCode} 冲突。");
            }

            return IdentityResolution.Success(cellDataDeviceCode);
        }

        if (mappedDevice is not null)
        {
            return IdentityResolution.Success(mappedDevice.PlcCode);
        }

        return IdentityResolution.Blocked(
            "data_pipeline_plc_identity_unresolved",
            "CellData.DeviceCode 为空，且 NetworkDeviceId 无法唯一映射到权威 PlcCode；未使用 DeviceName 猜测。");
    }

    private static string TryReadCellDataDeviceCode(string? cellDataJson)
    {
        if (string.IsNullOrWhiteSpace(cellDataJson))
        {
            return string.Empty;
        }

        try
        {
            using var document = JsonDocument.Parse(cellDataJson);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return string.Empty;
            }

            foreach (var property in document.RootElement.EnumerateObject())
            {
                if (string.Equals(property.Name, "deviceCode", StringComparison.OrdinalIgnoreCase)
                    && property.Value.ValueKind == JsonValueKind.String)
                {
                    return property.Value.GetString()?.Trim() ?? string.Empty;
                }
            }
        }
        catch (JsonException)
        {
        }

        return string.Empty;
    }

    private static DataPipelineIdentityMigrationIssue CreateIssue(
        MigrationTarget target,
        LegacyIdentityRow row,
        IdentityResolution resolution)
        => new(
            target.DatabaseName,
            target.TableName,
            row.Id,
            row.PlcCode,
            row.NetworkDeviceId,
            row.DeviceName,
            row.TaskKey,
            resolution.DiagnosticCode,
            resolution.DiagnosticMessage);

    private sealed record MigrationTarget(string DatabaseName, string TableName);

    private sealed class LegacyIdentityRow
    {
        public long Id { get; init; }

        public string CellDataJson { get; init; } = string.Empty;

        public string PlcCode { get; init; } = string.Empty;

        public int IdempotencyKeyVersion { get; init; }

        public int? NetworkDeviceId { get; init; }

        public string DeviceName { get; init; } = string.Empty;

        public string TaskKey { get; init; } = string.Empty;
    }

    private sealed record IdentityResolution(
        bool IsSuccess,
        string PlcCode,
        string DiagnosticCode,
        string DiagnosticMessage)
    {
        public static IdentityResolution Success(string plcCode)
            => new(true, plcCode.Trim(), string.Empty, string.Empty);

        public static IdentityResolution Blocked(string code, string message)
            => new(false, string.Empty, code, message);
    }
}
