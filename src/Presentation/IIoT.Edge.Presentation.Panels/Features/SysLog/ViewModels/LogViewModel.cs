using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Windows.Input;
using IIoT.Edge.Application.Common.Models;
using IIoT.Edge.Application.Features.SysLog.LogView;
using IIoT.Edge.UI.Shared.Mvvm;
using IIoT.Edge.UI.Shared.PluginSystem;

namespace IIoT.Edge.Presentation.Panels.Features.SysLog;

/// <summary>
/// 系统日志面板视图模型。
/// </summary>
public class LogViewModel : PresentationViewModelBase
{
    private readonly ILogViewService _logViewService;

    public override string ViewId => "Core.SysLog";
    public override string ViewTitle => "系统日志";

    public ObservableCollection<LogEntry> Entries => _logViewService.Entries;

    public bool HasEntries => Entries.Count > 0;
    public bool IsLogEmpty => !HasEntries;

    public ICommand ClearCommand { get; }

    public LogViewModel(ILogViewService logViewService)
    {
        _logViewService = logViewService;

        LayoutRow = 1;
        LayoutColumn = 1;

        Entries.CollectionChanged += OnEntriesChanged;
        ClearCommand = new BaseCommand(_ => _logViewService.Clear());
    }

    private void OnEntriesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        OnPropertyChanged(nameof(HasEntries));
        OnPropertyChanged(nameof(IsLogEmpty));
    }
}
