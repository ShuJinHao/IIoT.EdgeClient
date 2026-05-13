using IIoT.Edge.Infrastructure.Persistence.Dapper.Connection;
using Microsoft.Data.Sqlite;

namespace IIoT.Edge.NonUiRegressionTests;

public sealed class SqliteConnectionFactoryBehaviorTests
{
    [Fact]
    public async Task CreateAsync_ShouldApplySharedPragmas()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "edge-sqlite-factory-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try
        {
            var factory = new SqliteConnectionFactory(tempDir);

            await using var connection = (SqliteConnection)await factory.CreateAsync("pipeline_cloud");

            Assert.Equal(5000, await GetPragmaAsync(connection, "busy_timeout"));
            Assert.Equal(1, await GetPragmaAsync(connection, "foreign_keys"));
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                try
                {
                    Directory.Delete(tempDir, recursive: true);
                }
                catch (IOException)
                {
                }
            }
        }
    }

    private static async Task<int> GetPragmaAsync(SqliteConnection connection, string pragmaName)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = $"PRAGMA {pragmaName};";
        var scalar = await command.ExecuteScalarAsync();
        return Convert.ToInt32(scalar);
    }
}
