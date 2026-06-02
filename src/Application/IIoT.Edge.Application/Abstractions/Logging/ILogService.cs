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

}
