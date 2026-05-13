using IIoT.Edge.Application.Abstractions.Logging;
using IIoT.Edge.Application.Common.Models;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Threading;

namespace IIoT.Edge.Presentation.Panels.Features.SysLog;

/// <summary>
/// 日志展示服务（装饰器）。
/// 包装 ILogService，订阅其 EntryAdded 事件将日志同步到 UI 集合。
/// 仅由 Presentation 层的 ViewModel 通过 ILogDisplayService 依赖。
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
        if (System.Windows.Application.Current?.Dispatcher is { } dispatcher)
        {
            dispatcher.Invoke(() =>
            {
                Entries.Insert(0, entry);
                if (Entries.Count > 200)
                    Entries.RemoveAt(Entries.Count - 1);
            }, DispatcherPriority.Background);
        }

        EntryAdded?.Invoke(entry);
    }
}
