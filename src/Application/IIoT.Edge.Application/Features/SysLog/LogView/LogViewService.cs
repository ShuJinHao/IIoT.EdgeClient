using IIoT.Edge.Application.Abstractions.Logging;
using IIoT.Edge.Application.Common.Models;
using System.Collections.ObjectModel;

namespace IIoT.Edge.Application.Features.SysLog.LogView;

/// <summary>
/// 系统日志页面服务契约。
/// 向界面提供日志条目集合与清空能力。
/// </summary>
public interface ILogViewService
{
    ObservableCollection<LogEntry> Entries { get; }

    void Clear();
}

/// <summary>
/// 系统日志页面服务。
/// 负责将日志展示服务中的条目集合暴露给界面层使用。
/// </summary>
public sealed class LogViewService(ILogDisplayService logDisplayService) : ILogViewService
{
    public ObservableCollection<LogEntry> Entries => logDisplayService.Entries;

    public void Clear()
    {
        logDisplayService.Entries.Clear();
    }
}
