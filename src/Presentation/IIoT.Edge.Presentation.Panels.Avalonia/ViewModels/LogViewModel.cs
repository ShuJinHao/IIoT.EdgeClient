using CommunityToolkit.Mvvm.Input;
using IIoT.Edge.Application.Abstractions.Config;
using IIoT.Edge.Application.Abstractions.Logging;
using IIoT.Edge.Application.Abstractions.Modules;
using IIoT.Edge.Application.Common.Models;
using IIoT.Edge.UI.Avalonia.Mvvm;
using IIoT.Edge.UI.Avalonia.Services;
using System.Collections.ObjectModel;

namespace IIoT.Edge.Presentation.Panels.Avalonia.ViewModels;

public sealed partial class LogViewModel : AvaloniaViewModelBase
{
    private const int MaxEntries = 300;
    private readonly EdgeRuntimePaths _runtimePaths;
    private readonly IStartupDiagnosticsStore _diagnosticsStore;
    private readonly IAvaloniaDispatcherService _dispatcherService;
    private LogFileOption? _selectedLogFile;

    public LogViewModel(
        ILogService logService,
        EdgeRuntimePaths runtimePaths,
        IStartupDiagnosticsStore diagnosticsStore,
        IAvaloniaDispatcherService dispatcherService)
    {
        _runtimePaths = runtimePaths;
        _diagnosticsStore = diagnosticsStore;
        _dispatcherService = dispatcherService;

        LoadInitialEntries();
        logService.EntryAdded += HandleEntryAdded;
    }

    public override string ViewId => "Core.SysLog";

    public ObservableCollection<LogEntry> Entries { get; } = [];

    public ObservableCollection<LogFileOption> LogFiles { get; } = [];

    public LogFileOption? SelectedLogFile
    {
        get => _selectedLogFile;
        set
        {
            if (SetProperty(ref _selectedLogFile, value))
            {
                LoadInitialEntries();
            }
        }
    }

    [RelayCommand]
    private void Clear() => Entries.Clear();

    [RelayCommand]
    private void Refresh() => LoadInitialEntries();

    private void LoadInitialEntries()
    {
        RefreshLogFiles();
        Entries.Clear();
        foreach (var entry in ReadRuntimeLogEntries().Take(MaxEntries))
        {
            Entries.Add(entry);
        }

        if (Entries.Count == 0)
        {
            Entries.Add(CreateStartupDiagnosticsEntry());
        }
    }

    private IEnumerable<LogEntry> ReadRuntimeLogEntries()
    {
        if (SelectedLogFile is not null)
        {
            return ReadFileTail(SelectedLogFile.Path).Reverse();
        }

        if (!Directory.Exists(_runtimePaths.LogDirectory))
        {
            return [];
        }

        return Directory.EnumerateFiles(_runtimePaths.LogDirectory, "*.log", SearchOption.TopDirectoryOnly)
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .Take(3)
            .SelectMany(ReadFileTail)
            .Reverse();
    }

    private void RefreshLogFiles()
    {
        var selectedPath = SelectedLogFile?.Path;
        var files = Directory.Exists(_runtimePaths.LogDirectory)
            ? Directory.EnumerateFiles(_runtimePaths.LogDirectory, "*.log", SearchOption.TopDirectoryOnly)
                .OrderByDescending(File.GetLastWriteTimeUtc)
                .Select(path => new LogFileOption(
                    Path.GetFileName(path),
                    path,
                    File.GetLastWriteTime(path).ToString("yyyy-MM-dd HH:mm:ss")))
                .ToArray()
            : [];

        LogFiles.Clear();
        foreach (var file in files)
        {
            LogFiles.Add(file);
        }

        if (LogFiles.Count == 0)
        {
            _selectedLogFile = null;
            OnPropertyChanged(nameof(SelectedLogFile));
            return;
        }

        var selected = LogFiles.FirstOrDefault(file => string.Equals(file.Path, selectedPath, StringComparison.OrdinalIgnoreCase))
            ?? LogFiles.First();
        if (!ReferenceEquals(_selectedLogFile, selected))
        {
            _selectedLogFile = selected;
            OnPropertyChanged(nameof(SelectedLogFile));
        }
    }

    private static IEnumerable<LogEntry> ReadFileTail(string path)
    {
        try
        {
            return File.ReadLines(path)
                .Reverse()
                .Where(static line => !string.IsNullOrWhiteSpace(line))
                .Take(100)
                .Select(ParseLine)
                .ToArray();
        }
        catch (IOException)
        {
            return [];
        }
        catch (UnauthorizedAccessException)
        {
            return [];
        }
    }

    private static LogEntry ParseLine(string line)
    {
        var level = "INFO";
        foreach (var candidate in new[] { "FATAL", "ERROR", "WARN", "INFO", "DEBUG" })
        {
            if (line.Contains(candidate, StringComparison.OrdinalIgnoreCase))
            {
                level = candidate;
                break;
            }
        }

        return new LogEntry { Time = DateTime.Now, Level = level, Message = line };
    }

    private LogEntry CreateStartupDiagnosticsEntry()
    {
        var report = _diagnosticsStore.Current;
        var message = report.GeneratedAt == DateTime.MinValue
            ? "迁移运行目录暂无日志，启动诊断尚未生成。"
            : $"迁移运行目录暂无日志。启动诊断：模块 {report.ModuleRegistrations.Count} 个，问题 {report.Issues.Count} 个，生成时间 {report.GeneratedAt:yyyy-MM-dd HH:mm:ss}。";
        return new LogEntry { Time = DateTime.Now, Level = "INFO", Message = message };
    }

    private void HandleEntryAdded(LogEntry entry)
    {
        _ = _dispatcherService.InvokeAsync(() =>
        {
            Entries.Insert(0, entry);
            while (Entries.Count > MaxEntries)
            {
                Entries.RemoveAt(Entries.Count - 1);
            }
        });
    }
}

public sealed record LogFileOption(string FileName, string Path, string UpdatedAt)
{
    public string DisplayText => $"{FileName}（{UpdatedAt}）";
}
