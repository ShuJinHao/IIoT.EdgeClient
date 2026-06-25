using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using Avalonia.Threading;
using AvaloniaDispatcher = Avalonia.Threading.Dispatcher;
using IIoT.Edge.Application.Abstractions.Logging;

namespace IIoT.Edge.Presentation.Panels.Features.SysLog;

public interface ISystemLogDisplayStore
{
    ObservableCollection<LogEntry> Entries { get; }

    void Clear();
}

public interface ILogDeviceSelectionService
{
    const string AllFilterKey = "__all__";

    string SelectedDeviceKey { get; }

    event EventHandler? SelectionChanged;

    void SelectDevice(string deviceKey);
}

public sealed class LogDeviceSelectionService : ILogDeviceSelectionService
{
    private string _selectedDeviceKey = ILogDeviceSelectionService.AllFilterKey;

    public string SelectedDeviceKey => _selectedDeviceKey;

    public event EventHandler? SelectionChanged;

    public void SelectDevice(string deviceKey)
    {
        var normalizedKey = string.IsNullOrWhiteSpace(deviceKey)
            ? ILogDeviceSelectionService.AllFilterKey
            : deviceKey;
        if (string.Equals(_selectedDeviceKey, normalizedKey, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        _selectedDeviceKey = normalizedKey;
        SelectionChanged?.Invoke(this, EventArgs.Empty);
    }
}

/// <summary>
/// 日志展示服务，负责把真实日志事件批量同步到 Avalonia UI 集合。
/// </summary>
public class LogDisplayService : ISystemLogDisplayStore
{
    private const int MaxDisplayEntries = 200;
    private static readonly TimeSpan FlushInterval = TimeSpan.FromMilliseconds(500);

    private readonly ILogService _inner;
    private readonly ConcurrentQueue<LogEntry> _pendingEntries = new();
    private int _flushScheduled;

    public ObservableCollection<LogEntry> Entries { get; } = new BatchedLogEntryCollection();

    public LogDisplayService(ILogService inner)
    {
        _inner = inner;
        _inner.EntryAdded += OnInnerEntryAdded;
    }

    public void Clear()
    {
        while (_pendingEntries.TryDequeue(out _))
        {
        }

        void ClearCore()
        {
            if (Entries is BatchedLogEntryCollection batched)
            {
                batched.ClearBatched();
                return;
            }

            Entries.Clear();
        }

        if (AvaloniaDispatcher.UIThread.CheckAccess())
        {
            ClearCore();
            return;
        }

        AvaloniaDispatcher.UIThread.Post(ClearCore, DispatcherPriority.Background);
    }

    private void OnInnerEntryAdded(LogEntry entry)
    {
        _pendingEntries.Enqueue(entry);
        ScheduleFlush();
    }

    private void ScheduleFlush()
    {
        if (Interlocked.CompareExchange(ref _flushScheduled, 1, 0) != 0)
        {
            return;
        }

        _ = FlushLaterAsync();
    }

    private async Task FlushLaterAsync()
    {
        await Task.Delay(FlushInterval).ConfigureAwait(false);
        AvaloniaDispatcher.UIThread.Post(FlushPendingEntries, DispatcherPriority.Background);
    }

    private void FlushPendingEntries()
    {
        var entries = new List<LogEntry>();
        while (_pendingEntries.TryDequeue(out var entry))
        {
            entries.Add(entry);
        }

        if (entries.Count > 0)
        {
            if (Entries is BatchedLogEntryCollection batched)
            {
                batched.PrependRange(entries, MaxDisplayEntries);
            }
            else
            {
                foreach (var entry in entries)
                {
                    Entries.Insert(0, entry);
                }

                while (Entries.Count > MaxDisplayEntries)
                {
                    Entries.RemoveAt(Entries.Count - 1);
                }
            }
        }

        Interlocked.Exchange(ref _flushScheduled, 0);
        if (!_pendingEntries.IsEmpty)
        {
            ScheduleFlush();
        }
    }

    private sealed class BatchedLogEntryCollection : ObservableCollection<LogEntry>
    {
        public void PrependRange(IReadOnlyList<LogEntry> entries, int maxCount)
        {
            if (entries.Count == 0)
            {
                return;
            }

            foreach (var entry in entries)
            {
                Items.Insert(0, entry);
            }

            while (Items.Count > maxCount)
            {
                Items.RemoveAt(Items.Count - 1);
            }

            RaiseReset();
        }

        public void ClearBatched()
        {
            if (Items.Count == 0)
            {
                return;
            }

            Items.Clear();
            RaiseReset();
        }

        private void RaiseReset()
        {
            OnPropertyChanged(new PropertyChangedEventArgs(nameof(Count)));
            OnPropertyChanged(new PropertyChangedEventArgs("Item[]"));
            OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
        }
    }
}
