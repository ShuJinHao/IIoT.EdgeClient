using IIoT.Edge.Infrastructure.Persistence.Dapper.Connection;
using IIoT.Edge.Infrastructure.Persistence.Dapper.Stores;
using IIoT.Edge.Module.Contracts.Production;
using Microsoft.Data.Sqlite;

namespace IIoT.Edge.Persistence.Tests;

public sealed class ModuleProductionRecordPersistenceBehaviorTests
{
    private static readonly DateTime DayStartUtc =
        new(2026, 7, 24, 16, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task ApAndCp_ShouldPersistInIndependentDatabasesAndSurviveRestart()
    {
        var tempDirectory = CreateTempDirectory();
        try
        {
            var first = CreateStore(tempDirectory);
            var ap = CreateEntry(
                "AP",
                "AP-IDEMPOTENCY",
                "P1-AP01",
                "负极模切01",
                "AP.ClipScan.MG1",
                "MG1",
                "AP-CLIP-001",
                DayStartUtc.AddMinutes(2),
                quantity: 12);
            var cp = CreateEntry(
                "CP",
                "CP-IDEMPOTENCY",
                "P2-CP01",
                "正极模切01",
                "CP.ClipScan.MG2",
                "MG2",
                "CP-CLIP-001",
                DayStartUtc.AddMinutes(3),
                quantity: 18);

            Assert.True(await first.AddAsync(ap, TestContext.Current.CancellationToken));
            Assert.False(await first.AddAsync(ap, TestContext.Current.CancellationToken));
            Assert.True(await first.AddAsync(cp, TestContext.Current.CancellationToken));
            Assert.True(File.Exists(Path.Combine(tempDirectory, "ap_production.db")));
            Assert.True(File.Exists(Path.Combine(tempDirectory, "cp_production.db")));

            var restarted = CreateStore(tempDirectory);
            var apRows = await restarted.QueryAsync(
                new ModuleProductionRecordQuery(
                    "AP",
                    DayStartUtc,
                    DayStartUtc.AddDays(1),
                    "__all__"),
                TestContext.Current.CancellationToken);
            var cpRows = await restarted.QueryAsync(
                new ModuleProductionRecordQuery(
                    "CP",
                    DayStartUtc,
                    DayStartUtc.AddDays(1),
                    "__all__"),
                TestContext.Current.CancellationToken);

            Assert.Equal("AP-CLIP-001", Assert.Single(apRows).RecordCode);
            Assert.Equal("CP-CLIP-001", Assert.Single(cpRows).RecordCode);
        }
        finally
        {
            DeleteTempDirectory(tempDirectory);
        }
    }

    [Fact]
    public async Task Query_ShouldUseProductionDayDeviceFilterNewestOrderAndFiveHundredRowLimit()
    {
        var tempDirectory = CreateTempDirectory();
        try
        {
            var store = CreateStore(tempDirectory);
            await store.AddAsync(
                CreateEntry(
                    "AP",
                    "PREVIOUS-DAY",
                    "P1-AP01",
                    "负极模切01",
                    "AP.ClipScan.MG1",
                    "MG1",
                    "OLD",
                    DayStartUtc.AddTicks(-1),
                    quantity: 99),
                TestContext.Current.CancellationToken);

            for (var index = 0; index < 510; index++)
            {
                var deviceNumber = index % 2 + 1;
                await store.AddAsync(
                    CreateEntry(
                        "AP",
                        $"CURRENT-{index:D3}",
                        $"P1-AP{deviceNumber:D2}",
                        $"负极模切{deviceNumber:D2}",
                        index % 2 == 0 ? "AP.ClipScan.MG1" : "AP.ClipScan.MG2",
                        index % 2 == 0 ? "MG1" : "MG2",
                        $"CLIP-{index:D3}",
                        DayStartUtc.AddMinutes(index),
                        quantity: 1),
                    TestContext.Current.CancellationToken);
            }

            var allRows = await store.QueryAsync(
                new ModuleProductionRecordQuery(
                    "AP",
                    DayStartUtc,
                    DayStartUtc.AddDays(1),
                    "__all__",
                    500),
                TestContext.Current.CancellationToken);
            var selectedRows = await store.QueryAsync(
                new ModuleProductionRecordQuery(
                    "AP",
                    DayStartUtc,
                    DayStartUtc.AddDays(1),
                    "负极模切01",
                    500),
                TestContext.Current.CancellationToken);

            Assert.Equal(500, allRows.Count);
            Assert.Equal("CLIP-509", allRows[0].RecordCode);
            Assert.DoesNotContain(allRows, row => row.RecordCode == "OLD");
            Assert.Equal(255, selectedRows.Count);
            Assert.All(selectedRows, row => Assert.Equal("负极模切01", row.DeviceName));
        }
        finally
        {
            DeleteTempDirectory(tempDirectory);
        }
    }

    [Fact]
    public async Task Summary_ShouldAggregateOnlyPersistedRowsWithinCurrentDayAndRecentWindow()
    {
        var tempDirectory = CreateTempDirectory();
        try
        {
            var store = CreateStore(tempDirectory);
            await store.AddAsync(
                CreateEntry(
                    "CP",
                    "CP-OK",
                    "P2-CP01",
                    "正极模切01",
                    "CP.ClipScan.MG1",
                    "MG1",
                    "CP-OK",
                    DayStartUtc.AddHours(10),
                    quantity: 20,
                    isOk: true,
                    mainPlanCode: "PLAN-20260725"),
                TestContext.Current.CancellationToken);
            await store.AddAsync(
                CreateEntry(
                    "CP",
                    "CP-NG",
                    "P2-CP01",
                    "正极模切01",
                    "CP.ClipScan.MG2",
                    "MG2",
                    "CP-NG",
                    DayStartUtc.AddHours(11).AddMinutes(30),
                    quantity: 3,
                    isOk: false),
                TestContext.Current.CancellationToken);

            var summary = await store.QuerySummaryAsync(
                new ModuleProductionRecordSummaryPersistenceQuery(
                    "CP",
                    DayStartUtc,
                    DayStartUtc.AddDays(1),
                    DayStartUtc.AddHours(11),
                    "P2-CP01"),
                TestContext.Current.CancellationToken);

            Assert.Equal(23, summary.TodayOutput);
            Assert.Equal(20, summary.TodayOk);
            Assert.Equal(3, summary.TodayNg);
            Assert.Equal(3, summary.RecentOutput);
            Assert.Equal(0, summary.RecentOk);
            Assert.Equal(3, summary.RecentNg);
            Assert.Equal("CP-NG", summary.CurrentBatch);
        }
        finally
        {
            DeleteTempDirectory(tempDirectory);
        }
    }

    private static ModuleProductionRecordPersistence CreateStore(string tempDirectory)
        => new(
            new SqliteConnectionFactory(tempDirectory),
            new FakeLogService());

    private static ModuleProductionRecordEntry CreateEntry(
        string moduleId,
        string idempotencyKey,
        string deviceCode,
        string deviceName,
        string taskKey,
        string slot,
        string recordCode,
        DateTime completedAtUtc,
        int quantity,
        bool isOk = true,
        string mainPlanCode = "")
        => new(
            0,
            idempotencyKey,
            moduleId,
            deviceCode,
            deviceName,
            taskKey,
            slot,
            recordCode,
            mainPlanCode,
            string.Empty,
            quantity,
            1.25m,
            completedAtUtc.AddMinutes(-5),
            completedAtUtc,
            completedAtUtc.AddSeconds(-1),
            completedAtUtc,
            isOk);

    private static string CreateTempDirectory()
    {
        var path = Path.Combine(
            Path.GetTempPath(),
            "iiot-module-production-record-tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static void DeleteTempDirectory(string path)
    {
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive: true);
        }
    }
}
