using IIoT.Edge.Infrastructure.Persistence.EfCore;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;

namespace IIoT.Edge.NonUiRegressionTests;

public sealed class EdgeDbMigrationBehaviorTests
{
    [Fact]
    public async Task Migrate_WhenCreatingFreshDatabase_ShouldCreateCurrentHardwareColumns()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "edge-ef-migration-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try
        {
            var dbPath = Path.Combine(tempDir, "edge.db");
            using var services = CreateServiceProvider(dbPath);

            services.ApplyMigrations();

            await using var connection = new SqliteConnection($"Data Source={dbPath}");
            await connection.OpenAsync(TestContext.Current.CancellationToken);

            await using var command = connection.CreateCommand();
            command.CommandText = "PRAGMA table_info('hw_io_mapping');";

            var columns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            await using var reader = await command.ExecuteReaderAsync(TestContext.Current.CancellationToken);
            while (await reader.ReadAsync(TestContext.Current.CancellationToken))
            {
                columns.Add(reader.GetString(1));
            }

            Assert.Contains("category", columns);
            Assert.Contains("signal_key", columns);
            Assert.Contains("business_group", columns);
            Assert.DoesNotContain("signal_name", columns);

            var networkColumns = await LoadColumnsAsync(connection, "hw_network_device");
            Assert.DoesNotContain("module_id", networkColumns);
            Assert.Contains("protocol_frame", networkColumns);
            Assert.Contains("plc_code", networkColumns);
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
    public async Task Migrate_WhenAddingStablePlcCode_ShouldBackfillWithoutBlockingLegacyRows()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "edge-ef-migration-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try
        {
            var cancellationToken = TestContext.Current.CancellationToken;
            var dbPath = Path.Combine(tempDir, "edge.db");
            using var services = CreateServiceProvider(dbPath);
            var factory = services.GetRequiredService<IDbContextFactory<EdgeDbContext>>();
            await using var db = await factory.CreateDbContextAsync(cancellationToken);
            var migrator = db.GetService<IMigrator>();

            await migrator.MigrateAsync("20260701093000_AddNetworkDeviceProtocolFrame", cancellationToken);

            await using (var connection = new SqliteConnection($"Data Source={dbPath}"))
            {
                await connection.OpenAsync(cancellationToken);
                await using var insert = connection.CreateCommand();
                insert.CommandText = $"""
                    INSERT INTO hw_network_device
                        (id, device_name, device_type, ip_address, port1, connect_timeout, is_enabled)
                    VALUES
                        (1, 'PLC-PRIMARY', 'PLC', '192.168.0.11', 102, 3000, 1),
                        (2, 'PLC-DUP', 'PLC', '192.168.0.12', 102, 3000, 1),
                        (3, 'plc-dup', 'PLC', '192.168.0.13', 102, 3000, 1),
                        (4, 'PLC-INTERNAL-LEGACY', 'PLC', '192.168.0.14', 102, 3000, 1),
                        (5, '{new string('X', 70)}', 'PLC', '192.168.0.15', 102, 3000, 1);
                    """;
                await insert.ExecuteNonQueryAsync(cancellationToken);
            }

            await migrator.MigrateAsync(cancellationToken: cancellationToken);

            await using var verification = new SqliteConnection($"Data Source={dbPath}");
            await verification.OpenAsync(cancellationToken);
            await using var query = verification.CreateCommand();
            query.CommandText = "SELECT id, plc_code FROM hw_network_device ORDER BY id;";

            var plcCodes = new Dictionary<long, string>();
            await using var reader = await query.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                plcCodes.Add(reader.GetInt64(0), reader.GetString(1));
            }

            Assert.Equal("PLC-PRIMARY", plcCodes[1]);
            Assert.Equal("PLC-INTERNAL-2", plcCodes[2]);
            Assert.Equal("PLC-INTERNAL-3", plcCodes[3]);
            Assert.Equal("PLC-INTERNAL-4", plcCodes[4]);
            Assert.Equal("PLC-INTERNAL-5", plcCodes[5]);
            Assert.Equal(plcCodes.Count, plcCodes.Values.Distinct(StringComparer.OrdinalIgnoreCase).Count());
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
    public void ApplyMigrations_ShouldResolveSchemaRepairFromServiceProvider()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "edge-ef-migration-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try
        {
            var dbPath = Path.Combine(tempDir, "edge.db");
            var repair = new RecordingSchemaRepair();
            using var services = new ServiceCollection()
                .AddDbContextFactory<EdgeDbContext>(options => options.UseSqlite($"Data Source={dbPath}"))
                .AddSingleton<IEdgeSqliteSchemaRepair>(repair)
                .BuildServiceProvider();

            services.ApplyMigrations();

            Assert.Equal(1, repair.CallCount);
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
    public async Task ApplyMigrations_WhenOldIoMappingColumnsAlreadyApplied_ShouldRepairRenamedColumns()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "edge-ef-migration-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try
        {
            var dbPath = Path.Combine(tempDir, "edge.db");
            await CreateOldIoMappingDatabaseAsync(dbPath);

            using var services = CreateServiceProvider(dbPath);

            services.ApplyMigrations();

            await using var connection = new SqliteConnection($"Data Source={dbPath}");
            await connection.OpenAsync(TestContext.Current.CancellationToken);

            var columns = await LoadColumnsAsync(connection);
            Assert.Contains("signal_key", columns);
            Assert.Contains("business_group", columns);
            Assert.DoesNotContain("signal_name", columns);

            await using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT signal_key, business_group
                FROM hw_io_mapping
                WHERE id = 1;
                """;

            await using var reader = await command.ExecuteReaderAsync(TestContext.Current.CancellationToken);
            Assert.True(await reader.ReadAsync(TestContext.Current.CancellationToken));
            Assert.Equal("Homogenization.Interaction.Inbound", reader.GetString(0));
            Assert.Equal("扫码进站", reader.GetString(1));
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
    public void Repair_WhenIoMappingTableDoesNotExist_ShouldNotThrow()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "edge-ef-migration-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try
        {
            var dbPath = Path.Combine(tempDir, "edge.db");
            var options = new DbContextOptionsBuilder<EdgeDbContext>()
                .UseSqlite($"Data Source={dbPath}")
                .Options;
            using var db = new EdgeDbContext(options);
            var repair = new EdgeSqliteSchemaRepair();

            var exception = Record.Exception(() => repair.Repair(db));

            Assert.Null(exception);
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

    private static ServiceProvider CreateServiceProvider(string dbPath)
    {
        return new ServiceCollection()
            .AddEfCorePersistenceInfrastructure(dbPath)
            .BuildServiceProvider();
    }

    private static async Task CreateOldIoMappingDatabaseAsync(string dbPath)
    {
        await using var connection = new SqliteConnection($"Data Source={dbPath}");
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE "__EFMigrationsHistory" (
                "MigrationId" TEXT NOT NULL CONSTRAINT "PK___EFMigrationsHistory" PRIMARY KEY,
                "ProductVersion" TEXT NOT NULL
            );

            INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion") VALUES
                ('20260323061057_InitialCreate', '10.0.0'),
                ('20260323083510_AddDeviceTypeAndModel', '10.0.0'),
                ('20260323091143_RemoveAuditFields', '10.0.0'),
                ('20260324015923_AddConfigTables', '10.0.0'),
                ('20260416060225_AddNetworkDeviceModuleId', '10.0.0'),
                ('20260423143000_AddIoMappingDisplayFields', '10.0.0'),
                ('20260603090000_RemoveHardwareConfigBindingFields', '10.0.0');

            CREATE TABLE "hw_network_device" (
                "id" INTEGER NOT NULL CONSTRAINT "PK_hw_network_device" PRIMARY KEY AUTOINCREMENT,
                "device_name" TEXT NOT NULL,
                "device_type" TEXT NOT NULL,
                "device_model" TEXT NULL,
                "ip_address" TEXT NOT NULL,
                "port1" INTEGER NOT NULL,
                "port2" INTEGER NULL,
                "send_cmd1" TEXT NULL,
                "send_cmd2" TEXT NULL,
                "connect_timeout" INTEGER NOT NULL DEFAULT 3000,
                "is_enabled" INTEGER NOT NULL DEFAULT 1,
                "remark" TEXT NULL
            );

            CREATE INDEX "ix_hw_network_device_ip"
                ON "hw_network_device" ("ip_address");

            CREATE TABLE "hw_io_mapping" (
                "id" INTEGER NOT NULL CONSTRAINT "PK_hw_io_mapping" PRIMARY KEY AUTOINCREMENT,
                "network_device_id" INTEGER NOT NULL,
                "label" TEXT NOT NULL,
                "plc_address" TEXT NOT NULL,
                "address_count" INTEGER NOT NULL,
                "data_type" TEXT NOT NULL,
                "direction" TEXT NOT NULL,
                "category" TEXT NOT NULL,
                "group_name" TEXT NOT NULL,
                "display_role" TEXT NOT NULL,
                "sort_order" INTEGER NOT NULL,
                "remark" TEXT NULL
            );

            INSERT INTO "hw_io_mapping" (
                "id",
                "network_device_id",
                "label",
                "plc_address",
                "address_count",
                "data_type",
                "direction",
                "category",
                "group_name",
                "display_role",
                "sort_order",
                "remark")
            VALUES (
                1,
                10,
                'Homogenization.Interaction.Inbound',
                'D701',
                1,
                'Int16',
                'Read',
                '信号交互',
                '扫码进站',
                'PLC 触发',
                2,
                NULL);
            """;

        await command.ExecuteNonQueryAsync();
    }

    private static async Task<HashSet<string>> LoadColumnsAsync(
        SqliteConnection connection,
        string tableName = "hw_io_mapping")
    {
        await using var command = connection.CreateCommand();
        command.CommandText = $"PRAGMA table_info('{tableName}');";

        var columns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            columns.Add(reader.GetString(1));
        }

        return columns;
    }

    private sealed class RecordingSchemaRepair : IEdgeSqliteSchemaRepair
    {
        public int CallCount { get; private set; }

        public void Repair(EdgeDbContext db)
        {
            ArgumentNullException.ThrowIfNull(db);
            CallCount++;
        }
    }
}
