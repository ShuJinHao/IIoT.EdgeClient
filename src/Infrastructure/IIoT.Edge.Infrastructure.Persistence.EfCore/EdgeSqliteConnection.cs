using Microsoft.Data.Sqlite;

namespace IIoT.Edge.Infrastructure.Persistence.EfCore;

internal interface IEdgeSqliteConnection
{
    string BuildConnectionString(string dbPath);

    void EnsureRuntimePragmas(string dbPath);

    string ResolveDesignTimeDbPath(string[] args);
}

internal sealed class EdgeSqliteConnection : IEdgeSqliteConnection
{
    private const int BusyTimeoutSeconds = 5;
    private const int BusyTimeoutMilliseconds = 5000;

    public string BuildConnectionString(string dbPath)
    {
        if (string.IsNullOrWhiteSpace(dbPath))
        {
            throw new ArgumentNullException(nameof(dbPath));
        }

        return new SqliteConnectionStringBuilder
        {
            DataSource = Path.GetFullPath(dbPath),
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared,
            DefaultTimeout = BusyTimeoutSeconds,
            Pooling = false
        }.ToString();
    }

    public void EnsureRuntimePragmas(string dbPath)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(dbPath));
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        using var connection = new SqliteConnection(BuildConnectionString(dbPath));
        connection.Open();
        ExecutePragma(connection, "PRAGMA journal_mode=WAL;");
        ExecutePragma(connection, "PRAGMA foreign_keys=ON;");
        ExecutePragma(connection, $"PRAGMA busy_timeout={BusyTimeoutMilliseconds};");
    }

    public string ResolveDesignTimeDbPath(string[] args)
    {
        var fromArgs = args
            .FirstOrDefault(arg => arg.StartsWith("--dbPath=", StringComparison.OrdinalIgnoreCase))
            ?.Split('=', 2)[1];

        var configured = FirstNonEmpty(
            fromArgs,
            Environment.GetEnvironmentVariable("IIOT_EDGE_EFCORE_DB"),
            Environment.GetEnvironmentVariable("EdgeDb__DesignTimePath"));

        if (!string.IsNullOrWhiteSpace(configured))
        {
            return Path.GetFullPath(configured);
        }

        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return Path.Combine(localAppData, "IIoT.Edge", "design", "edge_design.db");
    }

    private static string? FirstNonEmpty(params string?[] values)
        => values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim();

    private static void ExecutePragma(SqliteConnection connection, string pragma)
    {
        using var command = connection.CreateCommand();
        command.CommandText = pragma;
        command.ExecuteNonQuery();
    }
}
