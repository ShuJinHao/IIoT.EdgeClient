namespace IIoT.Edge.Application.Common.Models
{
    /// <summary>
    /// 单条日志记录模型。
    /// </summary>
    public class LogEntry
    {
        public DateTime Time { get; init; }
        public string Level { get; init; } = string.Empty;
        public string Message { get; init; } = string.Empty;
    }
}
