using Dapper;
using IIoT.Edge.Application.Abstractions.Logging;
using IIoT.Edge.Infrastructure.Persistence.Dapper.Connection;
using IIoT.Edge.Infrastructure.Persistence.Dapper.Repository;
using System.Data;

namespace IIoT.Edge.Infrastructure.Persistence.Dapper.Stores;

public abstract class ClaimBufferStoreBase<TEntity> : DapperRepositoryBase<TEntity>
    where TEntity : class
{
    protected static readonly TimeSpan DefaultClaimTimeout = TimeSpan.FromMinutes(10);

    protected ClaimBufferStoreBase(
        SqliteConnectionFactory connectionFactory,
        ILogService logger)
        : base(connectionFactory, logger)
    {
    }

    protected Task DeleteExpiredClaimsAsync(
        IDbConnection connection,
        IDbTransaction transaction,
        string claimTableName,
        DateTime nowUtc,
        TimeSpan? claimTimeout = null)
    {
        return connection.ExecuteAsync(
            $"DELETE FROM {claimTableName} WHERE ClaimedAt <= @ExpiredAt",
            new
            {
                ExpiredAt = nowUtc.Subtract(claimTimeout ?? DefaultClaimTimeout).ToString("O")
            },
            transaction,
            commandTimeout: CommandTimeout);
    }

    protected Task InsertClaimRowsAsync(
        IDbConnection connection,
        IDbTransaction transaction,
        string claimTableName,
        IEnumerable<long> ids,
        string claimToken,
        DateTime nowUtc)
    {
        var claimRows = ids.Select(id => new
        {
            RecordId = id,
            ClaimToken = claimToken,
            ClaimedAt = nowUtc.ToString("O")
        });

        return connection.ExecuteAsync(
            $"INSERT INTO {claimTableName} (RecordId, ClaimToken, ClaimedAt) VALUES (@RecordId, @ClaimToken, @ClaimedAt)",
            claimRows,
            transaction,
            commandTimeout: CommandTimeout);
    }

    protected Task<TBatch?> ClaimBatchCoreAsync<TPayload, TBatch>(
        string claimTableName,
        int batchSize,
        Func<IDbConnection, IDbTransaction, int, DateTime, Task<List<long>>> selectPendingIdsAsync,
        Func<IDbConnection, IDbTransaction, string, Task<List<TPayload>>> selectClaimedPayloadAsync,
        Func<string, List<TPayload>, TBatch> createBatch)
        where TBatch : class
    {
        return ExecuteInTransactionAsync<TBatch?>(async (connection, transaction) =>
        {
            var nowUtc = DateTime.UtcNow;
            await DeleteExpiredClaimsAsync(connection, transaction, claimTableName, nowUtc).ConfigureAwait(false);

            var ids = await selectPendingIdsAsync(connection, transaction, batchSize, nowUtc).ConfigureAwait(false);
            if (ids.Count == 0)
            {
                return null;
            }

            var claimToken = Guid.NewGuid().ToString("N");
            await InsertClaimRowsAsync(connection, transaction, claimTableName, ids, claimToken, nowUtc).ConfigureAwait(false);

            var payload = await selectClaimedPayloadAsync(connection, transaction, claimToken).ConfigureAwait(false);
            if (payload.Count == 0)
            {
                await DeleteClaimRowsByTokenAsync(connection, transaction, claimTableName, claimToken).ConfigureAwait(false);
                return null;
            }

            return createBatch(claimToken, payload);
        });
    }

    protected async Task<List<long>> SelectUnclaimedIdsByIdAscendingAsync(
        IDbConnection connection,
        IDbTransaction transaction,
        string claimTableName,
        int batchSize)
    {
        return (await connection.QueryAsync<long>(
            $@"
                SELECT b.Id
                FROM {TableName} b
                LEFT JOIN {claimTableName} c ON c.RecordId = b.Id
                WHERE c.RecordId IS NULL
                ORDER BY b.Id ASC
                LIMIT @BatchSize",
            new { BatchSize = batchSize },
            transaction,
            commandTimeout: CommandTimeout).ConfigureAwait(false)).ToList();
    }

    protected async Task<List<long>> GetClaimedIdsAsync(
        IDbConnection connection,
        IDbTransaction transaction,
        string claimTableName,
        string claimToken)
    {
        return (await connection.QueryAsync<long>(
            $"SELECT RecordId FROM {claimTableName} WHERE ClaimToken = @ClaimToken",
            new { ClaimToken = claimToken },
            transaction,
            commandTimeout: CommandTimeout).ConfigureAwait(false)).ToList();
    }

    protected Task DeleteClaimRowsByIdsAsync(
        IDbConnection connection,
        IDbTransaction transaction,
        string claimTableName,
        IEnumerable<long> ids)
    {
        return connection.ExecuteAsync(
            $"DELETE FROM {claimTableName} WHERE RecordId IN @Ids",
            new { Ids = ids.ToList() },
            transaction,
            commandTimeout: CommandTimeout);
    }

    protected Task DeleteClaimRowsByTokenAsync(
        IDbConnection connection,
        IDbTransaction transaction,
        string claimTableName,
        string claimToken)
    {
        return connection.ExecuteAsync(
            $"DELETE FROM {claimTableName} WHERE ClaimToken = @ClaimToken",
            new { ClaimToken = claimToken },
            transaction,
            commandTimeout: CommandTimeout);
    }

    protected async Task DeleteRowsByIdsInChunksAsync(IEnumerable<long> ids, int chunkSize = 500)
    {
        var idList = ids.ToList();
        if (idList.Count == 0)
        {
            return;
        }

        foreach (var batch in ChunkBy(idList, chunkSize))
        {
            await SafeExecuteAsync($"DELETE FROM {TableName} WHERE Id IN @Ids", new { Ids = batch }).ConfigureAwait(false);
        }
    }

    protected Task DeleteClaimedRowsByClaimAsync(
        string claimTableName,
        string claimToken,
        string noRowsMessage)
    {
        return ExecuteInTransactionAsync<int>(async (connection, transaction) =>
        {
            var ids = await GetClaimedIdsAsync(connection, transaction, claimTableName, claimToken).ConfigureAwait(false);
            if (ids.Count == 0)
            {
                throw new InvalidOperationException(noRowsMessage);
            }

            await connection.ExecuteAsync(
                $"DELETE FROM {TableName} WHERE Id IN @Ids",
                new { Ids = ids },
                transaction,
                commandTimeout: CommandTimeout).ConfigureAwait(false);

            await DeleteClaimRowsByIdsAsync(connection, transaction, claimTableName, ids).ConfigureAwait(false);

            return ids.Count;
        });
    }

    protected Task ReleaseClaimCoreAsync(
        string claimTableName,
        string claimToken,
        string failureMessage)
    {
        return StrictExecuteAsync(
            $"DELETE FROM {claimTableName} WHERE ClaimToken = @ClaimToken",
            new { ClaimToken = claimToken },
            requireAffectedRows: true,
            failureMessage: failureMessage);
    }

    private static IEnumerable<List<T>> ChunkBy<T>(List<T> source, int chunkSize)
    {
        for (var i = 0; i < source.Count; i += chunkSize)
        {
            yield return source.GetRange(i, Math.Min(chunkSize, source.Count - i));
        }
    }
}
