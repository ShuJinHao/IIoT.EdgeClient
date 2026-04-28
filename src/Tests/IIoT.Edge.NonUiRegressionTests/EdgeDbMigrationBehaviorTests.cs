using IIoT.Edge.Infrastructure.Persistence.EfCore;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

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
            Assert.Contains("group_name", columns);
            Assert.Contains("display_role", columns);
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
}
