using System.Collections.ObjectModel;
using IIoT.Edge.Application.Abstractions.Logging;
using IIoT.Edge.Presentation.Panels.Features.SysLog;
using Xunit;

namespace IIoT.Edge.Shell.Tests;

public sealed class LogViewModelBehaviorTests
{
    [Fact]
    public void Entries_WhenMultipleDieCuttingPlcsFailSameSignal_ShouldShowSummaryByDefault()
    {
        var store = new TestSystemLogDisplayStore();
        var viewModel = new LogViewModel(
            store,
            new SystemLogDisplayProjector(),
            new TestAppLanguageService(),
            new LogDeviceSelectionService());

        store.Entries.Add(CreateEntry("ERROR", "[P1-AP01] 读取 R2450 失败：Read R2450 failed.", second: 1));
        store.Entries.Add(CreateEntry("ERROR", "[P1-AP02] 读取 R2450 失败：Read R2450 failed.", second: 2));

        var summary = Assert.Single(viewModel.Entries);
        Assert.Contains("负极模切采样异常", summary.Message, StringComparison.Ordinal);
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
            new LogDeviceSelectionService());

        store.Entries.Add(CreateEntry("ERROR", "[P1-AP01] 读取 R2450 失败：Read R2450 failed.", second: 1));
        store.Entries.Add(CreateEntry("ERROR", "[P1-AP02] 读取 R2450 失败：Read R2450 failed.", second: 2));

        viewModel.SelectedDeviceFilter = Assert.Single(
            viewModel.DeviceFilters,
            static option => option.Key == "P1-AP01");

        var entry = Assert.Single(viewModel.Entries);
        Assert.Contains("[P1-AP01]", entry.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("[P1-AP02]", entry.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void SelectedDeviceFilter_WhenChanged_ShouldPublishSharedSelection()
    {
        var store = new TestSystemLogDisplayStore();
        var selectionService = new LogDeviceSelectionService();
        var viewModel = new LogViewModel(
            store,
            new SystemLogDisplayProjector(),
            new TestAppLanguageService(),
            selectionService);

        store.Entries.Add(CreateEntry("ERROR", "[P1-AP01] 读取 R2450 失败：Read R2450 failed.", second: 1));

        viewModel.SelectedDeviceFilter = Assert.Single(
            viewModel.DeviceFilters,
            static option => option.Key == "P1-AP01");

        Assert.Equal("P1-AP01", selectionService.SelectedDeviceKey);
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
