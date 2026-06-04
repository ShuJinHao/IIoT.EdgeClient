using IIoT.Edge.Application.Features.Production.Monitor;
using IIoT.Edge.UI.Shared.Avalonia.Controls;
using IIoT.Edge.UI.Shared.Localization;
using IIoT.Edge.UI.Shared.Mvvm;

namespace IIoT.Edge.Presentation.Navigation.Features.Production.Monitor;

public sealed class MonitorTabItemViewModel : BaseNotifyPropertyChanged
{
    private readonly IAppLanguageService _languageService;
    private bool _isSelected;

    public MonitorTabItemViewModel(
        IAppLanguageService languageService,
        string key,
        string titleResourceKey,
        string titleFallback)
    {
        _languageService = languageService;
        Key = key;
        TitleResourceKey = titleResourceKey;
        TitleFallback = titleFallback;
    }

    public string Key { get; }

    public string TitleResourceKey { get; }

    public string TitleFallback { get; }

    public string Title => _languageService.GetString(TitleResourceKey, TitleFallback);

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (_isSelected == value)
            {
                return;
            }

            _isSelected = value;
            OnPropertyChanged();
        }
    }

    public void RefreshLanguage()
        => OnPropertyChanged(nameof(Title));

    public override string ToString()
        => Title;
}

public sealed class MonitorCellDebugItemViewModel : IEquatable<MonitorCellDebugItemViewModel>
{
    public MonitorCellDebugItemViewModel(MonitorCellDebugSnapshot snapshot)
    {
        Snapshot = snapshot;
    }

    public MonitorCellDebugSnapshot Snapshot { get; }

    public string DeviceName => Snapshot.DeviceName;

    public string InternalKey => Snapshot.InternalKey;

    public string DisplayLabel => Snapshot.DisplayLabel;

    public string ProcessType => Snapshot.ProcessType;

    public string RuntimeStatusText => Snapshot.RuntimeStatusText;

    public string CompletedTimeText => Snapshot.CompletedTimeText;

    public IReadOnlyList<MonitorSnapshotRow> FieldRows => Snapshot.FieldRows;

    public bool Equals(MonitorCellDebugItemViewModel? other)
        => other is not null && EqualityComparer<MonitorCellDebugSnapshot>.Default.Equals(Snapshot, other.Snapshot);

    public override bool Equals(object? obj)
        => Equals(obj as MonitorCellDebugItemViewModel);

    public override int GetHashCode()
        => EqualityComparer<MonitorCellDebugSnapshot>.Default.GetHashCode(Snapshot);
}

public sealed record MonitorStatusItemVm(string Label, string Value);

public sealed record MonitorStateMachineTaskItemViewModel(
    string DisplayName,
    string AvailabilityStatusText,
    string StepText,
    string StepValueText,
    string DetailText,
    bool IsUnavailable,
    bool IsHeartbeatLike,
    EdgeVisualStatus VisualStatus)
{
    public string Title => DisplayName;

    public string StatusText => IsHeartbeatLike ? StepValueText : AvailabilityStatusText;

    public string Description => StepText;

    public string Detail => DetailText;

    public EdgeVisualStatus Status => VisualStatus;
}
