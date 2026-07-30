using IIoT.Edge.Application.Common.Identity;
using IIoT.Edge.Application.Features.Production.Equipment;
using IIoT.Edge.Module.Contracts.Production;
using IIoT.Edge.Module.Contracts.UI;

namespace IIoT.Edge.Application.Tests;

public sealed class CapacitySnapshotProductionRecordBehaviorTests
{
    [Fact]
    public async Task Handler_ShouldAggregateOnlyModuleProductionRecordSourcesForCurrentSelection()
    {
        var time = new FakeProductionTimeProvider
        {
            FixedUtcNow = new DateTime(2026, 7, 25, 1, 30, 0, DateTimeKind.Utc)
        };
        var selection = new FakeSelectionContext("正极模切07");
        var ap = new FakeSummarySource(
            "AP",
            new ModuleProductionRecordSummary(
                12,
                10,
                2,
                5,
                4,
                1,
                "AP-BATCH"));
        var cp = new FakeSummarySource(
            "CP",
            new ModuleProductionRecordSummary(
                23,
                20,
                3,
                8,
                7,
                1,
                "CP-BATCH"));
        var handler = new GetCapacitySnapshotHandler([cp, ap], time, selection);

        var result = await handler.Handle(
            new GetCapacitySnapshotQuery(),
            TestContext.Current.CancellationToken);

        Assert.Equal(35, result.TodayOutput);
        Assert.Equal(30, result.OkCount);
        Assert.Equal(5, result.NgCount);
        Assert.Equal("85.7%", result.TodayYield);
        Assert.Equal("AP-BATCH", result.CurrentBatch);
        Assert.Equal(13, result.RecentHourOutput);
        Assert.Equal(11, result.RecentHourOk);
        Assert.Equal(2, result.RecentHourNg);
        Assert.Equal("08:30-09:30", result.RecentHourLabel);
        Assert.All(
            new[] { Assert.IsType<ProductionRecordSummaryQuery>(ap.LastQuery), Assert.IsType<ProductionRecordSummaryQuery>(cp.LastQuery) },
            query =>
            {
                Assert.Equal("正极模切07", query.SelectedDeviceKey);
                Assert.Equal(new DateTime(2026, 7, 24, 16, 0, 0, DateTimeKind.Utc), query.RangeStartUtc);
                Assert.Equal(new DateTime(2026, 7, 25, 16, 0, 0, DateTimeKind.Utc), query.RangeEndUtc);
                Assert.Equal(new DateTime(2026, 7, 25, 0, 30, 0, DateTimeKind.Utc), query.RecentWindowStartUtc);
            });
    }

    [Fact]
    public async Task Handler_WhenNoCompletedRows_ShouldReturnExplicitZeroSnapshot()
    {
        var time = new FakeProductionTimeProvider
        {
            FixedUtcNow = new DateTime(2026, 7, 25, 1, 30, 0, DateTimeKind.Utc)
        };
        var handler = new GetCapacitySnapshotHandler(
            [],
            time,
            new FakeSelectionContext(IDeviceSelectionContext.AllFilterKey));

        var result = await handler.Handle(
            new GetCapacitySnapshotQuery(),
            TestContext.Current.CancellationToken);

        Assert.Equal(0, result.TodayOutput);
        Assert.Equal(0, result.OkCount);
        Assert.Equal(0, result.NgCount);
        Assert.Equal("0.0%", result.TodayYield);
        Assert.Equal("--", result.CurrentBatch);
        Assert.Equal(0, result.RecentHourOutput);
    }

    [Fact]
    public async Task Handler_WhenStablePlcCodeIsAvailable_ShouldUseItForProductionSummarySelection()
    {
        var time = new FakeProductionTimeProvider
        {
            FixedUtcNow = new DateTime(2026, 7, 25, 1, 30, 0, DateTimeKind.Utc)
        };
        var source = new FakeSummarySource(
            "AP",
            new ModuleProductionRecordSummary(0, 0, 0, 0, 0, 0, string.Empty));
        var handler = new GetCapacitySnapshotHandler(
            [source],
            time,
            new FakeSelectionContext("旧显示名", "P1-AP01"));

        await handler.Handle(
            new GetCapacitySnapshotQuery(),
            TestContext.Current.CancellationToken);

        Assert.Equal(
            "P1-AP01",
            Assert.IsType<ProductionRecordSummaryQuery>(source.LastQuery).SelectedDeviceKey);
    }

    private sealed class FakeSummarySource(
        string moduleId,
        ModuleProductionRecordSummary summary)
        : IModuleProductionRecordSummarySource
    {
        public string ModuleId => moduleId;

        public ProductionRecordSummaryQuery? LastQuery { get; private set; }

        public Task<ModuleProductionRecordSummary> QueryAsync(
            ProductionRecordSummaryQuery query,
            CancellationToken cancellationToken = default)
        {
            LastQuery = query;
            return Task.FromResult(summary);
        }
    }

    private sealed class FakeSelectionContext(
        string selectedDeviceKey,
        string? selectedPlcCode = null) : IPlcDeviceSelectionContext
    {
        public string SelectedDeviceKey { get; } = selectedDeviceKey;

        public string? SelectedPlcCode { get; } = selectedPlcCode;

        public bool IsAllSelected => string.Equals(
            SelectedDeviceKey,
            IDeviceSelectionContext.AllFilterKey,
            StringComparison.OrdinalIgnoreCase);

        public event EventHandler? SelectionChanged
        {
            add
            {
            }
            remove
            {
            }
        }
    }
}
