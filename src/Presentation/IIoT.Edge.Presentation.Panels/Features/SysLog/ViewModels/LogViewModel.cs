using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Windows.Input;
using IIoT.Edge.Application.Abstractions.Logging;
using IIoT.Edge.UI.Shared.Mvvm;
using IIoT.Edge.UI.Shared.PluginSystem;

namespace IIoT.Edge.Presentation.Panels.Features.SysLog;

/// <summary>
/// 系统日志面板视图模型。
/// </summary>
public class LogViewModel : PresentationViewModelBase
{
    private readonly ISystemLogDisplayStore _logDisplayStore;

    public override string ViewId => "Core.SysLog";
    public override string ViewTitle => "系统日志";

    public ObservableCollection<LogEntry> Entries => _logDisplayStore.Entries;

    public bool HasEntries => Entries.Count > 0;
    public bool IsLogEmpty => !HasEntries;

    public ICommand ClearCommand { get; }

    public LogViewModel(ISystemLogDisplayStore logDisplayStore)
    {
        _logDisplayStore = logDisplayStore;

        LayoutRow = 1;
        LayoutColumn = 1;

        Entries.CollectionChanged += OnEntriesChanged;
        ClearCommand = new BaseCommand(_ => _logDisplayStore.Clear());
    }

    private void OnEntriesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        OnPropertyChanged(nameof(HasEntries));
        OnPropertyChanged(nameof(IsLogEmpty));
    }
}
