using System.Linq.Expressions;
using Dapper;
using IIoT.Edge.Domain.Hardware.Aggregates;
using IIoT.Edge.Infrastructure.Persistence.Dapper.Connection;
using IIoT.Edge.Infrastructure.Persistence.Dapper.Repository;
using IIoT.Edge.Infrastructure.Persistence.Dapper.Stores;
using IIoT.Edge.Module.Contracts.DataPipeline.CellData;
using IIoT.Edge.Module.Contracts.Hardware;
using IIoT.Edge.SharedKernel.Repository;
using IIoT.Edge.SharedKernel.Specification;
using Microsoft.Data.Sqlite;

namespace IIoT.Edge.Persistence.Tests;

public sealed class DataPipelineIdentityMigrationBehaviorTests
{
    [Fact]
    public async Task MigrateAsync_ShouldPreferCellDeviceCodeThenUniqueNetworkDeviceIdAndPreserveConflicts()
    {
        var tempDir = Path.Combine(
            Path.GetTempPath(),
            "edge-data-pipeline-identity-tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try
        {
            var logger = new FakeLogService();
            var connectionFactory = new SqliteConnectionFactory(tempDir);
            await InitializeAllIdentityTablesAsync(connectionFactory, logger);
            var plcOne = NetworkDeviceEntity.Create(
                    "Display-One",
                    DeviceType.PLC,
                    "127.0.0.1",
                    6001,
                    "PLC-ONE")
                .WithId(1);
            var plcTwo = NetworkDeviceEntity.Create(
                    "Display-Two",
                    DeviceType.PLC,
                    "127.0.0.1",
                    6002,
                    "PLC-TWO")
                .WithId(2);
            using (var connection = connectionFactory.Create("pipeline_cloud"))
            {
                await InsertLegacyRetryAsync(
                    connection,
                    "cell-code",
                    """{"deviceCode":"PLC-ONE"}""",
                    1,
                    "Renamed-One");
                await InsertLegacyRetryAsync(
                    connection,
                    "network-id",
                    """{"deviceCode":""}""",
                    2,
                    "Any-Display");
                await InsertLegacyRetryAsync(
                    connection,
                    "conflict",
                    """{"deviceCode":"PLC-OTHER"}""",
                    1,
                    "Display-One");
            }

            var migration = new DataPipelineIdentityMigration(
                connectionFactory,
                new FakeNetworkDeviceReadRepository([plcOne, plcTwo]),
                logger);
            var result = await migration.MigrateAsync(TestContext.Current.CancellationToken);

            Assert.Equal(2, result.MigratedRecordCount);
            var conflict = Assert.Single(result.Issues);
            Assert.Equal("data_pipeline_plc_identity_conflict", conflict.DiagnosticCode);
            Assert.Equal("conflict", conflict.TaskKey);

            using var readConnection = connectionFactory.Create("pipeline_cloud");
            var rows = (await readConnection.QueryAsync<IdentityRow>(
                """
                SELECT TaskKey, PlcCode, IdempotencyKeyVersion
                FROM failed_cloud_records
                ORDER BY Id ASC
                """)).ToArray();
            Assert.Equal("PLC-ONE", rows[0].PlcCode);
            Assert.Equal("PLC-TWO", rows[1].PlcCode);
            Assert.Equal(string.Empty, rows[2].PlcCode);
            Assert.All(rows, row => Assert.Equal(1, row.IdempotencyKeyVersion));
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, recursive: true);
            }
        }
    }

    [Fact]
    public async Task MigrateAsync_ShouldBackfillAllSixDurableIdentityTablesAsV1()
    {
        var tempDir = Path.Combine(
            Path.GetTempPath(),
            "edge-data-pipeline-identity-tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try
        {
            var logger = new FakeLogService();
            var connectionFactory = new SqliteConnectionFactory(tempDir);
            await InitializeAllIdentityTablesAsync(connectionFactory, logger);
            var plc = NetworkDeviceEntity.Create(
                    "已改名设备",
                    DeviceType.PLC,
                    "127.0.0.1",
                    6001,
                    "PLC-STABLE")
                .WithId(7);

            using (var cloud = connectionFactory.Create("pipeline_cloud"))
            {
                await InsertLegacyRetryAsync(
                    cloud,
                    "failed_cloud_records",
                    "cloud-retry",
                    """{"deviceCode":"PLC-STABLE"}""",
                    7,
                    "旧显示名");
                await InsertLegacyFallbackAsync(
                    cloud,
                    "cloud_fallback_records",
                    "cloud-fallback",
                    """{"deviceCode":"PLC-STABLE"}""",
                    7,
                    "旧显示名");
                await InsertLegacyDeadLetterAsync(
                    cloud,
                    "dead_cloud_records",
                    "cloud-dead",
                    """{"deviceCode":"PLC-STABLE"}""",
                    7,
                    "旧显示名");
            }

            using (var mes = connectionFactory.Create("pipeline_mes"))
            {
                await InsertLegacyRetryAsync(
                    mes,
                    "failed_mes_records",
                    "mes-retry",
                    """{"deviceCode":"PLC-STABLE"}""",
                    7,
                    "旧显示名");
                await InsertLegacyFallbackAsync(
                    mes,
                    "mes_fallback_records",
                    "mes-fallback",
                    """{"deviceCode":"PLC-STABLE"}""",
                    7,
                    "旧显示名");
                await InsertLegacyDeadLetterAsync(
                    mes,
                    "dead_mes_records",
                    "mes-dead",
                    """{"deviceCode":"PLC-STABLE"}""",
                    7,
                    "旧显示名");
            }

            var migration = new DataPipelineIdentityMigration(
                connectionFactory,
                new FakeNetworkDeviceReadRepository([plc]),
                logger);
            var result = await migration.MigrateAsync(TestContext.Current.CancellationToken);

            Assert.Equal(6, result.MigratedRecordCount);
            Assert.Empty(result.Issues);
            foreach (var target in IdentityTargets)
            {
                using var connection = connectionFactory.Create(target.DatabaseName);
                var row = await ReadIdentityAsync(connection, target.TableName);
                Assert.Equal("PLC-STABLE", row.PlcCode);
                Assert.Equal(1, row.IdempotencyKeyVersion);
            }
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, recursive: true);
            }
        }
    }

    [Fact]
    public async Task MigrateAsync_WhenV2RecordHasNoPlcCode_ShouldPreserveAndBlockInsteadOfDowngrading()
    {
        var tempDir = Path.Combine(
            Path.GetTempPath(),
            "edge-data-pipeline-identity-tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try
        {
            var logger = new FakeLogService();
            var connectionFactory = new SqliteConnectionFactory(tempDir);
            await InitializeAllIdentityTablesAsync(connectionFactory, logger);
            var plc = NetworkDeviceEntity.Create(
                    "当前显示名",
                    DeviceType.PLC,
                    "127.0.0.1",
                    6001,
                    "PLC-STABLE")
                .WithId(7);
            using (var connection = connectionFactory.Create("pipeline_cloud"))
            {
                await InsertLegacyRetryAsync(
                    connection,
                    "failed_cloud_records",
                    "v2-missing-code",
                    """{"deviceCode":"PLC-STABLE"}""",
                    7,
                    "旧显示名",
                    idempotencyKeyVersion: 2);
            }

            var migration = new DataPipelineIdentityMigration(
                connectionFactory,
                new FakeNetworkDeviceReadRepository([plc]),
                logger);
            var result = await migration.MigrateAsync(TestContext.Current.CancellationToken);

            Assert.Equal(0, result.MigratedRecordCount);
            var issue = Assert.Single(result.Issues);
            Assert.Equal("data_pipeline_v2_plc_identity_missing", issue.DiagnosticCode);
            using var readConnection = connectionFactory.Create("pipeline_cloud");
            var row = await ReadIdentityAsync(readConnection, "failed_cloud_records");
            Assert.Equal(string.Empty, row.PlcCode);
            Assert.Equal(2, row.IdempotencyKeyVersion);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, recursive: true);
            }
        }
    }

    private static readonly IdentityTarget[] IdentityTargets =
    [
        new("pipeline_cloud", "failed_cloud_records"),
        new("pipeline_cloud", "cloud_fallback_records"),
        new("pipeline_cloud", "dead_cloud_records"),
        new("pipeline_mes", "failed_mes_records"),
        new("pipeline_mes", "mes_fallback_records"),
        new("pipeline_mes", "dead_mes_records")
    ];

    private static async Task InitializeAllIdentityTablesAsync(
        SqliteConnectionFactory connectionFactory,
        FakeLogService logger)
    {
        var serializer = new CellDataJsonSerializer(new CellDataTypeRegistry());
        ITableInitializer[] initializers =
        [
            new CloudRetryRecordStore(connectionFactory, logger, serializer),
            new CloudFallbackBufferStore(connectionFactory, logger, serializer),
            new CloudDeadLetterStore(connectionFactory, logger),
            new MesRetryRecordStore(connectionFactory, logger, serializer),
            new MesFallbackBufferStore(connectionFactory, logger, serializer),
            new MesDeadLetterStore(connectionFactory, logger)
        ];
        foreach (var group in initializers.GroupBy(static initializer => initializer.DbName))
        {
            using var connection = connectionFactory.Create(group.Key);
            foreach (var initializer in group)
            {
                await initializer.InitializeTableAsync(connection);
            }
        }
    }

    private static Task<int> InsertLegacyRetryAsync(
        System.Data.IDbConnection connection,
        string taskKey,
        string cellDataJson,
        int? networkDeviceId,
        string deviceName)
        => InsertLegacyRetryAsync(
            connection,
            "failed_cloud_records",
            taskKey,
            cellDataJson,
            networkDeviceId,
            deviceName);

    private static Task<int> InsertLegacyRetryAsync(
        System.Data.IDbConnection connection,
        string tableName,
        string taskKey,
        string cellDataJson,
        int? networkDeviceId,
        string deviceName,
        int idempotencyKeyVersion = 1)
        => connection.ExecuteAsync(
            $"""
            INSERT INTO {tableName}
                (ProcessType, CellDataJson, FailedTarget, ErrorMessage,
                 RetryCount, NextRetryTime, CreatedAt,
                 PlcCode, IdempotencyKeyVersion, NetworkDeviceId, DeviceName,
                 ModuleId, TaskKey, PlanSessionId, MainPlanCode, TraceBatchNumber)
            VALUES
                ('TestProcess', @CellDataJson, 'Cloud', 'legacy',
                 0, @Now, @Now,
                 '', @IdempotencyKeyVersion, @NetworkDeviceId, @DeviceName,
                 'TestModule', @TaskKey, '', '', '')
            """,
            new
            {
                CellDataJson = cellDataJson,
                NetworkDeviceId = networkDeviceId,
                DeviceName = deviceName,
                TaskKey = taskKey,
                IdempotencyKeyVersion = idempotencyKeyVersion,
                Now = DateTime.UtcNow.ToString("O")
            });

    private static Task<int> InsertLegacyFallbackAsync(
        System.Data.IDbConnection connection,
        string tableName,
        string taskKey,
        string cellDataJson,
        int? networkDeviceId,
        string deviceName)
        => connection.ExecuteAsync(
            $"""
            INSERT INTO {tableName}
                (ProcessType, CellDataJson, FailedTarget, ErrorMessage, CreatedAt,
                 PlcCode, IdempotencyKeyVersion, NetworkDeviceId, DeviceName,
                 ModuleId, TaskKey, PlanSessionId, MainPlanCode, TraceBatchNumber)
            VALUES
                ('TestProcess', @CellDataJson, 'Fallback', 'legacy', @Now,
                 '', 1, @NetworkDeviceId, @DeviceName,
                 'TestModule', @TaskKey, '', '', '')
            """,
            new
            {
                CellDataJson = cellDataJson,
                NetworkDeviceId = networkDeviceId,
                DeviceName = deviceName,
                TaskKey = taskKey,
                Now = DateTime.UtcNow.ToString("O")
            });

    private static Task<int> InsertLegacyDeadLetterAsync(
        System.Data.IDbConnection connection,
        string tableName,
        string taskKey,
        string cellDataJson,
        int? networkDeviceId,
        string deviceName)
        => connection.ExecuteAsync(
            $"""
            INSERT INTO {tableName}
                (ProcessType, CellDataJson, FailedTarget, SourceTable, SourceRecordId,
                 FailureStage, FailureReason, CreatedAt,
                 PlcCode, IdempotencyKeyVersion, NetworkDeviceId, DeviceName,
                 ModuleId, TaskKey, PlanSessionId, MainPlanCode, TraceBatchNumber)
            VALUES
                ('TestProcess', @CellDataJson, 'DeadLetter', 'legacy_records', 1,
                 'Retry', 'legacy', @Now,
                 '', 1, @NetworkDeviceId, @DeviceName,
                 'TestModule', @TaskKey, '', '', '')
            """,
            new
            {
                CellDataJson = cellDataJson,
                NetworkDeviceId = networkDeviceId,
                DeviceName = deviceName,
                TaskKey = taskKey,
                Now = DateTime.UtcNow.ToString("O")
            });

    private static async Task<IdentityRow> ReadIdentityAsync(
        System.Data.IDbConnection connection,
        string tableName)
        => await connection.QuerySingleAsync<IdentityRow>(
            $"""
             SELECT TaskKey, PlcCode, IdempotencyKeyVersion
             FROM {tableName}
             ORDER BY Id ASC
             LIMIT 1
             """);

    private sealed class IdentityRow
    {
        public string TaskKey { get; init; } = string.Empty;

        public string PlcCode { get; init; } = string.Empty;

        public int IdempotencyKeyVersion { get; init; }
    }

    private sealed record IdentityTarget(string DatabaseName, string TableName);

    private sealed class FakeNetworkDeviceReadRepository(
        IReadOnlyCollection<NetworkDeviceEntity> devices)
        : IReadRepository<NetworkDeviceEntity>
    {
        public Task<List<NetworkDeviceEntity>> GetListAsync(
            Expression<Func<NetworkDeviceEntity, bool>> expression,
            CancellationToken cancellationToken = default)
            => Task.FromResult(devices.Where(expression.Compile()).ToList());

        public Task<NetworkDeviceEntity?> GetByIdAsync<TKey>(
            TKey id,
            CancellationToken cancellationToken = default)
            where TKey : notnull
            => throw new NotSupportedException();

        public Task<NetworkDeviceEntity?> GetAsync(
            Expression<Func<NetworkDeviceEntity, bool>> expression,
            Expression<Func<NetworkDeviceEntity, object>>[]? includes = null,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<List<NetworkDeviceEntity>> GetListAsync(
            Expression<Func<NetworkDeviceEntity, bool>> expression,
            Expression<Func<NetworkDeviceEntity, object>>[]? includes = null,
            CancellationToken cancellationToken = default)
            => GetListAsync(expression, cancellationToken);

        public Task<List<NetworkDeviceEntity>> GetListAsync(
            ISpecification<NetworkDeviceEntity>? specification = null,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<NetworkDeviceEntity?> GetSingleOrDefaultAsync(
            ISpecification<NetworkDeviceEntity>? specification = null,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<int> GetCountAsync(
            Expression<Func<NetworkDeviceEntity, bool>> expression,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<int> CountAsync(
            ISpecification<NetworkDeviceEntity>? specification = null,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<bool> AnyAsync(
            ISpecification<NetworkDeviceEntity>? specification = null,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }
}
