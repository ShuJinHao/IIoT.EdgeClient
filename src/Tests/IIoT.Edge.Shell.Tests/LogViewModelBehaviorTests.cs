using System.Collections.ObjectModel;
using IIoT.Edge.Application.Abstractions.Logging;
using IIoT.Edge.Presentation.Panels.Features.DeviceSelection;
using IIoT.Edge.Presentation.Panels.Features.SysLog;
using Xunit;

namespace IIoT.Edge.Shell.Tests;

public sealed class LogViewModelBehaviorTests
{
    [Fact]
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

    [Fact]
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

    [Fact]
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

    [Fact]
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
}
