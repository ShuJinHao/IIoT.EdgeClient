using System.Linq.Expressions;
using IIoT.Edge.Domain.Hardware.Aggregates;
using IIoT.Edge.Infrastructure.Persistence.Dapper.Connection;
using IIoT.Edge.Infrastructure.Persistence.Dapper.Stores;
using IIoT.Edge.Module.Contracts.Hardware;
using IIoT.Edge.Module.Contracts.Production;
using IIoT.Edge.SharedKernel.Repository;
using IIoT.Edge.SharedKernel.Specification;
using Microsoft.Data.Sqlite;
using Dapper;

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

            var apRow = Assert.Single(apRows);
            var cpRow = Assert.Single(cpRows);
            Assert.Equal("AP-CLIP-001", apRow.RecordCode);
            Assert.Equal("P1-AP01", apRow.PlcCode);
            Assert.Equal("CP-CLIP-001", cpRow.RecordCode);
            Assert.Equal("P2-CP01", cpRow.PlcCode);
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
                    "P1-AP01",
                    500),
                TestContext.Current.CancellationToken);

            Assert.Equal(500, allRows.Count);
            Assert.Equal("CLIP-509", allRows[0].RecordCode);
            Assert.DoesNotContain(allRows, row => row.RecordCode == "OLD");
            Assert.Equal(255, selectedRows.Count);
            Assert.All(selectedRows, row => Assert.Equal("P1-AP01", row.PlcCode));
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

    [Fact]
    public async Task HistoricalTable_ShouldAddPlcCodeAndBackfillFromStableDeviceCode()
    {
        var tempDirectory = CreateTempDirectory();
        try
        {
            var factory = new SqliteConnectionFactory(tempDirectory);
            using (var connection = factory.Create("ap_production"))
            {
                await connection.ExecuteAsync(
                    """
                    CREATE TABLE module_production_records
                    (
                        Id INTEGER PRIMARY KEY AUTOINCREMENT,
                        IdempotencyKey TEXT NOT NULL UNIQUE,
                        ModuleId TEXT NOT NULL,
                        DeviceCode TEXT NOT NULL,
                        DeviceName TEXT NOT NULL,
                        TaskKey TEXT NOT NULL,
                        SlotKey TEXT NOT NULL,
                        RecordCode TEXT NOT NULL,
                        MainPlanCode TEXT NOT NULL,
                        TraceBatchNumber TEXT NOT NULL,
                        Quantity INTEGER NOT NULL,
                        Speed REAL NOT NULL,
                        StartedAtUtc TEXT NOT NULL,
                        CompletedAtUtc TEXT NOT NULL,
                        QueueCreatedAtUtc TEXT NOT NULL,
                        QueueProcessedAtUtc TEXT NOT NULL,
                        IsOk INTEGER NOT NULL
                    );
                    INSERT INTO module_production_records
                    (
                        IdempotencyKey, ModuleId, DeviceCode, DeviceName, TaskKey,
                        SlotKey, RecordCode, MainPlanCode, TraceBatchNumber,
                        Quantity, Speed, StartedAtUtc, CompletedAtUtc,
                        QueueCreatedAtUtc, QueueProcessedAtUtc, IsOk
                    )
                    VALUES
                    (
                        'LEGACY-AP', 'AP', 'P1-AP09', '旧显示名称', 'AP.ClipScan.MG1',
                        'MG1', 'LEGACY-CLIP', '', '',
                        1, 1.0, @StartedAtUtc, @CompletedAtUtc,
                        @StartedAtUtc, @CompletedAtUtc, 1
                    );
                    """,
                    new
                    {
                        StartedAtUtc = DayStartUtc.AddMinutes(1).ToString("O"),
                        CompletedAtUtc = DayStartUtc.AddMinutes(2).ToString("O")
                    });
            }

            var store = CreateStore(tempDirectory);
            var rows = await store.QueryAsync(
                new ModuleProductionRecordQuery(
                    "AP",
                    DayStartUtc,
                    DayStartUtc.AddDays(1),
                    "P1-AP09"),
                TestContext.Current.CancellationToken);

            var legacy = Assert.Single(rows);
            Assert.Equal("P1-AP09", legacy.DeviceCode);
            Assert.Equal("P1-AP09", legacy.PlcCode);
            Assert.Equal("旧显示名称", legacy.DeviceName);
        }
        finally
        {
            DeleteTempDirectory(tempDirectory);
        }
    }

    [Fact]
    public async Task Query_WhenDeviceWasRenamed_ShouldResolveDisplaySelectionToStablePlcCode()
    {
        var tempDirectory = CreateTempDirectory();
        try
        {
            var device = NetworkDeviceEntity.Create(
                    "当前显示名称",
                    DeviceType.PLC,
                    "127.0.0.1",
                    6001,
                    "P1-AP01")
                .WithId(8);
            var store = CreateStore(tempDirectory, [device]);
            await store.AddAsync(
                CreateEntry(
                    "AP",
                    "RENAMED-DEVICE",
                    "P1-AP01",
                    "旧显示名称",
                    "AP.ClipScan.MG1",
                    "MG1",
                    "RENAMED-CLIP",
                    DayStartUtc.AddMinutes(2),
                    quantity: 3),
                TestContext.Current.CancellationToken);

            var rows = await store.QueryAsync(
                new ModuleProductionRecordQuery(
                    "AP",
                    DayStartUtc,
                    DayStartUtc.AddDays(1),
                    "当前显示名称"),
                TestContext.Current.CancellationToken);

            var row = Assert.Single(rows);
            Assert.Equal("P1-AP01", row.PlcCode);
            Assert.Equal("旧显示名称", row.DeviceName);
        }
        finally
        {
            DeleteTempDirectory(tempDirectory);
        }
    }

    private static ModuleProductionRecordPersistence CreateStore(
        string tempDirectory,
        IReadOnlyCollection<NetworkDeviceEntity>? devices = null)
        => new(
            new SqliteConnectionFactory(tempDirectory),
            new FakeLogService(),
            devices is null ? null : new FakeNetworkDeviceReadRepository(devices));

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

    private sealed class FakeNetworkDeviceReadRepository(
        IReadOnlyCollection<NetworkDeviceEntity> devices)
        : IReadRepository<NetworkDeviceEntity>
    {
        public Task<List<NetworkDeviceEntity>> GetListAsync(
            Expression<Func<NetworkDeviceEntity, bool>> expression,
            CancellationToken cancellationToken = default)
            => Task.FromResult(devices.Where(expression.Compile()).ToList());

        public Task<NetworkDeviceEntity?> GetByIdAsync<TKey>(
            TKey id,
            CancellationToken cancellationToken = default)
            where TKey : notnull
            => throw new NotSupportedException();

        public Task<NetworkDeviceEntity?> GetAsync(
            Expression<Func<NetworkDeviceEntity, bool>> expression,
            Expression<Func<NetworkDeviceEntity, object>>[]? includes = null,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<List<NetworkDeviceEntity>> GetListAsync(
            Expression<Func<NetworkDeviceEntity, bool>> expression,
            Expression<Func<NetworkDeviceEntity, object>>[]? includes = null,
            CancellationToken cancellationToken = default)
            => GetListAsync(expression, cancellationToken);

        public Task<List<NetworkDeviceEntity>> GetListAsync(
            ISpecification<NetworkDeviceEntity>? specification = null,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<NetworkDeviceEntity?> GetSingleOrDefaultAsync(
            ISpecification<NetworkDeviceEntity>? specification = null,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<int> GetCountAsync(
            Expression<Func<NetworkDeviceEntity, bool>> expression,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<int> CountAsync(
            ISpecification<NetworkDeviceEntity>? specification = null,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<bool> AnyAsync(
            ISpecification<NetworkDeviceEntity>? specification = null,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }
}
