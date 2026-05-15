using IIoT.Edge.Application.Abstractions.Logging;
using IIoT.Edge.Application.Common.Models;

namespace IIoT.Edge.Host.Bootstrap;

public sealed class EdgeHostLogService : ILogService
{
    public event Action<LogEntry>? EntryAdded;

    public void Debug(string message) => Write("DEBUG", message);

    public void Info(string message) => Write("INFO", message);

    public void Warn(string message) => Write("WARN", message);

    public void Error(string message) => Write("ERROR", message);

    public void Fatal(string message) => Write("FATAL", message);

    private void Write(string level, string message)
    {
        EntryAdded?.Invoke(new LogEntry
        {
            Time = DateTime.Now,
            Level = level,
            Message = message
        });
    }
}
