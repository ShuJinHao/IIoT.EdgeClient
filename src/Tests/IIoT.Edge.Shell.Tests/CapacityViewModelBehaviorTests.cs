using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using IIoT.Edge.Application.Abstractions.Cloud;
using IIoT.Edge.Application.Features.Production.CapacityView;
using IIoT.Edge.Presentation.Navigation.Features.Production.CapacityView;
using IIoT.Edge.Presentation.Panels.Features.DeviceSelection;
using IIoT.Edge.UI.Shared.Avalonia.Controls;
using Xunit;

namespace IIoT.Edge.Shell.Tests;

public sealed class CapacityViewModelBehaviorTests
{
    [Fact]
    public async Task OnActivatedAsync_WhenSelectionIsAll_ShouldQueryAggregateCapacity()
    {
        var facade = new FakeCapacityQueryFacade { IsOnline = true };
        var viewModel = CreateViewModel(facade, new DeviceSelectionService());

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
        var viewModel = CreateViewModel(facade, selectionService);

        await viewModel.OnActivatedAsync();

        Assert.Equal("P1-AP01", facade.LastLoadTodayPlcName);
        Assert.Equal(1, facade.LoadTodayCallCount);
    }

    [Fact]
    public async Task OnActivatedAsync_WhenResultIsEmpty_ShouldShowRealEmptyStateWithoutError()
    {
        var facade = new FakeCapacityQueryFacade
        {
            IsOnline = true,
            LoadTodayResult = CapacityViewResult.Empty()
        };
        var viewModel = CreateViewModel(facade, new DeviceSelectionService());

        await viewModel.OnActivatedAsync();

        Assert.True(viewModel.IsDailyRecordsEmpty);
        Assert.False(viewModel.HasError);
        Assert.Equal(0, viewModel.PeriodTotal);
    }

    [Fact]
    public async Task OnActivatedAsync_WhenServiceBecomesUnavailable_ShouldClearOldDataAndShowSafeError()
    {
        var facade = new FakeCapacityQueryFacade
        {
            IsOnline = true,
            LoadTodayResult = CreateSuccessResult()
        };
        var viewModel = CreateViewModel(facade, new DeviceSelectionService());
        await viewModel.OnActivatedAsync();
        Assert.Single(viewModel.DailyRecords);

        facade.LoadTodayResult = CapacityViewResult.Unavailable("raw_response_body");
        await viewModel.OnActivatedAsync();

        Assert.Empty(viewModel.DailyRecords);
        Assert.Equal(0, viewModel.PeriodTotal);
        Assert.True(viewModel.HasError);
        Assert.Contains("暂不可用", viewModel.ErrorMessage, StringComparison.Ordinal);
        Assert.DoesNotContain("raw_response_body", viewModel.ErrorMessage, StringComparison.Ordinal);
    }

    [Fact]
    public async Task OnActivatedAsync_WhenPayloadIsInvalid_ShouldShowContractErrorWithoutReasonDetails()
    {
        var facade = new FakeCapacityQueryFacade
        {
            IsOnline = true,
            LoadTodayResult = CapacityViewResult.InvalidPayload("sensitive_payload_fragment")
        };
        var viewModel = CreateViewModel(facade, new DeviceSelectionService());

        await viewModel.OnActivatedAsync();

        Assert.Empty(viewModel.DailyRecords);
        Assert.True(viewModel.HasError);
        Assert.Contains("响应格式无效", viewModel.ErrorMessage, StringComparison.Ordinal);
        Assert.DoesNotContain("sensitive_payload_fragment", viewModel.ErrorMessage, StringComparison.Ordinal);
    }

    [Fact]
    public async Task OnActivatedAsync_WhenQueryRecovers_ShouldClearOldErrorAndApplyRealSummary()
    {
        var facade = new FakeCapacityQueryFacade
        {
            IsOnline = true,
            LoadTodayResult = CapacityViewResult.Unavailable("cloud_network_failure")
        };
        var viewModel = CreateViewModel(facade, new DeviceSelectionService());
        await viewModel.OnActivatedAsync();
        Assert.True(viewModel.HasError);

        facade.LoadTodayResult = CreateSuccessResult();
        await viewModel.OnActivatedAsync();

        Assert.False(viewModel.HasError);
        Assert.Single(viewModel.DailyRecords);
        Assert.Equal(10, viewModel.PeriodTotal);
        Assert.Equal(9, viewModel.PeriodOk);
        Assert.Equal(1, viewModel.PeriodNg);
        Assert.Equal("90.00%", viewModel.PeriodYield);
    }

    [Fact]
    public async Task OnActivatedAsync_WhenGateIsOffline_ShouldNotCallFacadeOrSetFailureState()
    {
        var facade = new FakeCapacityQueryFacade
        {
            IsOnline = false,
            LoadTodayResult = CapacityViewResult.Unavailable("must_not_be_observed")
        };
        var viewModel = CreateViewModel(facade, new DeviceSelectionService());

        await viewModel.OnActivatedAsync();

        Assert.Equal(0, facade.LoadTodayCallCount);
        Assert.False(viewModel.HasError);
        Assert.False(viewModel.CanQueryCloud);
        Assert.True(viewModel.IsDailyRecordsEmpty);
    }

    [AvaloniaFact]
    public async Task CapacityViewPage_ShouldBindLoadingAndErrorStatesToSharedTablePanel()
    {
        var completion = new TaskCompletionSource<CapacityViewResult>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var facade = new FakeCapacityQueryFacade
        {
            IsOnline = true,
            LoadTodayHandler = _ => completion.Task
        };
        var viewModel = CreateViewModel(facade, new DeviceSelectionService());
        var view = new CapacityViewPage { DataContext = viewModel };
        const string expectedErrorTitle = "existing-capacity-error-title";
        view.Resources["Navigation_Capacity_QueryFailed"] = expectedErrorTitle;
        var window = new Window
        {
            Width = 1200,
            Height = 760,
            Content = view
        };

        try
        {
            window.Show();
            var table = view.FindControl<EdgeTablePanel>("CapacityTable");
            Assert.NotNull(table);

            var activation = viewModel.OnActivatedAsync();

            Assert.True(viewModel.IsBusy);
            Assert.True(table.IsLoading);

            completion.SetResult(CapacityViewResult.Unavailable("raw_internal_failure"));
            await activation;

            Assert.False(table.IsLoading);
            Assert.True(table.HasError);
            Assert.Equal(expectedErrorTitle, table.ErrorTitle);
            Assert.Equal(viewModel.ErrorMessage, table.ErrorMessage);
            Assert.DoesNotContain("raw_internal_failure", viewModel.ErrorMessage, StringComparison.Ordinal);
        }
        finally
        {
            window.Close();
        }
    }

    private static CapacityViewModel CreateViewModel(
        ICapacityQueryFacade facade,
        IDeviceSelectionService selectionService)
        => new(
            facade,
            new TestAppLanguageService(),
            selectionService);

    private static CapacityViewResult CreateSuccessResult()
    {
        IReadOnlyList<DailyCapacitySnapshot> rows =
        [
            new()
            {
                Date = "07-11",
                DateFull = "2026-07-11",
                Total = 10,
                OkCount = 9,
                NgCount = 1,
                Yield = "90.0%"
            }
        ];

        return CapacityViewResult.Success(rows, 10, 9, 1, "90.00%", "10");
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

        public CapacityViewResult LoadTodayResult { get; set; } = CapacityViewResult.Empty();

        public Func<string, Task<CapacityViewResult>>? LoadTodayHandler { get; init; }

        public Task<CapacityViewResult> LoadTodayAsync(
            string plcName,
            CancellationToken cancellationToken = default)
        {
            LoadTodayCallCount++;
            LastLoadTodayPlcName = plcName;
            return LoadTodayHandler?.Invoke(plcName) ?? Task.FromResult(LoadTodayResult);
        }

        public Task<CapacityViewResult> QueryHistoryAsync(
            string queryMode,
            DateTime queryDate,
            string plcName,
            CancellationToken cancellationToken = default)
            => Task.FromResult(LoadTodayResult);
    }
}
