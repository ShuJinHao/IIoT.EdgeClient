using Microsoft.Data.Sqlite;
using System.Data;

namespace IIoT.Edge.Infrastructure.Persistence.Dapper.Connection;

/// <summary>
/// SQLite connection factory for Edge local persistence databases.
/// Each created connection is opened immediately and applies the shared runtime pragmas.
/// </summary>
public class SqliteConnectionFactory
{
    private const int BusyTimeoutMs = 5000;

    private readonly string _dbDirectory;

    public string DbDirectory => _dbDirectory;

    public SqliteConnectionFactory(string dbDirectory)
    {
        if (string.IsNullOrWhiteSpace(dbDirectory))
        {
            throw new ArgumentNullException(nameof(dbDirectory));
        }

        _dbDirectory = dbDirectory;
        Directory.CreateDirectory(_dbDirectory);
    }

    public IDbConnection Create(string dbName)
    {
        var connection = new SqliteConnection(BuildConnectionString(dbName));
        connection.Open();
        ApplyPragmas(connection);
        return connection;
    }

    public async Task<IDbConnection> CreateAsync(string dbName)
    {
        var connection = new SqliteConnection(BuildConnectionString(dbName));
        await connection.OpenAsync().ConfigureAwait(false);
        await ApplyPragmasAsync(connection).ConfigureAwait(false);
        return connection;
    }

    public string GetDbPath(string dbName)
        => Path.Combine(_dbDirectory, $"{dbName}.db");

    private string BuildConnectionString(string dbName)
    {
        return new SqliteConnectionStringBuilder
        {
            DataSource = GetDbPath(dbName),
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared
        }.ToString();
    }

    private static void ApplyPragmas(SqliteConnection connection)
    {
        ExecutePragma(connection, "PRAGMA journal_mode=WAL;");
        ExecutePragma(connection, "PRAGMA foreign_keys=ON;");
        ExecutePragma(connection, $"PRAGMA busy_timeout={BusyTimeoutMs};");
    }

    private static async Task ApplyPragmasAsync(SqliteConnection connection)
    {
        await ExecutePragmaAsync(connection, "PRAGMA journal_mode=WAL;").ConfigureAwait(false);
        await ExecutePragmaAsync(connection, "PRAGMA foreign_keys=ON;").ConfigureAwait(false);
        await ExecutePragmaAsync(connection, $"PRAGMA busy_timeout={BusyTimeoutMs};").ConfigureAwait(false);
    }

    private static void ExecutePragma(SqliteConnection connection, string pragma)
    {
        using var command = connection.CreateCommand();
        command.CommandText = pragma;
        command.ExecuteNonQuery();
    }

    private static async Task ExecutePragmaAsync(SqliteConnection connection, string pragma)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = pragma;
        await command.ExecuteNonQueryAsync().ConfigureAwait(false);
    }
}
