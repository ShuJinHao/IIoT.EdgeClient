using System.Collections.ObjectModel;
using Avalonia.Threading;
using AvaloniaDispatcher = Avalonia.Threading.Dispatcher;
using IIoT.Edge.Application.Abstractions.Logging;
using IIoT.Edge.Application.Common.Models;

namespace IIoT.Edge.Presentation.Panels.Features.SysLog;

/// <summary>
/// 日志展示服务，负责把真实日志事件同步到 Avalonia UI 集合。
/// </summary>
public class LogDisplayService : ILogDisplayService
{
    private readonly ILogService _inner;

    public ObservableCollection<LogEntry> Entries { get; } = new();

    public event Action<LogEntry>? EntryAdded;

    public LogDisplayService(ILogService inner)
    {
        _inner = inner;
        _inner.EntryAdded += OnInnerEntryAdded;
    }

    public void Debug(string message) => _inner.Debug(message);
    public void Info(string message) => _inner.Info(message);
    public void Warn(string message) => _inner.Warn(message);
    public void Error(string message) => _inner.Error(message);
    public void Fatal(string message) => _inner.Fatal(message);

    private void OnInnerEntryAdded(LogEntry entry)
    {
        void ApplyEntry()
        {
            Entries.Insert(0, entry);
            if (Entries.Count > 200)
            {
                Entries.RemoveAt(Entries.Count - 1);
            }

            EntryAdded?.Invoke(entry);
        }

        if (AvaloniaDispatcher.UIThread.CheckAccess())
        {
            ApplyEntry();
            return;
        }

        AvaloniaDispatcher.UIThread.Post(ApplyEntry, DispatcherPriority.Background);
    }
}
