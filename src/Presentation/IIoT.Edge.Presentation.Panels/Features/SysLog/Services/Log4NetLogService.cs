using IIoT.Edge.Application.Abstractions.Config;
using IIoT.Edge.Application.Abstractions.Logging;
using IIoT.Edge.Application.Common.Models;
using log4net;
using log4net.Config;
using System.IO;

namespace IIoT.Edge.Presentation.Panels.Features.SysLog;

/// <summary>
/// 纯 log4net 日志服务。
/// 仅负责写文件日志和触发 EntryAdded 事件，不涉及任何 UI 操作。
/// 所有层（Infrastructure、Runtime、Application）通过 ILogService 使用此实现。
/// </summary>
public class Log4NetLogService : ILogService
{
    private static readonly ILog _log4net = LogManager.GetLogger("logLogger");

    public event Action<LogEntry>? EntryAdded;

    public Log4NetLogService(EdgeRuntimePaths runtimePaths)
    {
        var configPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "log4net.config");
        Directory.CreateDirectory(runtimePaths.LogDirectory);
        log4net.GlobalContext.Properties["LogDirectory"] = EnsureTrailingSeparator(runtimePaths.LogDirectory);

        if (File.Exists(configPath))
            XmlConfigurator.Configure(new FileInfo(configPath));
    }

    public void Debug(string message) => Write("DEBUG", message, () => _log4net.Debug(message));
    public void Info(string message) => Write("INFO", message, () => _log4net.Info(message));
    public void Warn(string message) => Write("WARN", message, () => _log4net.Warn(message));
    public void Error(string message) => Write("ERROR", message, () => _log4net.Error(message));
    public void Fatal(string message) => Write("FATAL", message, () => _log4net.Fatal(message));

    private void Write(string level, string message, Action log4netWrite)
    {
        log4netWrite();

        var entry = new LogEntry
        {
            Time = DateTime.Now,
            Level = level,
            Message = message
        };

        EntryAdded?.Invoke(entry);
    }

    private static string EnsureTrailingSeparator(string path)
        => path.EndsWith(Path.DirectorySeparatorChar)
            ? path
            : path + Path.DirectorySeparatorChar;
}
