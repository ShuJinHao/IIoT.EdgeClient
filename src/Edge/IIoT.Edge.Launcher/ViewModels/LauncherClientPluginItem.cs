using IIoT.Edge.Application.Abstractions.Updates;
using IIoT.Edge.Launcher.Services;
using IIoT.Edge.UI.Shared.Mvvm;

namespace IIoT.Edge.Launcher.ViewModels;

public sealed class LauncherClientPluginItem : BaseNotifyPropertyChanged
{
    private string _currentVersion;
    private string _statusText;
    private string _actionText;

    public LauncherClientPluginItem(
        string moduleId,
        string displayName,
        string currentVersion,
        string targetVersion,
        string packageSizeText,
        string detailText,
        bool canInstallOrUpdate,
        string statusKind,
        string statusText,
        string actionText,
        EdgeVersionStatus state,
        LauncherVersionOptionItem? versionOption = null,
        string componentKindText = "",
        string publishedAtText = "",
        string releaseNotesText = "",
        LauncherVersionComponentItem? versionComponent = null,
        string historyActionText = "",
        string emptyHistoryText = "")
    {
        ModuleId = moduleId;
        DisplayName = displayName;
        _currentVersion = currentVersion;
        TargetVersion = targetVersion;
        PackageSizeText = packageSizeText;
        DetailText = detailText;
        CanInstallOrUpdate = canInstallOrUpdate;
        StatusKind = statusKind;
        _statusText = statusText;
        _actionText = actionText;
        State = state;
        VersionOption = versionOption;
        ComponentKindText = string.IsNullOrWhiteSpace(componentKindText) ? "-" : componentKindText;
        PublishedAtText = string.IsNullOrWhiteSpace(publishedAtText) ? "-" : publishedAtText;
        ReleaseNotesText = string.IsNullOrWhiteSpace(releaseNotesText) ? "-" : releaseNotesText;
        VersionComponent = versionComponent;
        HistoryCount = versionComponent?.Versions.Count ?? 0;
        HasVersionHistory = VersionComponent is not null && HistoryCount > 0;
        HistoryActionText = historyActionText;
        EmptyHistoryText = string.IsNullOrWhiteSpace(emptyHistoryText) ? "-" : emptyHistoryText;
    }

    public string ModuleId { get; }

    public string DisplayName { get; }

    public string CurrentVersion
    {
        get => _currentVersion;
        set
        {
            if (_currentVersion == value)
            {
                return;
            }

            _currentVersion = value;
            OnPropertyChanged();
        }
    }

    public string TargetVersion { get; }

    public string PackageSizeText { get; }

    public string PackageSizeDisplayText => string.IsNullOrWhiteSpace(PackageSizeText) ? "-" : PackageSizeText;

    public string DetailText { get; }

    public bool CanInstallOrUpdate { get; }

    public bool HasNoInstallOrUpdate => !CanInstallOrUpdate;

    public string StatusKind { get; }

    public EdgeVersionStatus State { get; }

    public LauncherVersionOptionItem? VersionOption { get; }

    public string ComponentKindText { get; }

    public string PublishedAtText { get; }

    public string ReleaseNotesText { get; }

    public LauncherVersionComponentItem? VersionComponent { get; }

    public int HistoryCount { get; }

    public bool HasVersionHistory { get; }

    public bool HasNoVersionHistory => !HasVersionHistory;

    public string HistoryActionText { get; }

    public string EmptyHistoryText { get; }

    public string StatusText
    {
        get => _statusText;
        set
        {
            if (_statusText == value)
            {
                return;
            }

            _statusText = value;
            OnPropertyChanged();
        }
    }

    public string ActionText
    {
        get => _actionText;
        set
        {
            if (_actionText == value)
            {
                return;
            }

            _actionText = value;
            OnPropertyChanged();
        }
    }
}
