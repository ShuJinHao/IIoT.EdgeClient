using System.Collections.ObjectModel;
using System.Collections.Specialized;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using IIoT.Edge.Module.Contracts.Logging;
using IIoT.Edge.Presentation.Panels.Features.DeviceSelection;
using IIoT.Edge.Presentation.Panels.Features.SysLog;
using Xunit;

namespace IIoT.Edge.Shell.UiTests;

public sealed class LogViewModelBehaviorTests
{
    [AvaloniaFact]
    public void Entries_WhenMultiplePlcsFailSameSignal_ShouldShowSummaryByDefault()
    {
        var store = new TestSystemLogDisplayStore();
        var viewModel = new LogViewModel(
            store,
            new SystemLogDisplayProjector(),
            new TestAppLanguageService(),
            new DeviceSelectionService());

        store.Entries.Add(CreateEntry("ERROR", "[PLC-A01] 读取 R2450 失败：Read R2450 failed.", second: 1));
        store.Entries.Add(CreateEntry("ERROR", "[PLC-A02] 读取 R2450 失败：Read R2450 failed.", second: 2));

        var summary = Assert.Single(viewModel.Entries);
        Assert.Contains("PLC采样异常", summary.Message, StringComparison.Ordinal);
        Assert.Contains("2 台 PLC", summary.Message, StringComparison.Ordinal);
        Assert.Contains("失败信号 R2450", summary.Message, StringComparison.Ordinal);
        Assert.Contains("MES 上传已暂停", summary.Message, StringComparison.Ordinal);
    }

    [AvaloniaFact]
    public void Entries_WhenDeviceFilterSelected_ShouldShowSelectedDeviceRawEntries()
    {
        var store = new TestSystemLogDisplayStore();
        var viewModel = new LogViewModel(
            store,
            new SystemLogDisplayProjector(),
            new TestAppLanguageService(),
            new DeviceSelectionService());

        store.Entries.Add(CreateEntry("ERROR", "[PLC-A01] 读取 R2450 失败：Read R2450 failed.", second: 1));
        store.Entries.Add(CreateEntry("ERROR", "[PLC-A02] 读取 R2450 失败：Read R2450 failed.", second: 2));

        viewModel.SelectedDeviceFilter = Assert.Single(
            viewModel.DeviceFilters,
            static option => option.Key == "PLC-A01");

        var entry = Assert.Single(viewModel.Entries);
        Assert.Contains("[PLC-A01]", entry.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("[PLC-A02]", entry.Message, StringComparison.Ordinal);
    }

    [AvaloniaFact]
    public void SelectedDeviceFilter_WhenChangedInsideLogPage_ShouldNotPublishSharedSelection()
    {
        var store = new TestSystemLogDisplayStore();
        var selectionService = new DeviceSelectionService();
        var viewModel = new LogViewModel(
            store,
            new SystemLogDisplayProjector(),
            new TestAppLanguageService(),
            selectionService);

        store.Entries.Add(CreateEntry("ERROR", "[PLC-A01] 读取 R2450 失败：Read R2450 failed.", second: 1));

        viewModel.SelectedDeviceFilter = Assert.Single(
            viewModel.DeviceFilters,
            static option => option.Key == "PLC-A01");

        Assert.Equal(IDeviceSelectionService.AllFilterKey, selectionService.SelectedDeviceKey);
        Assert.Equal("PLC-A01", viewModel.SelectedDeviceFilter?.Key);
    }

    [AvaloniaFact]
    public void Entries_WhenSharedSelectionHasNoCurrentLogRows_ShouldKeepSelectedDeviceFilter()
    {
        var store = new TestSystemLogDisplayStore();
        var selectionService = new DeviceSelectionService();
        var viewModel = new LogViewModel(
            store,
            new SystemLogDisplayProjector(),
            new TestAppLanguageService(),
            selectionService);

        selectionService.SelectDevice("PLC-A09");

        Assert.Equal("PLC-A09", viewModel.SelectedDeviceFilter?.Key);
        Assert.Contains(viewModel.DeviceFilters, static option => option.Key == "PLC-A09");
        Assert.True(viewModel.IsLogEmpty);
    }

    [AvaloniaFact]
    public async Task LogDisplayService_WhenPublishedConcurrently_ShouldBatchOnUiThreadAndKeepLatestTwoHundred()
    {
        var source = new ConcurrentLogService();
        var display = new LogDisplayService(source);
        var resetCount = 0;
        var collectionNotificationsStayedOnUiThread = true;
        var batchCompleted = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        display.Entries.CollectionChanged += (_, args) =>
        {
            collectionNotificationsStayedOnUiThread &= Dispatcher.UIThread.CheckAccess();
            if (args.Action == NotifyCollectionChangedAction.Reset)
            {
                resetCount++;
                if (display.Entries.Count == 200)
                {
                    batchCompleted.TrySetResult(true);
                }
            }
        };

        var publishers = Enumerable.Range(0, 4)
            .Select(worker => Task.Run(
                () =>
                {
                    for (var index = 0; index < 75; index++)
                    {
                        source.Publish($"worker={worker};index={index}");
                    }
                },
                TestContext.Current.CancellationToken))
            .ToArray();
        await Task.WhenAll(publishers);

        Assert.Empty(display.Entries);
        await batchCompleted.Task.WaitAsync(
            TimeSpan.FromSeconds(5),
            TestContext.Current.CancellationToken);

        Assert.True(collectionNotificationsStayedOnUiThread);
        Assert.Equal(200, display.Entries.Count);
        Assert.Equal(1, resetCount);
    }

    private static LogEntry CreateEntry(string level, string message, int second)
        => new()
        {
            Time = new DateTime(2026, 6, 24, 16, 24, second),
            Level = level,
            Message = message
        };

    private sealed class TestSystemLogDisplayStore : ISystemLogDisplayStore
    {
        public ObservableCollection<LogEntry> Entries { get; } = [];

        public void Clear() => Entries.Clear();
    }

    private sealed class ConcurrentLogService : ILogService
    {
        public event Action<LogEntry>? EntryAdded;

        public void Debug(string message) => Publish(message);

        public void Info(string message) => Publish(message);

        public void Warn(string message) => Publish(message);

        public void Error(string message) => Publish(message);

        public void Fatal(string message) => Publish(message);

        public void Publish(string message)
            => EntryAdded?.Invoke(new LogEntry
            {
                Time = DateTime.UtcNow,
                Level = "Info",
                Message = message
            });
    }
}
