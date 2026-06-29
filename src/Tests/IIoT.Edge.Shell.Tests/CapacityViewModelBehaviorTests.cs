using IIoT.Edge.Application.Abstractions.Cloud;
using IIoT.Edge.Application.Features.Production.CapacityView;
using IIoT.Edge.Presentation.Navigation.Features.Production.CapacityView;
using IIoT.Edge.Presentation.Panels.Features.DeviceSelection;
using Xunit;

namespace IIoT.Edge.Shell.Tests;

public sealed class CapacityViewModelBehaviorTests
{
    [Fact]
    public async Task OnActivatedAsync_WhenSelectionIsAll_ShouldQueryAggregateCapacity()
    {
        var facade = new FakeCapacityQueryFacade { IsOnline = true };
        var viewModel = new CapacityViewModel(
            facade,
            new TestAppLanguageService(),
            new DeviceSelectionService());

        await viewModel.OnActivatedAsync();

        Assert.Equal(string.Empty, facade.LastLoadTodayPlcName);
        Assert.Equal(1, facade.LoadTodayCallCount);
    }

    [Fact]
    public async Task OnActivatedAsync_WhenDeviceIsSelected_ShouldQuerySelectedDeviceCapacity()
    {
        var facade = new FakeCapacityQueryFacade { IsOnline = true };
        var selectionService = new DeviceSelectionService();
        selectionService.SelectDevice("P1-AP01");
        var viewModel = new CapacityViewModel(
            facade,
            new TestAppLanguageService(),
            selectionService);

        await viewModel.OnActivatedAsync();

        Assert.Equal("P1-AP01", facade.LastLoadTodayPlcName);
        Assert.Equal(1, facade.LoadTodayCallCount);
    }

    private sealed class FakeCapacityQueryFacade : ICapacityQueryFacade
    {
        public event Action<EdgeUploadGateSnapshot>? UploadGateChanged
        {
            add { }
            remove { }
        }

        public bool IsOnline { get; init; }

        public int LoadTodayCallCount { get; private set; }

        public string? LastLoadTodayPlcName { get; private set; }

        public IReadOnlyList<string> GetDeviceNames() => [];

        public Task<CapacityViewResult> LoadTodayAsync(
            string plcName,
            CancellationToken cancellationToken = default)
        {
            LoadTodayCallCount++;
            LastLoadTodayPlcName = plcName;
            return Task.FromResult(new CapacityViewResult([], 0, 0, 0, "0%", "0"));
        }

        public Task<CapacityViewResult> QueryHistoryAsync(
            string queryMode,
            DateTime queryDate,
            string plcName,
            CancellationToken cancellationToken = default)
            => Task.FromResult(new CapacityViewResult([], 0, 0, 0, "0%", "0"));
    }
}
