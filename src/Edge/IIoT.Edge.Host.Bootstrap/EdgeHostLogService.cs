using IIoT.Edge.Application.Abstractions.Logging;
using IIoT.Edge.Application.Common.Models;
using System.Collections.ObjectModel;

namespace IIoT.Edge.Host.Bootstrap;

public sealed class EdgeHostLogService : ILogDisplayService
{
    private const int MaxBufferedEntries = 500;
    private readonly object _syncRoot = new();

    public event Action<LogEntry>? EntryAdded;

    public ObservableCollection<LogEntry> Entries { get; } = [];

    public void Debug(string message) => Write("DEBUG", message);

    public void Info(string message) => Write("INFO", message);

    public void Warn(string message) => Write("WARN", message);

    public void Error(string message) => Write("ERROR", message);

    public void Fatal(string message) => Write("FATAL", message);

    private void Write(string level, string message)
    {
        var entry = new LogEntry
        {
            Time = DateTime.Now,
            Level = level,
            Message = message
        };

        lock (_syncRoot)
        {
            Entries.Add(entry);
            while (Entries.Count > MaxBufferedEntries)
            {
                Entries.RemoveAt(0);
            }
        }

        EntryAdded?.Invoke(entry);
    }
}
