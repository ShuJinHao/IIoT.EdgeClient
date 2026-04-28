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
}
