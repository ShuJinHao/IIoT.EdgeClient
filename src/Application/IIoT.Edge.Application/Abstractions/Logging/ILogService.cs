using IIoT.Edge.Application.Common.Models;
using System.Collections.ObjectModel;

namespace IIoT.Edge.Application.Abstractions.Logging
{
    /// <summary>
    /// 日志服务契约。
    /// 由应用层定义契约，具体实现由上层装配提供。
    /// 包含日志写入和事件通知能力，不涉及 UI 关注点。
    /// </summary>
    public interface ILogService
    {
        void Debug(string message);
        void Info(string message);
        void Warn(string message);
        void Error(string message);
        void Fatal(string message);

        /// <summary>每写入一条日志触发一次，参数为新增日志条目。供 DeviceLogSyncTask 等订阅使用。</summary>
        event Action<LogEntry> EntryAdded;
    }

    /// <summary>
    /// 日志展示服务契约。
    /// 在 ILogService 基础上增加 UI 绑定能力（Entries 集合）。
    /// 仅由 Presentation 层的 ViewModel 依赖。
    /// </summary>
    public interface ILogDisplayService : ILogService
    {
        /// <summary>供界面绑定的日志条目集合，支持线程安全写入。</summary>
        ObservableCollection<LogEntry> Entries { get; }
    }
}
