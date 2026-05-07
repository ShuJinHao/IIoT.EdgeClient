using IIoT.Edge.Infrastructure.Persistence.EfCore;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace IIoT.Edge.NonUiRegressionTests;

public sealed class EdgeDbMigrationBehaviorTests
{
    [Fact]
    public async Task Migrate_WhenCreatingFreshDatabase_ShouldCreateIoMappingDisplayColumns()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "edge-ef-migration-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try
        {
            var dbPath = Path.Combine(tempDir, "edge.db");
            var options = new DbContextOptionsBuilder<EdgeDbContext>()
                .UseSqlite($"Data Source={dbPath}")
                .Options;

            await using (var db = new EdgeDbContext(options))
            {
                await db.Database.MigrateAsync();
            }

            await using var connection = new SqliteConnection($"Data Source={dbPath}");
            await connection.OpenAsync();

            await using var command = connection.CreateCommand();
            command.CommandText = "PRAGMA table_info('hw_io_mapping');";

            var columns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            await using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                columns.Add(reader.GetString(1));
            }

            Assert.Contains("category", columns);
            Assert.Contains("signal_key", columns);
            Assert.Contains("business_group", columns);
            Assert.Contains("signal_name", columns);
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

            var services = new ServiceCollection()
                .AddEfCorePersistenceInfrastructure(dbPath)
                .BuildServiceProvider();

            services.ApplyMigrations();

            await using var connection = new SqliteConnection($"Data Source={dbPath}");
            await connection.OpenAsync();

            var columns = await LoadColumnsAsync(connection);
            Assert.Contains("signal_key", columns);
            Assert.Contains("business_group", columns);
            Assert.Contains("signal_name", columns);

            await using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT signal_key, business_group, signal_name
                FROM hw_io_mapping
                WHERE id = 1;
                """;

            await using var reader = await command.ExecuteReaderAsync();
            Assert.True(await reader.ReadAsync());
            Assert.Equal("Homogenization.Interaction.Inbound", reader.GetString(0));
            Assert.Equal("扫码进站", reader.GetString(1));
            Assert.Equal("PLC 触发", reader.GetString(2));
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
                ('20260423143000_AddIoMappingDisplayFields', '10.0.0');

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

    private static async Task<HashSet<string>> LoadColumnsAsync(SqliteConnection connection)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA table_info('hw_io_mapping');";

        var columns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            columns.Add(reader.GetString(1));
        }

        return columns;
    }
}
