using System.Collections.ObjectModel;
using Avalonia.Threading;
using AvaloniaDispatcher = Avalonia.Threading.Dispatcher;
using IIoT.Edge.Application.Abstractions.Logging;

namespace IIoT.Edge.Presentation.Panels.Features.SysLog;

public interface ISystemLogDisplayStore
{
    ObservableCollection<LogEntry> Entries { get; }

    void Clear();
}

/// <summary>
/// 日志展示服务，负责把真实日志事件同步到 Avalonia UI 集合。
/// </summary>
public class LogDisplayService : ISystemLogDisplayStore
{
    private readonly ILogService _inner;

    public ObservableCollection<LogEntry> Entries { get; } = new();

    public LogDisplayService(ILogService inner)
    {
        _inner = inner;
        _inner.EntryAdded += OnInnerEntryAdded;
    }

    public void Clear()
    {
        Entries.Clear();
    }

    private void OnInnerEntryAdded(LogEntry entry)
    {
        void ApplyEntry()
        {
            Entries.Insert(0, entry);
            if (Entries.Count > 200)
            {
                Entries.RemoveAt(Entries.Count - 1);
            }
        }

        if (AvaloniaDispatcher.UIThread.CheckAccess())
        {
            ApplyEntry();
            return;
        }

        AvaloniaDispatcher.UIThread.Post(ApplyEntry, DispatcherPriority.Background);
    }
}
