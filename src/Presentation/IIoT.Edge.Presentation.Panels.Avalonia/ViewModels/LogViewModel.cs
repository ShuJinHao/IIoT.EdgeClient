using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using IIoT.Edge.Application.Abstractions.Logging;
using IIoT.Edge.Application.Common.Models;
using IIoT.Edge.UI.Avalonia.Mvvm;
using IIoT.Edge.UI.Avalonia.Services;
using System.Collections.ObjectModel;

namespace IIoT.Edge.Presentation.Panels.Avalonia.ViewModels;

public sealed partial class LogViewModel : AvaloniaViewModelBase
{
    private readonly IAvaloniaDispatcherService _dispatcherService;

    public LogViewModel(
        ILogService logService,
        IAvaloniaDispatcherService dispatcherService)
    {
        _dispatcherService = dispatcherService;
        Entries.Add(new LogEntry { Time = DateTime.Now, Level = "INFO", Message = "Avalonia Shell 已加载，后台运行链路未启动。" });
        logService.EntryAdded += HandleEntryAdded;
    }

    public override string ViewId => "Core.SysLog";

    public ObservableCollection<LogEntry> Entries { get; } = [];

    [RelayCommand]
    private void Clear() => Entries.Clear();

    private void HandleEntryAdded(LogEntry entry)
    {
        _ = _dispatcherService.InvokeAsync(() =>
        {
            Entries.Insert(0, entry);
            while (Entries.Count > 300)
            {
                Entries.RemoveAt(Entries.Count - 1);
            }
        });
    }
}
