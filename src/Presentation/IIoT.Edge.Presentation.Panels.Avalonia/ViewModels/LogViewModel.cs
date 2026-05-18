using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using IIoT.Edge.Application.Abstractions.Config;
using IIoT.Edge.Application.Abstractions.Logging;
using IIoT.Edge.Application.Abstractions.Modules;
using IIoT.Edge.Application.Common.Models;
using IIoT.Edge.UI.Avalonia.Localization;
using IIoT.Edge.UI.Avalonia.Mvvm;
using IIoT.Edge.UI.Avalonia.Services;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Text.RegularExpressions;

namespace IIoT.Edge.Presentation.Panels.Avalonia.ViewModels;

public sealed partial class LogViewModel : AvaloniaViewModelBase
{
    private const int MaxEntries = 300;
    private static readonly Regex LeadingTimestampPattern = new(
        @"^\s*(?:\[(?<timestamp>\d{4}[-/]\d{2}[-/]\d{2}[ T]\d{2}:\d{2}:\d{2}(?:[.,]\d{1,7})?(?:Z|[+-]\d{2}:?\d{2})?)\]|(?<timestamp>\d{4}[-/]\d{2}[-/]\d{2}[ T]\d{2}:\d{2}:\d{2}(?:[.,]\d{1,7})?(?:Z|[+-]\d{2}:?\d{2})?))",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly string[] TimestampFormats =
    [
        "yyyy-MM-dd HH:mm:ss",
        "yyyy-MM-dd HH:mm:ss.FFFFFFF",
        "yyyy/MM/dd HH:mm:ss",
        "yyyy/MM/dd HH:mm:ss.FFFFFFF",
        "yyyy-MM-ddTHH:mm:ssK",
        "yyyy-MM-ddTHH:mm:ss.FFFFFFFK"
    ];

    private readonly ILogService _logService;
    private readonly EdgeRuntimePaths _runtimePaths;
    private readonly IStartupDiagnosticsStore _diagnosticsStore;
    private readonly IAvaloniaDispatcherService _dispatcherService;
    private readonly IAvaloniaLanguageService _languageService;
    private LogFileOption? _selectedLogFile;

    public LogViewModel(
        ILogService logService,
        EdgeRuntimePaths runtimePaths,
        IStartupDiagnosticsStore diagnosticsStore,
        IAvaloniaDispatcherService dispatcherService,
        IAvaloniaLanguageService languageService)
    {
        _logService = logService;
        _runtimePaths = runtimePaths;
        _diagnosticsStore = diagnosticsStore;
        _dispatcherService = dispatcherService;
        _languageService = languageService;

        LoadInitialEntries();
        logService.EntryAdded += HandleEntryAdded;
        _languageService.LanguageChanged += (_, _) => UpdateLogMetrics();
    }

    public override string ViewId => "Core.SysLog";

    public ObservableCollection<LogEntryRow> Entries { get; } = [];

    public ObservableCollection<LogFileOption> LogFiles { get; } = [];

    [ObservableProperty]
    private int totalCount;

    [ObservableProperty]
    private int warningCount;

    [ObservableProperty]
    private int errorCount;

    [ObservableProperty]
    private string latestLogSummary = string.Empty;

    [ObservableProperty]
    private bool isLogEmpty;

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
    private void Clear()
    {
        Entries.Clear();
        UpdateLogMetrics();
    }

    [RelayCommand]
    private void Refresh() => LoadInitialEntries();

    private void LoadInitialEntries()
    {
        RefreshLogFiles();
        Entries.Clear();
        foreach (var entry in ReadRuntimeLogEntries().Take(MaxEntries))
        {
            Entries.Add(CreateRow(entry));
        }

        UpdateLogMetrics();
    }

    private IEnumerable<LogEntry> ReadRuntimeLogEntries()
    {
        if (SelectedLogFile is not null)
        {
            return ReadFileTail(SelectedLogFile.Path);
        }

        var bufferedEntries = _logService is ILogDisplayService displayService
            ? displayService.Entries
                .Reverse()
                .Take(MaxEntries)
                .ToArray()
            : Array.Empty<LogEntry>();

        if (!Directory.Exists(_runtimePaths.LogDirectory))
        {
            return bufferedEntries;
        }

        var fileEntries = Directory.EnumerateFiles(_runtimePaths.LogDirectory, "*.log", SearchOption.TopDirectoryOnly)
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .Take(3)
            .SelectMany(ReadFileTail)
            .ToArray();

        return bufferedEntries
            .Concat(fileEntries)
            .Take(MaxEntries);
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

        return new LogEntry
        {
            Time = TryParseTimestamp(line, out var timestamp) ? timestamp : DateTime.MinValue,
            Level = level,
            Message = line
        };
    }

    private static bool TryParseTimestamp(string line, out DateTime timestamp)
    {
        timestamp = DateTime.MinValue;
        var match = LeadingTimestampPattern.Match(line);
        if (!match.Success)
        {
            return false;
        }

        var value = match.Groups["timestamp"].Value.Replace(',', '.');
        if (DateTimeOffset.TryParseExact(
                value,
                TimestampFormats,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AllowWhiteSpaces | DateTimeStyles.AssumeLocal,
                out var offsetTimestamp))
        {
            timestamp = offsetTimestamp.LocalDateTime;
            return true;
        }

        if (DateTime.TryParseExact(
                value,
                TimestampFormats,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AllowWhiteSpaces | DateTimeStyles.AssumeLocal,
                out var localTimestamp))
        {
            timestamp = localTimestamp;
            return true;
        }

        if (DateTimeOffset.TryParse(
                value,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AllowWhiteSpaces | DateTimeStyles.AssumeLocal,
                out offsetTimestamp))
        {
            timestamp = offsetTimestamp.LocalDateTime;
            return true;
        }

        if (DateTime.TryParse(
                value,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AllowWhiteSpaces | DateTimeStyles.AssumeLocal,
                out localTimestamp))
        {
            timestamp = localTimestamp;
            return true;
        }

        return false;
    }

    private string CreateStartupDiagnosticsSummary()
    {
        var report = _diagnosticsStore.Current;
        return report.GeneratedAt == DateTime.MinValue
            ? _languageService.GetText("Panels_Log_NoRuntimeLog")
            : string.Format(
                _languageService.GetText("Panels_Log_StartupDiagnosticsFormat"),
                report.ModuleRegistrations.Count,
                report.Issues.Count,
                report.GeneratedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss"));
    }

    private void UpdateLogMetrics()
    {
        TotalCount = Entries.Count;
        WarningCount = Entries.Count(static entry => entry.IsWarning);
        ErrorCount = Entries.Count(static entry => entry.IsError);
        IsLogEmpty = Entries.Count == 0;
        LatestLogSummary = Entries.FirstOrDefault()?.Message ?? CreateStartupDiagnosticsSummary();
    }

    private void HandleEntryAdded(LogEntry entry)
    {
        _ = _dispatcherService.InvokeAsync(() =>
        {
            Entries.Insert(0, CreateRow(entry));
            while (Entries.Count > MaxEntries)
            {
                Entries.RemoveAt(Entries.Count - 1);
            }

            UpdateLogMetrics();
        });
    }

    private LogEntryRow CreateRow(LogEntry entry)
        => LogEntryRow.From(entry, _languageService.GetText("Panels_Log_UnknownTime"));
}

public sealed record LogEntryRow(
    DateTime Time,
    string TimeText,
    string Level,
    string Message,
    bool IsWarning,
    bool IsError,
    bool IsDebug)
{
    public static LogEntryRow From(LogEntry entry, string unknownTimeText)
    {
        var level = string.IsNullOrWhiteSpace(entry.Level)
            ? "INFO"
            : entry.Level.ToUpperInvariant();

        return new LogEntryRow(
            entry.Time,
            entry.Time == DateTime.MinValue
                ? unknownTimeText
                : entry.Time.ToString("HH:mm:ss", CultureInfo.CurrentCulture),
            level,
            entry.Message,
            IsWarningLevel(level),
            IsErrorLevel(level),
            level.Contains("DEBUG", StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsWarningLevel(string level)
        => level.Contains("WARN", StringComparison.OrdinalIgnoreCase);

    private static bool IsErrorLevel(string level)
        => level.Contains("ERROR", StringComparison.OrdinalIgnoreCase)
           || level.Contains("FATAL", StringComparison.OrdinalIgnoreCase);
}

public sealed record LogFileOption(string FileName, string Path, string UpdatedAt)
{
    public string DisplayText => $"{FileName} - {UpdatedAt}";
}
