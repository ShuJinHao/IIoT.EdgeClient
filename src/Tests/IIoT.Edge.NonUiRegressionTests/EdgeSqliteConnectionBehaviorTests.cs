using IIoT.Edge.Infrastructure.Persistence.EfCore;
using Microsoft.Data.Sqlite;

namespace IIoT.Edge.NonUiRegressionTests;

public sealed class EdgeSqliteConnectionBehaviorTests
{
    [Fact]
    public void BuildConnectionString_ShouldUseRuntimeSqliteSettings()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "edge-ef-sqlite-tests", Guid.NewGuid().ToString("N"));
        var dbPath = Path.Combine(tempDir, "edge.db");
        var connection = new EdgeSqliteConnection();

        var builder = new SqliteConnectionStringBuilder(connection.BuildConnectionString(dbPath));

        Assert.Equal(Path.GetFullPath(dbPath), builder.DataSource);
        Assert.Equal(SqliteOpenMode.ReadWriteCreate, builder.Mode);
        Assert.Equal(SqliteCacheMode.Shared, builder.Cache);
        Assert.False(builder.Pooling);
        Assert.Equal(5, builder.DefaultTimeout);
    }

    [Fact]
    public async Task EnsureRuntimePragmas_ShouldCreateDirectoryAndEnableWalMode()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "edge-ef-sqlite-tests", Guid.NewGuid().ToString("N"));

        try
        {
            var dbPath = Path.Combine(tempDir, "nested", "edge.db");
            var connectionService = new EdgeSqliteConnection();

            connectionService.EnsureRuntimePragmas(dbPath);

            Assert.True(Directory.Exists(Path.GetDirectoryName(dbPath)));

            await using var connection = new SqliteConnection(connectionService.BuildConnectionString(dbPath));
            await connection.OpenAsync(TestContext.Current.CancellationToken);
            Assert.Equal("wal", await GetPragmaTextAsync(connection, "journal_mode"));
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
    public void ResolveDesignTimeDbPath_ShouldPreferCommandLineArgument()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "edge-ef-sqlite-tests", Guid.NewGuid().ToString("N"));
        var fromArgs = Path.Combine(tempDir, "args.db");
        var fromEnv = Path.Combine(tempDir, "env.db");

        WithEnvironment("IIOT_EDGE_EFCORE_DB", fromEnv, () =>
        {
            var connection = new EdgeSqliteConnection();

            var resolved = connection.ResolveDesignTimeDbPath([$"--dbPath={fromArgs}"]);

            Assert.Equal(Path.GetFullPath(fromArgs), resolved);
        });
    }

    [Fact]
    public void ResolveDesignTimeDbPath_ShouldUsePrimaryEnvironmentVariableBeforeLegacyVariable()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "edge-ef-sqlite-tests", Guid.NewGuid().ToString("N"));
        var primary = Path.Combine(tempDir, "primary.db");
        var legacy = Path.Combine(tempDir, "legacy.db");

        WithEnvironment("IIOT_EDGE_EFCORE_DB", primary, () =>
        {
            WithEnvironment("EdgeDb__DesignTimePath", legacy, () =>
            {
                var connection = new EdgeSqliteConnection();

                var resolved = connection.ResolveDesignTimeDbPath([]);

                Assert.Equal(Path.GetFullPath(primary), resolved);
            });
        });
    }

    [Fact]
    public void ResolveDesignTimeDbPath_ShouldUseLegacyEnvironmentVariableWhenPrimaryIsMissing()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "edge-ef-sqlite-tests", Guid.NewGuid().ToString("N"));
        var legacy = Path.Combine(tempDir, "legacy.db");

        WithEnvironment("IIOT_EDGE_EFCORE_DB", null, () =>
        {
            WithEnvironment("EdgeDb__DesignTimePath", legacy, () =>
            {
                var connection = new EdgeSqliteConnection();

                var resolved = connection.ResolveDesignTimeDbPath([]);

                Assert.Equal(Path.GetFullPath(legacy), resolved);
            });
        });
    }

    private static async Task<string> GetPragmaTextAsync(SqliteConnection connection, string pragmaName)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = $"PRAGMA {pragmaName};";
        var scalar = await command.ExecuteScalarAsync();
        return Convert.ToString(scalar) ?? string.Empty;
    }

    private static void WithEnvironment(string name, string? value, Action action)
    {
        var original = Environment.GetEnvironmentVariable(name);
        try
        {
            Environment.SetEnvironmentVariable(name, value);
            action();
        }
        finally
        {
            Environment.SetEnvironmentVariable(name, original);
        }
    }
}
