using IIoT.Edge.Application.Common.Models;
using IIoT.Edge.Application.Features.SysLog.LogView;
using IIoT.Edge.UI.Shared.Mvvm;
using IIoT.Edge.UI.Shared.PluginSystem;
using System.Collections.ObjectModel;
using System.Windows.Input;

namespace IIoT.Edge.Presentation.Panels.Features.SysLog;

/// <summary>
/// 系统日志面板视图模型。
/// </summary>
public class LogViewModel : PresentationViewModelBase
{
    public override string ViewId => "Core.SysLog";
    public override string ViewTitle => "系统日志";

    private readonly ILogViewService _logViewService;

    public ObservableCollection<LogEntry> Entries => _logViewService.Entries;

    public ICommand ClearCommand { get; }

    public LogViewModel(ILogViewService logViewService)
    {
        _logViewService = logViewService;

        LayoutRow = 1;
        LayoutColumn = 1;

        ClearCommand = new BaseCommand(_ => _logViewService.Clear());
    }
}
