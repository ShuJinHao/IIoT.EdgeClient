using System.Collections.Specialized;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Avalonia.Headless.XUnit;
using IIoT.Edge.Module.Contracts.Cloud;
using IIoT.Edge.Application.Features.Production.CapacityView;
using IIoT.Edge.Presentation.Navigation.Features.Production.CapacityView;
using IIoT.Edge.Presentation.Panels.Features.DeviceSelection;
using IIoT.Edge.UI.Shared.Avalonia.Controls;
using Xunit;

namespace IIoT.Edge.Shell.UiTests;

public sealed class CapacityViewModelBehaviorTests
{
    [AvaloniaFact]
    public async Task OnActivatedAsync_WhenSelectionIsAll_ShouldQueryAggregateCapacity()
    {
        var facade = new FakeCapacityQueryFacade { IsOnline = true };
        var viewModel = CreateViewModel(facade, new DeviceSelectionService());

        await viewModel.OnActivatedAsync();

        Assert.Equal(string.Empty, facade.LastLoadTodayPlcName);
        Assert.Equal(1, facade.LoadTodayCallCount);
    }

    [AvaloniaFact]
    public async Task OnActivatedAsync_WhenDeviceIsSelected_ShouldQuerySelectedDeviceCapacity()
    {
        var facade = new FakeCapacityQueryFacade { IsOnline = true };
        var selectionService = new DeviceSelectionService();
        selectionService.SelectDevice("PLC-A01");
        var viewModel = CreateViewModel(facade, selectionService);

        await viewModel.OnActivatedAsync();

        Assert.Equal("PLC-A01", facade.LastLoadTodayPlcName);
        Assert.Equal(1, facade.LoadTodayCallCount);
    }

    [AvaloniaFact]
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

    [AvaloniaFact]
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

    [AvaloniaFact]
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

    [AvaloniaFact]
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

    [AvaloniaFact]
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

    [AvaloniaFact]
    public async Task BackgroundCapacityNotification_BeforePageCreation_ShouldInitializeAndRenderChartOnUiThread()
    {
        var facade = new FakeCapacityQueryFacade
        {
            IsOnline = true,
            LoadTodayResult = CreateSuccessResult()
        };
        var selectionService = new DeviceSelectionService();
        var viewModel = await Task.Run(
            () => CreateViewModel(facade, selectionService),
            TestContext.Current.CancellationToken);
        var collectionNotificationsStayedOnUiThread = true;
        viewModel.ChartSeries.CollectionChanged += (_, _) =>
            collectionNotificationsStayedOnUiThread &= Dispatcher.UIThread.CheckAccess();
        viewModel.ChartPoints.CollectionChanged += (_, _) =>
            collectionNotificationsStayedOnUiThread &= Dispatcher.UIThread.CheckAccess();

        global::Avalonia.Application.Current!.Resources["Edge.Brush.Chart.Accent"] = Brushes.DodgerBlue;
        global::Avalonia.Application.Current.Resources["Edge.Brush.Status.Running"] = Brushes.ForestGreen;
        global::Avalonia.Application.Current.Resources["Edge.Brush.Status.Warning"] = Brushes.Goldenrod;
        global::Avalonia.Application.Current.Resources["Edge.Brush.Chart.Secondary"] = Brushes.MediumPurple;

        var chartReady = WaitForChartReadyAsync(viewModel);
        await Task.Run(
            () => facade.PublishUploadGate(new EdgeUploadGateSnapshot
            {
                State = EdgeUploadGateState.Ready,
                Reason = EdgeUploadBlockReason.None
            }),
            TestContext.Current.CancellationToken);

        await chartReady;

        var view = new CapacityViewPage(viewModel);
        var window = new Window
        {
            Width = 1200,
            Height = 760,
            Content = view
        };

        try
        {
            window.Show();
            var chart = Assert.Single(view.GetVisualDescendants().OfType<EdgeBarLineChart>());
            for (var attempt = 0; attempt < 5; attempt++)
            {
                window.Measure(new Avalonia.Size(1200, 760));
                window.Arrange(new Avalonia.Rect(0, 0, 1200, 760));
                chart.InvalidateVisual();
                await Dispatcher.UIThread.InvokeAsync(static () => { });
            }

            Assert.True(collectionNotificationsStayedOnUiThread);
            Assert.All(viewModel.ChartSeries, series => Assert.NotNull(series.Brush));
            Assert.Single(viewModel.ChartPoints);
        }
        finally
        {
            window.Close();
        }
    }

    private static async Task WaitForChartReadyAsync(CapacityViewModel viewModel)
    {
        var completion = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        NotifyCollectionChangedEventHandler handler = (_, _) =>
        {
            if (viewModel.ChartPoints.Count == 1 && viewModel.ChartSeries.Count == 4)
            {
                completion.TrySetResult(true);
            }
        };
        viewModel.ChartPoints.CollectionChanged += handler;
        viewModel.ChartSeries.CollectionChanged += handler;
        try
        {
            if (viewModel.ChartPoints.Count == 1 && viewModel.ChartSeries.Count == 4)
            {
                completion.TrySetResult(true);
            }

            await completion.Task.WaitAsync(TestContext.Current.CancellationToken);
        }
        finally
        {
            viewModel.ChartPoints.CollectionChanged -= handler;
            viewModel.ChartSeries.CollectionChanged -= handler;
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
        public event Action<EdgeUploadGateSnapshot>? UploadGateChanged;

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

        public void PublishUploadGate(EdgeUploadGateSnapshot snapshot)
            => UploadGateChanged?.Invoke(snapshot);
    }
}
