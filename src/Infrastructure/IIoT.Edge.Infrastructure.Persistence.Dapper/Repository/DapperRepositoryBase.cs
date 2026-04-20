using Dapper;
using IIoT.Edge.Application.Abstractions.Logging;
using IIoT.Edge.Application.Common.Persistence;
using IIoT.Edge.Infrastructure.Persistence.Dapper.Connection;
using Microsoft.Data.Sqlite;
using System.Data;

namespace IIoT.Edge.Infrastructure.Persistence.Dapper.Repository;

public abstract class DapperRepositoryBase<TEntity> : ITableInitializer
    where TEntity : class
{
    private static readonly int[] WriteRetryDelaysMs = [50, 150, 400];

    protected readonly SqliteConnectionFactory ConnectionFactory;
    protected readonly ILogService Logger;

    protected abstract string TableName { get; }

    public abstract string DbName { get; }

    protected virtual int CommandTimeout => 30;

    protected DapperRepositoryBase(
        SqliteConnectionFactory connectionFactory,
        ILogService logger)
    {
        ConnectionFactory = connectionFactory;
        Logger = logger;
    }

    protected abstract string CreateTableSql { get; }

    public async Task InitializeTableAsync(IDbConnection connection)
    {
        try
        {
            await connection.ExecuteAsync(CreateTableSql).ConfigureAwait(false);
            Logger.Info($"[Dapper] 表 {TableName} 初始化完成（{DbName}.db）");
        }
        catch (Exception ex)
        {
            throw LogAndWrapAccessFailure("表初始化失败", ex);
        }
    }

    protected IDbConnection GetConnection() => ConnectionFactory.Create(DbName);

    protected Task<IDbConnection> GetConnectionAsync() => ConnectionFactory.CreateAsync(DbName);

    public async Task<TEntity?> GetByIdAsync(long id)
    {
        var sql = $"SELECT * FROM {TableName} WHERE Id = @Id";
        return await SafeQueryFirstOrDefaultAsync(sql, new { Id = id }).ConfigureAwait(false);
    }

    protected Task<IEnumerable<TEntity>> SafeQueryAsync(string sql, object? param = null)
    {
        return ExecuteReadAsync(
            "查询失败",
            connection => connection.QueryAsync<TEntity>(sql, param, commandTimeout: CommandTimeout));
    }

    protected Task<List<T>> SafeQueryListAsync<T>(string sql, object? param = null)
    {
        return ExecuteReadAsync(
            "查询失败",
            async connection => (await connection.QueryAsync<T>(sql, param, commandTimeout: CommandTimeout).ConfigureAwait(false)).ToList());
    }

    protected Task<TEntity?> SafeQueryFirstOrDefaultAsync(string sql, object? param = null)
    {
        return ExecuteReadAsync(
            "查询失败",
            connection => connection.QueryFirstOrDefaultAsync<TEntity>(sql, param, commandTimeout: CommandTimeout));
    }

    protected Task<int> SafeCountAsync(string sql, object? param = null)
    {
        return ExecuteReadAsync(
            "计数失败",
            connection => connection.ExecuteScalarAsync<int>(sql, param, commandTimeout: CommandTimeout));
    }

    protected Task<int> SafeExecuteAsync(string sql, object? param = null)
    {
        return ExecuteWriteAsync(
            "执行失败",
            connection => connection.ExecuteAsync(sql, param, commandTimeout: CommandTimeout));
    }

    protected async Task<int> StrictExecuteAsync(
        string sql,
        object? param = null,
        bool requireAffectedRows = false,
        string? failureMessage = null)
    {
        var affectedRows = await SafeExecuteAsync(sql, param).ConfigureAwait(false);
        if (requireAffectedRows && affectedRows <= 0)
        {
            throw new InvalidOperationException(
                failureMessage ?? $"No rows were affected while executing a critical command on {TableName}.");
        }

        return affectedRows;
    }

    protected Task<long> SafeInsertReturnIdAsync(string sql, object param)
    {
        return ExecuteWriteAsync(
            "插入失败",
            async connection =>
            {
                var fullSql = $"{sql}; SELECT last_insert_rowid();";
                return await connection.ExecuteScalarAsync<long>(fullSql, param, commandTimeout: CommandTimeout).ConfigureAwait(false);
            });
    }

    public Task<int> DeleteByIdAsync(long id)
    {
        var sql = $"DELETE FROM {TableName} WHERE Id = @Id";
        return SafeExecuteAsync(sql, new { Id = id });
    }

    protected async Task<T> ExecuteInTransactionAsync<T>(Func<IDbConnection, IDbTransaction, Task<T>> action)
    {
        for (var attempt = 0; ; attempt++)
        {
            using var connection = GetConnection();
            using var transaction = connection.BeginTransaction();

            try
            {
                var result = await action(connection, transaction).ConfigureAwait(false);
                transaction.Commit();
                return result;
            }
            catch (SqliteException ex) when (IsBusyOrLocked(ex) && attempt < WriteRetryDelaysMs.Length)
            {
                SafeRollback(transaction);
                await DelayForRetryAsync(ex, attempt).ConfigureAwait(false);
            }
            catch (SqliteException ex)
            {
                SafeRollback(transaction);
                throw LogAndWrapAccessFailure("事务执行失败", ex);
            }
            catch
            {
                SafeRollback(transaction);
                throw;
            }
        }
    }

    private async Task<T> ExecuteReadAsync<T>(string operation, Func<IDbConnection, Task<T>> action)
    {
        try
        {
            using var connection = GetConnection();
            return await action(connection).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            throw LogAndWrapAccessFailure(operation, ex);
        }
    }

    private async Task<T> ExecuteWriteAsync<T>(string operation, Func<IDbConnection, Task<T>> action)
    {
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                using var connection = GetConnection();
                return await action(connection).ConfigureAwait(false);
            }
            catch (SqliteException ex) when (IsBusyOrLocked(ex) && attempt < WriteRetryDelaysMs.Length)
            {
                await DelayForRetryAsync(ex, attempt).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                throw LogAndWrapAccessFailure(operation, ex);
            }
        }
    }

    private async Task DelayForRetryAsync(SqliteException ex, int attempt)
    {
        var delayMs = WriteRetryDelaysMs[attempt];
        Logger.Warn(
            $"[Dapper] 写入遇到 busy/locked，准备重试 [{TableName}] ({DbName}.db) - attempt {attempt + 1}/{WriteRetryDelaysMs.Length}, delay {delayMs}ms, sqlite={ex.SqliteErrorCode}");
        await Task.Delay(delayMs).ConfigureAwait(false);
    }

    private PersistenceAccessException LogAndWrapAccessFailure(string operation, Exception ex)
    {
        if (ex is PersistenceAccessException accessException)
        {
            return accessException;
        }

        var message = $"[Dapper] {operation} [{TableName}] ({DbName}.db): {ex.Message}";
        Logger.Error(message);
        return new PersistenceAccessException(message, ex);
    }

    private static bool IsBusyOrLocked(SqliteException ex)
        => ex.SqliteErrorCode is 5 or 6;

    private static void SafeRollback(IDbTransaction transaction)
    {
        try
        {
            transaction.Rollback();
        }
        catch
        {
        }
    }
}
