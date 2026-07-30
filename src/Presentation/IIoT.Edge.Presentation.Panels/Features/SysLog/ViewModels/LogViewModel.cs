using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Windows.Input;
using Avalonia.Threading;
using IIoT.Edge.Module.Contracts.Logging;
using IIoT.Edge.Presentation.Panels.Features.DeviceSelection;
using IIoT.Edge.UI.Shared.Localization;
using IIoT.Edge.UI.Shared.Mvvm;
using IIoT.Edge.UI.Shared.PluginSystem;

namespace IIoT.Edge.Presentation.Panels.Features.SysLog;

/// <summary>
/// 系统日志面板视图模型。
/// </summary>
public class LogViewModel : PresentationViewModelBase
{
    private const string AllFilterKey = IDeviceSelectionService.AllFilterKey;

    private readonly ISystemLogDisplayStore _logDisplayStore;
    private readonly ISystemLogDisplayProjector _logProjector;
    private readonly IAppLanguageService _languageService;
    private readonly IDeviceSelectionService _deviceSelectionService;
    private LogDeviceFilterOption? _selectedDeviceFilter;

    public override string ViewId => "Core.SysLog";
    public override string ViewTitle => "系统日志";

    public ObservableCollection<LogEntry> Entries { get; } = new();

    public ObservableCollection<LogDeviceFilterOption> DeviceFilters { get; } = new();

    public LogDeviceFilterOption? SelectedDeviceFilter
    {
        get => _selectedDeviceFilter;
        set
        {
            if (Equals(_selectedDeviceFilter, value))
            {
                return;
            }

            _selectedDeviceFilter = value;
            OnPropertyChanged();
            RebuildDisplayedEntries();
        }
    }

    public bool HasEntries => Entries.Count > 0;
    public bool IsLogEmpty => !HasEntries;

    public ICommand ClearCommand { get; }

    public LogViewModel(
        ISystemLogDisplayStore logDisplayStore,
        ISystemLogDisplayProjector logProjector,
        IAppLanguageService languageService,
        IDeviceSelectionService deviceSelectionService)
    {
        _logDisplayStore = logDisplayStore;
        _logProjector = logProjector;
        _languageService = languageService;
        _deviceSelectionService = deviceSelectionService;

        LayoutRow = 1;
        LayoutColumn = 1;

        DeviceFilters.Add(CreateAllFilter());
        _selectedDeviceFilter = DeviceFilters[0];
        Entries.CollectionChanged += OnEntriesChanged;
        _logDisplayStore.Entries.CollectionChanged += OnSourceEntriesChanged;
        _languageService.LanguageChanged += OnLanguageChanged;
        _deviceSelectionService.SelectionChanged += OnSharedDeviceSelectionChanged;
        ClearCommand = new BaseCommand(_ => _logDisplayStore.Clear());
        RebuildDisplayedEntries();
    }

    private void OnEntriesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        OnPropertyChanged(nameof(HasEntries));
        OnPropertyChanged(nameof(IsLogEmpty));
    }

    private void OnSourceEntriesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        RebuildDeviceFilters();
        RebuildDisplayedEntries();
    }

    private void OnLanguageChanged(object? sender, EventArgs e)
        => RunOnUiThread(() =>
        {
            var selectedKey = SelectedDeviceFilter?.Key ?? AllFilterKey;
            RebuildDeviceFilters(selectedKey);
        });

    private void OnSharedDeviceSelectionChanged(object? sender, EventArgs e)
        => RunOnUiThread(() =>
        {
            var selectedKey = _deviceSelectionService.SelectedDeviceKey;
            var option = DeviceFilters.FirstOrDefault(filter =>
                string.Equals(filter.Key, selectedKey, StringComparison.OrdinalIgnoreCase));
            if (option is null)
            {
                option = new LogDeviceFilterOption(selectedKey, selectedKey);
                DeviceFilters.Add(option);
            }

            SelectedDeviceFilter = option;
        });

    private static void RunOnUiThread(Action action)
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            action();
            return;
        }

        Dispatcher.UIThread.Post(action);
    }

    private void RebuildDeviceFilters(string? preferredKey = null)
    {
        preferredKey ??= SelectedDeviceFilter?.Key ?? AllFilterKey;
        var devices = _logProjector.ExtractDeviceNames(_logDisplayStore.Entries)
            .Select(static deviceName => new LogDeviceFilterOption(deviceName, deviceName))
            .ToArray();

        DeviceFilters.Clear();
        DeviceFilters.Add(CreateAllFilter());
        foreach (var device in devices)
        {
            DeviceFilters.Add(device);
        }

        if (!string.Equals(preferredKey, AllFilterKey, StringComparison.OrdinalIgnoreCase)
            && DeviceFilters.All(filter => !string.Equals(filter.Key, preferredKey, StringComparison.OrdinalIgnoreCase)))
        {
            DeviceFilters.Add(new LogDeviceFilterOption(preferredKey, preferredKey));
        }

        SelectedDeviceFilter = DeviceFilters.FirstOrDefault(filter =>
                string.Equals(filter.Key, preferredKey, StringComparison.OrdinalIgnoreCase))
            ?? DeviceFilters[0];
    }

    private void RebuildDisplayedEntries()
    {
        var selectedKey = SelectedDeviceFilter?.Key ?? AllFilterKey;
        var entries = string.Equals(selectedKey, AllFilterKey, StringComparison.Ordinal)
            ? _logProjector.BuildAggregatedEntries(_logDisplayStore.Entries)
            : _logProjector.BuildDeviceEntries(
                _logDisplayStore.Entries,
                _deviceSelectionService.SelectedPlcCode ?? selectedKey);

        Entries.Clear();
        foreach (var entry in entries)
        {
            Entries.Add(entry);
        }
    }

    private LogDeviceFilterOption CreateAllFilter()
        => new(
            AllFilterKey,
            _languageService.GetString("Panels_Filter_AllOrSummary", "全部/汇总"));

    public sealed record LogDeviceFilterOption(string Key, string DisplayName)
    {
        public override string ToString() => DisplayName;
    }
}
