using Dapper;
using IIoT.Edge.Application.Abstractions.DataPipeline.Stores;
using IIoT.Edge.Application.Abstractions.Logging;
using IIoT.Edge.Infrastructure.Persistence.Dapper.Connection;
using IIoT.Edge.SharedKernel.DataPipeline.Capacity;

namespace IIoT.Edge.Infrastructure.Persistence.Dapper.Stores;

public class CapacityBufferStore : ClaimBufferStoreBase<CapacityRecord>, ICapacityBufferStore
{
    private const string ClaimTableName = "capacity_buffer_claims";
    private const string InsertCapacitySql = @"
            INSERT INTO capacity_buffer
                (Barcode, CellResult, ShiftCode, CompletedTime, CreatedAt, PlcName)
            VALUES
                (@Barcode, @CellResult, @ShiftCode, @CompletedTime, @CreatedAt, @PlcName)";

    public override string DbName => "pipeline_cloud";
    protected override string TableName => "capacity_buffer";

    protected override string CreateTableSql => @"
        CREATE TABLE IF NOT EXISTS capacity_buffer (
            Id            INTEGER PRIMARY KEY AUTOINCREMENT,
            Barcode       TEXT    NOT NULL,
            CellResult    INTEGER NOT NULL,
            ShiftCode     TEXT    NOT NULL,
            CompletedTime TEXT    NOT NULL,
            CreatedAt     TEXT    NOT NULL,
            PlcName       TEXT    NOT NULL DEFAULT ''
        );
        CREATE INDEX IF NOT EXISTS idx_buffer_completed
            ON capacity_buffer (CompletedTime);
        CREATE INDEX IF NOT EXISTS idx_buffer_plcname
            ON capacity_buffer (PlcName);

        CREATE TABLE IF NOT EXISTS capacity_buffer_claims (
            RecordId    INTEGER PRIMARY KEY,
            ClaimToken  TEXT    NOT NULL,
            ClaimedAt   TEXT    NOT NULL
        );
        CREATE INDEX IF NOT EXISTS idx_capacity_buffer_claim_token
            ON capacity_buffer_claims (ClaimToken);
        CREATE INDEX IF NOT EXISTS idx_capacity_buffer_claim_time
            ON capacity_buffer_claims (ClaimedAt);
    ";

    public CapacityBufferStore(
        SqliteConnectionFactory connectionFactory,
        ILogService logger)
        : base(connectionFactory, logger)
    {
    }

    public async Task SaveAsync(CapacityRecord record)
    {
        var createdAt = DateTime.UtcNow.ToString("O");
        await SafeExecuteAsync(InsertCapacitySql, CreateInsertRow(record, createdAt));
    }

    public async Task SaveBatchAsync(IEnumerable<CapacityRecord> records)
    {
        var now = DateTime.UtcNow.ToString("O");
        var rows = records.Select(r => CreateInsertRow(r, now)).ToList();

        if (rows.Count == 0)
        {
            return;
        }

        await ExecuteInTransactionAsync<int>(async (conn, tx) =>
        {
            await conn.ExecuteAsync(InsertCapacitySql, rows, tx, commandTimeout: CommandTimeout);
            return rows.Count;
        });

        Logger.Info($"[CapacityBuffer] Batch saved: {rows.Count} row(s).");
    }

    private static object CreateInsertRow(CapacityRecord record, string createdAt)
    {
        return new
        {
            record.Barcode,
            record.CellResult,
            record.ShiftCode,
            CompletedTime = record.CompletedTime.ToString("O"),
            CreatedAt = createdAt,
            record.PlcName
        };
    }

    public async Task<List<BufferHourlySummaryDto>> GetHourlySummaryAsync()
    {
        return await SafeQueryListAsync<BufferHourlySummaryDto>(
            BuildHourlySummarySql(
                "FROM capacity_buffer",
                completedTimeColumn: "CompletedTime",
                shiftCodeColumn: "ShiftCode",
                plcNameColumn: "PlcName",
                cellResultColumn: "CellResult",
                orderShiftCodeColumn: "ShiftCode"));
    }

    public async Task<ClaimedCapacityBufferBatch?> ClaimHourlySummaryBatchAsync(int batchSize = 200)
    {
        return await ClaimBatchCoreAsync<BufferHourlySummaryDto, ClaimedCapacityBufferBatch>(
            ClaimTableName,
            batchSize,
            (conn, tx, size, _) => SelectUnclaimedIdsByIdAscendingAsync(conn, tx, ClaimTableName, size),
            async (conn, tx, claimToken) => (await conn.QueryAsync<BufferHourlySummaryDto>(
                BuildHourlySummarySql(
                    @"FROM capacity_buffer b
                INNER JOIN capacity_buffer_claims c ON c.RecordId = b.Id",
                    completedTimeColumn: "b.CompletedTime",
                    shiftCodeColumn: "b.ShiftCode",
                    plcNameColumn: "b.PlcName",
                    cellResultColumn: "b.CellResult",
                    orderShiftCodeColumn: "b.ShiftCode",
                    whereClause: "c.ClaimToken = @ClaimToken"),
                new { ClaimToken = claimToken },
                tx,
                commandTimeout: CommandTimeout)).ToList(),
            (claimToken, summaries) => new ClaimedCapacityBufferBatch
            {
                ClaimToken = claimToken,
                Summaries = summaries
            }).ConfigureAwait(false);
    }

    public async Task DeleteClaimedSummaryAsync(string claimToken, string date, int hour, int minuteBucket, string shiftCode, string plcName)
    {
        await ExecuteInTransactionAsync<int>(async (conn, tx) =>
        {
            var ids = (await conn.QueryAsync<long>(
                @"
                SELECT b.Id
                FROM capacity_buffer b
                INNER JOIN capacity_buffer_claims c ON c.RecordId = b.Id
                WHERE c.ClaimToken = @ClaimToken
                  AND " + CapacityDateExpression("b.CompletedTime") + @" = @Date
                  AND " + CapacityHourExpression("b.CompletedTime") + @" = @Hour
                  AND " + CapacityMinuteBucketExpression("b.CompletedTime") + @" = @MinuteBucket
                  AND b.ShiftCode = @ShiftCode
                  AND b.PlcName = @PlcName",
                new
                {
                    ClaimToken = claimToken,
                    Date = date,
                    Hour = hour,
                    MinuteBucket = minuteBucket,
                    ShiftCode = shiftCode,
                    PlcName = plcName
                },
                tx,
                commandTimeout: CommandTimeout)).ToList();

            if (ids.Count == 0)
            {
                throw new InvalidOperationException($"No claimed capacity rows found for claim {claimToken}.");
            }

            await conn.ExecuteAsync(
                "DELETE FROM capacity_buffer WHERE Id IN @Ids",
                new { Ids = ids },
                tx,
                commandTimeout: CommandTimeout);

            await DeleteClaimRowsByIdsAsync(conn, tx, ClaimTableName, ids).ConfigureAwait(false);

            return ids.Count;
        });
    }

    public async Task ReleaseClaimAsync(string claimToken)
        => await ReleaseClaimCoreAsync(
            ClaimTableName,
            claimToken,
            $"Failed to release capacity claim {claimToken}.");

    public async Task ClearAllAsync()
    {
        await SafeExecuteAsync("DELETE FROM capacity_buffer_claims");
        await SafeExecuteAsync($"DELETE FROM {TableName}");
    }

    public async Task<int> GetCountAsync()
        => await SafeCountAsync($"SELECT COUNT(*) FROM {TableName}");

    private static string BuildHourlySummarySql(
        string fromClause,
        string completedTimeColumn,
        string shiftCodeColumn,
        string plcNameColumn,
        string cellResultColumn,
        string orderShiftCodeColumn,
        string? whereClause = null)
    {
        var dateExpression = CapacityDateExpression(completedTimeColumn);
        var hourExpression = CapacityHourExpression(completedTimeColumn);
        var minuteBucketExpression = CapacityMinuteBucketExpression(completedTimeColumn);
        var whereSql = string.IsNullOrWhiteSpace(whereClause)
            ? string.Empty
            : $@"
                WHERE {whereClause}";

        return $@"
                SELECT
                    {dateExpression}                                    AS Date,
                    {hourExpression}                                    AS Hour,
                    {minuteBucketExpression}                            AS MinuteBucket,
                    {shiftCodeColumn}                                   AS ShiftCode,
                    {plcNameColumn}                                     AS PlcName,
                    COUNT(*)                                            AS Total,
                    SUM(CASE WHEN {cellResultColumn} = 1 THEN 1 ELSE 0 END) AS OkCount,
                    SUM(CASE WHEN {cellResultColumn} = 0 THEN 1 ELSE 0 END) AS NgCount
                {fromClause}{whereSql}
                GROUP BY
                    {dateExpression},
                    {hourExpression},
                    {minuteBucketExpression},
                    {shiftCodeColumn},
                    {plcNameColumn}
                ORDER BY Date ASC, Hour ASC, MinuteBucket ASC, {orderShiftCodeColumn} ASC";
    }

    private static string CapacityDateExpression(string completedTimeColumn)
        => $"substr({completedTimeColumn}, 1, 10)";

    private static string CapacityHourExpression(string completedTimeColumn)
        => $"CAST(substr({completedTimeColumn}, 12, 2) AS INTEGER)";

    private static string CapacityMinuteBucketExpression(string completedTimeColumn)
        => $@"CASE
                        WHEN CAST(substr({completedTimeColumn}, 15, 2) AS INTEGER) >= 30
                        THEN 30 ELSE 0
                    END";
}
